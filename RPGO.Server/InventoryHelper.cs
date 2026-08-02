using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class InventoryHelper
{
    // Ключ, по которому предметы стакаются: TemplateId, если он задан,
    // иначе комбинация Type+Name (для стакаемых предметов без шаблона —
    // зелья, трофеи, коллекционки).
    public static string StackKey(Item it)
        => !string.IsNullOrEmpty(it.TemplateId) ? "t:" + it.TemplateId : "k:" + it.Type + "|" + it.Name;

    public static bool StackMatch(Item a, Item b) => StackKey(a) == StackKey(b);

    // Добавляет предмет в инвентарь игрока, стакая стакаемые предметы
    // (MaxStack > 1) по StackKey в памяти.
    public static void AddItem(Player player, Item item)
    {
        int qty = Math.Max(1, item.Quantity);
        int cap = Balance.MaxStackForType(item.Type);

        if (cap > 1)
        {
            var stack = player.Inventory
                .Where(i => i.Quantity < cap && StackMatch(i, item))
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

    // Списывает qty штук предметов, подходящих под reference (по StackKey).
    // Возвращает true, если удалось списать запрошенное количество.
    public static bool RemoveQuantity(Player player, Item reference, int qty)
    {
        int available = player.Inventory
            .Where(i => StackMatch(i, reference))
            .Sum(i => i.Quantity);
        if (available < qty) return false;

        int remaining = qty;
        foreach (var item in player.Inventory.Where(i => StackMatch(i, reference)).ToList())
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

    // Возвращает, сколько штук предмета (по StackKey) есть у игрока.
    public static int CountByItem(Player player, Item reference)
        => player.Inventory.Where(i => StackMatch(i, reference)).Sum(i => i.Quantity);

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

    // Объединяет стакаемые предметы (MaxStack > 1) в стаки по StackKey.
    // Возвращает новый список; нестакаемые записи не трогает.
    // Используется при загрузке из БД, чтобы слить раздробленные записи
    // (например, много строк зелий по 1 шт).
    public static List<Item> ConsolidateStackables(List<Item> items)
    {
        var result = new List<Item>();
        foreach (var it in items)
        {
            int cap = Balance.MaxStackForType(it.Type);
            if (cap <= 1)
            {
                result.Add(it);
                continue;
            }

            int qty = Math.Max(1, it.Quantity);
            var stack = result.FirstOrDefault(s => s.Quantity < cap && StackMatch(s, it));
            if (stack != null)
            {
                int room = cap - stack.Quantity;
                int add = Math.Min(room, qty);
                stack.Quantity += add;
                qty -= add;
            }

            // Остаток кладём в новые стаки. Первый чанк сохраняет Id исходной
            // записи (чтобы клиент, получивший storage_update/inventory_response,
            // не потерял ссылку на предмет при последующем withdraw/deposit).
            bool firstChunk = true;
            while (qty > 0)
            {
                int take = Math.Min(cap, qty);
                Item chunk;
                if (firstChunk)
                {
                    chunk = it;
                    firstChunk = false;
                }
                else
                {
                    chunk = it.Clone();
                    chunk.Id = Guid.NewGuid().ToString();
                }
                chunk.Quantity = take;
                chunk.MaxStack = cap;
                result.Add(chunk);
                qty -= take;
            }
        }
        return result;
    }
}
