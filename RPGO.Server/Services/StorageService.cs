using RPGGame.Server.Network;
using RPGGame.Server.Repositories;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.Services;

/// <summary>
/// Сервис персистентного хранилища (склада) игрока.
/// Хранит предметы в таблице storage_items (JSON-based persistence через List<Item>).
/// </summary>
public sealed class StorageService
{
    private readonly GameWorld _world;
    private readonly INetworkHub _hub;

    public int StorageX { get; set; }
    public int StorageY { get; set; }

    public StorageService(GameWorld world, INetworkHub hub)
    {
        _world = world;
        _hub = hub;
    }

    public void SetPosition(int x, int y)
    {
        StorageX = x;
        StorageY = y;
    }

    public async Task OnPlayerInteractAsync(Player player)
    {
        var client = _world.FindClientByPlayer(player);
        if (client == null) return;

        var items = LoadFromDb(player.Name);
        await _hub.SendToClient(client, new GameMessage
        {
            Type = "storage_open",
            Data = new { Items = items, Slots = Balance.StorageSlots }
        });
    }

    public async Task DepositAsync(Player player, string itemId, int quantity)
    {
        var client = _world.FindClientByPlayer(player);
        if (client == null) return;

        var inventoryItem = player.Inventory.FirstOrDefault(i => i.Id == itemId);
        if (inventoryItem == null)
        {
            await _hub.SendToClient(client, new GameMessage
            {
                Type = "error",
                Data = new { Code = ErrorCodes.ItemNotFound, Message = "Предмет не найден в инвентаре!" }
            });
            return;
        }

        int actualQuantity = InventoryHelper.CountByItem(player, inventoryItem);
        if (actualQuantity != inventoryItem.Quantity)
        {
            Log.Warn($"[Deposit] Player {player.Name} item {inventoryItem.Id} client qty={inventoryItem.Quantity} server total={actualQuantity}");
        }

        var storageItems = LoadFromDb(player.Name);
        int currentSlots = storageItems.Count;
        bool isStackable = Balance.MaxStackForType(inventoryItem.Type) > 1;

        if (isStackable)
        {
            int maxStack = Balance.MaxStackForType(inventoryItem.Type);
            int canDeposit = Math.Min(quantity, inventoryItem.Quantity);
            int remaining = canDeposit;

            // Пытаемся добавить в существующий стек
            foreach (var si in storageItems.Where(s => InventoryHelper.StackMatch(s, inventoryItem) && s.Quantity < maxStack))
            {
                if (remaining <= 0) break;
                int room = maxStack - si.Quantity;
                int add = Math.Min(room, remaining);
                si.Quantity += add;
                remaining -= add;
            }

            // Остаток — новые записи
            while (remaining > 0)
            {
                if (currentSlots >= Balance.StorageSlots)
                {
                    await _hub.SendToClient(client, new GameMessage
                    {
                        Type = "error",
                        Data = new { Code = ErrorCodes.NoSpace, Message = "Склад заполнен!" }
                    });
                    break;
                }
                int take = Math.Min(maxStack, remaining);
                var clone = inventoryItem.Clone();
                clone.Id = Guid.NewGuid().ToString();
                clone.Quantity = take;
                clone.MaxStack = maxStack;
                storageItems.Add(clone);
                currentSlots++;
                remaining -= take;
            }

            int deposited = canDeposit - remaining;
            if (deposited > 0)
            {
                InventoryHelper.RemoveQuantity(player, inventoryItem, deposited);
            }
        }
        else
        {
            int toDeposit = Math.Min(quantity, inventoryItem.Quantity);
            int deposited = 0;
            for (int k = 0; k < toDeposit; k++)
            {
                if (currentSlots >= Balance.StorageSlots)
                {
                    await _hub.SendToClient(client, new GameMessage
                    {
                        Type = "error",
                        Data = new { Code = ErrorCodes.NoSpace, Message = "Склад заполнен!" }
                    });
                    break;
                }
                var clone = inventoryItem.Clone();
                clone.Id = Guid.NewGuid().ToString();
                clone.Quantity = 1;
                clone.MaxStack = 1;
                storageItems.Add(clone);
                currentSlots++;
                deposited++;
            }

            if (deposited > 0)
            {
                InventoryHelper.RemoveFromRecord(player, inventoryItem.Id, deposited);
            }
        }

        // Атомарное сохранение: инвентарь и склад пишутся одной транзакцией,
        // чтобы при вылете/перезапуске предметы не пропадали и не дублировались.
        DatabaseManager.SavePlayerProgress(player, storageItems);
        await SendStorageUpdate(client, player, storageItems);
        await _hub.SendInventoryAndStatus(client, player);
    }

    public async Task WithdrawAsync(Player player, string itemId, int quantity)
    {
        var client = _world.FindClientByPlayer(player);
        if (client == null) return;

        var storageItems = LoadFromDb(player.Name);
        var storageItem = storageItems.FirstOrDefault(i => i.Id == itemId);
        if (storageItem == null)
        {
            await _hub.SendToClient(client, new GameMessage
            {
                Type = "error",
                Data = new { Code = ErrorCodes.ItemNotFound, Message = "Предмет не найден на складе!" }
            });
            return;
        }

        int toWithdraw = Math.Min(quantity, storageItem.Quantity);
        if (toWithdraw <= 0) return;

        bool isStackable = Balance.MaxStackForType(storageItem.Type) > 1;

        if (isStackable)
        {
            int maxStack = Balance.MaxStackForType(storageItem.Type);
            int remaining = toWithdraw;

            // Пытаемся добавить в существующий стек инвентаря
            var existingStacks = player.Inventory
                .Where(i => InventoryHelper.StackMatch(i, storageItem) && i.Quantity < maxStack)
                .OrderByDescending(i => i.Quantity)
                .ToList();

            foreach (var stack in existingStacks)
            {
                if (remaining <= 0) break;
                int room = maxStack - stack.Quantity;
                int add = Math.Min(room, remaining);
                stack.Quantity += add;
                remaining -= add;
            }

            // Остаток — новые записи в инвентарь
            while (remaining > 0)
            {
                int take = Math.Min(maxStack, remaining);
                var clone = storageItem.Clone();
                clone.Id = Guid.NewGuid().ToString();
                clone.Quantity = take;
                clone.MaxStack = maxStack;
                player.Inventory.Add(clone);
                remaining -= take;
            }
        }
        else
        {
            for (int i = 0; i < toWithdraw; i++)
            {
                var clone = storageItem.Clone();
                clone.Id = Guid.NewGuid().ToString();
                clone.Quantity = 1;
                clone.MaxStack = 1;
                player.Inventory.Add(clone);
            }
        }

        storageItem.Quantity -= toWithdraw;
        if (storageItem.Quantity <= 0)
            storageItems.Remove(storageItem);

        // Атомарное сохранение инвентаря и склада одной транзакцией.
        DatabaseManager.SavePlayerProgress(player, storageItems);
        await SendStorageUpdate(client, player, storageItems);
        await _hub.SendInventoryAndStatus(client, player);
    }

    private async Task SendStorageUpdate(ClientConnection client, Player player, List<Item> storageItems)
    {
        await _hub.SendToClient(client, new GameMessage
        {
            Type = "storage_update",
            Data = new { Items = storageItems, Slots = Balance.StorageSlots }
        });
    }

    public List<Item> LoadFromDb(string playerName)
    {
        lock (Db.Lock)
        {
            using var connection = Db.Open();
            var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT item_id, template_id, name, type, value, quantity, max_stack,
                description, bonus_defense, bonus_phys_attack, bonus_mag_attack,
                bonus_max_health, heal_amount, restore_mana, weapon_subtype,
                damage_min, damage_max, attack_range
                FROM storage_items WHERE player_name = $name";
            cmd.Parameters.AddWithValue("$name", playerName);

            var items = new List<Item>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new Item
                {
                    Id = reader.GetString(0),
                    TemplateId = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    Value = reader.GetInt32(4),
                    Quantity = reader.GetInt32(5),
                    MaxStack = reader.GetInt32(6),
                    Description = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    BonusDefense = reader.GetInt32(8),
                    BonusPhysAttack = reader.GetInt32(9),
                    BonusMagAttack = reader.GetInt32(10),
                    MaxHealthBonus = reader.GetInt32(11),
                    HealAmount = reader.GetInt32(12),
                    RestoreMana = reader.GetInt32(13),
                    WeaponSubtype = reader.IsDBNull(14) ? "" : reader.GetString(14),
                    DamageMin = reader.GetInt32(15),
                    DamageMax = reader.GetInt32(16),
                    AttackRange = reader.GetInt32(17)
                });
            }
            return InventoryHelper.ConsolidateStackables(items);
        }
    }
}
