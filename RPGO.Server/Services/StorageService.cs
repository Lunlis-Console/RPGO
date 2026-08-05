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
            await _hub.SendError(client, ErrorCodes.ItemNotFound, "Предмет не найден в инвентаре!");
            return;
        }

        var storageItems = LoadFromDb(player.Name);
        int canDeposit = Math.Min(quantity, inventoryItem.Quantity);

        int remaining = InventoryHelper.AddToStackable(storageItems, inventoryItem, canDeposit,
            slotLimit: Balance.StorageSlots);

        if (remaining > 0)
            await _hub.SendError(client, ErrorCodes.NoSpace, "Склад заполнен!");

        int deposited = canDeposit - Math.Abs(remaining);
        if (remaining < 0) deposited = canDeposit;

        if (deposited > 0)
        {
            if (InventoryHelper.StackCapFor(inventoryItem) > 1)
                InventoryHelper.RemoveQuantity(player, inventoryItem, deposited);
            else
                InventoryHelper.RemoveFromRecord(player, inventoryItem.Id, deposited);
        }

        if (deposited > 0)
        {
            DatabaseManager.SavePlayerProgress(player, storageItems);
            await SendStorageUpdate(client, player, storageItems);
            await _hub.SendInventoryAndStatus(client, player);
        }
    }

    public async Task WithdrawAsync(Player player, string itemId, int quantity)
    {
        var client = _world.FindClientByPlayer(player);
        if (client == null) return;

        var storageItems = LoadFromDb(player.Name);
        var storageItem = storageItems.FirstOrDefault(i => i.Id == itemId);
        if (storageItem == null)
        {
            await _hub.SendError(client, ErrorCodes.ItemNotFound, "Предмет не найден на складе!");
            return;
        }

        int toWithdraw = Math.Min(quantity, storageItem.Quantity);
        if (toWithdraw <= 0) return;

        InventoryHelper.AddToStackable(player.Inventory, storageItem, toWithdraw);

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
                damage_min, damage_max, attack_range, required_level
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
                    AttackRange = reader.GetInt32(17),
                    RequiredLevel = reader.IsDBNull(18) ? 0 : reader.GetInt32(18)
                });
            }
            return InventoryHelper.ConsolidateStackables(items);
        }
    }
}
