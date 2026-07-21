using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class InventoryHelper
{
    // Добавляет предмет в инвентарь игрока, стакая стакаемые предметы
    // (MaxStack > 1 и задан TemplateId) по template_id в памяти.
    public static void AddItem(Player player, Item item)
    {
        int qty = Math.Max(1, item.Quantity);
        int cap = Balance.MaxStackForType(item.Type);

        if (cap > 1 && !string.IsNullOrEmpty(item.TemplateId))
        {
            var stack = player.Inventory
                .Where(i => i.TemplateId == item.TemplateId && i.Quantity < cap)
                .OrderByDescending(i => i.Quantity)
                .FirstOrDefault();

            if (stack != null)
            {
                int room = cap - stack.Quantity;
                int add = Math.Min(room, qty);
                stack.Quantity += add;
                qty -= add;
            }
        }

        while (qty > 0)
        {
            int take = cap > 1 ? Math.Min(cap, qty) : 1;
            var clone = item.Clone();
            clone.Id = Guid.NewGuid().ToString();
            clone.Quantity = take;
            clone.MaxStack = cap;
            player.Inventory.Add(clone);
            qty -= take;
        }
    }

    // Списывает qty штук предметов с заданным template_id.
    // Возвращает true, если удалось списать запрошенное количество.
    public static bool RemoveQuantity(Player player, string templateId, int qty)
    {
        int available = player.Inventory
            .Where(i => i.TemplateId == templateId)
            .Sum(i => i.Quantity);
        if (available < qty) return false;

        int remaining = qty;
        foreach (var item in player.Inventory.Where(i => i.TemplateId == templateId).ToList())
        {
            if (remaining <= 0) break;
            if (item.Quantity <= remaining)
            {
                remaining -= item.Quantity;
                player.Inventory.Remove(item);
            }
            else
            {
                item.Quantity -= remaining;
                remaining = 0;
            }
        }
        return remaining == 0;
    }

    // Удаляет конкретные записи по их item_id (точечно, без стак-логики).
    // Возвращает список удалённых предметов.
    public static List<Item> RemoveByIds(Player player, IEnumerable<string> ids)
    {
        var idSet = new HashSet<string>(ids);
        var removed = player.Inventory.Where(i => idSet.Contains(i.Id)).ToList();
        foreach (var item in removed)
            player.Inventory.Remove(item);
        return removed;
    }

    // Возвращает, сколько штук предмета с template_id есть у игрока.
    public static int CountByTemplate(Player player, string templateId)
    {
        return player.Inventory
            .Where(i => i.TemplateId == templateId)
            .Sum(i => i.Quantity);
    }

    // Списывает qty штук конкретной записи инвентаря (по item_id).
    // Если у записи Quantity > qty — уменьшает Quantity. Иначе удаляет запись.
    // Возвращает true, если удалось списать.
    public static bool RemoveFromRecord(Player player, string itemId, int qty)
    {
        var item = player.Inventory.FirstOrDefault(i => i.Id == itemId);
        if (item == null) return false;
        if (item.Quantity <= qty)
        {
            player.Inventory.Remove(item);
        }
        else
        {
            item.Quantity -= qty;
        }
        return true;
    }
}
