using System.Text.Json;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using LostAndDivine.Server.Network;
using LostAndDivine.Server.Repositories;

namespace LostAndDivine.Server;

public class DialogueManager
{
    private readonly GameWorld _world;
    private readonly QuestManager _quests;
    private readonly MerchantManager _merchant;
    private INetworkHub? _hub;
    private GameServices _svc = null!;
    private readonly Dictionary<string, DialogueTree> _cache = new();
    private readonly object _lock = new();

    public DialogueManager(GameWorld world, QuestManager quests, MerchantManager merchant)
    {
        _world = world;
        _quests = quests;
        _merchant = merchant;
    }

    public void SetHub(INetworkHub hub) => _hub = hub;

    public void SetServices(GameServices svc) => _svc = svc;

    public void LoadAll()
    {
        var npcs = DatabaseManager.LoadNpcs();
        int count = 0;
        foreach (var npc in npcs)
        {
            var tree = DialogueParser.Parse(npc.Data);
            if (tree != null && tree.Nodes.Count > 0)
            {
                lock (_lock) _cache[npc.Id] = tree;
                count++;
            }
        }
        Log.Info($"Загружено диалогов: {count}");
    }

    public DialogueTree? GetTree(string npcId)
    {
        lock (_lock) return _cache.GetValueOrDefault(npcId);
    }

    public string? GetStartNodeId(string npcId)
    {
        var tree = GetTree(npcId);
        if (tree == null) return null;
        return tree.Nodes.ContainsKey("greeting") ? "greeting" : tree.Nodes.Keys.FirstOrDefault();
    }

    /// <summary>Есть ли в диалоге NPC вариант «взять задание» для указанного квеста.</summary>
    public bool OffersQuest(string npcId, string questId) =>
        OffersAction(npcId, "accept_quest:" + questId);

    /// <summary>Есть ли в диалоге NPC действие (например accept_quest:Q0009, complete_quest:Q0009).</summary>
    public bool OffersAction(string npcId, string action)
    {
        var tree = GetTree(npcId);
        if (tree == null) return false;
        foreach (var node in tree.Nodes.Values)
            foreach (var c in node.Choices)
                if (!string.IsNullOrEmpty(c.Action) &&
                    string.Equals(c.Action, action, StringComparison.OrdinalIgnoreCase))
                    return true;
        return false;
    }

    public async Task HandleChoice(ClientConnection client, Player player, int choiceIndex)
    {
        if (_hub == null) return;
        if (!player.Dialogue.IsActive) return;

        var tree = GetTree(player.Dialogue.NpcId!);
        if (tree == null || !tree.Nodes.TryGetValue(player.Dialogue.CurrentNodeId!, out var currentNode))
        {
            await CloseDialogue(client, player);
            return;
        }

        if (choiceIndex < 0)
        {
            await CloseDialogue(client, player);
            return;
        }

        var visibleChoices = FilterChoices(currentNode.Choices, player);
        if (choiceIndex >= visibleChoices.Count)
        {
            await CloseDialogue(client, player);
            return;
        }

        var choice = visibleChoices[choiceIndex];

        if (!string.IsNullOrEmpty(choice.Action))
        {
            bool close = await ApplyAction(client, player, choice.Action);
            if (close) return;
        }

        if (!string.IsNullOrEmpty(choice.NextNodeId) && tree.Nodes.ContainsKey(choice.NextNodeId))
        {
            player.Dialogue.Start(player.Dialogue.NpcId!, choice.NextNodeId);
            await SendNode(client, player, tree, choice.NextNodeId);
        }
        else
        {
            await CloseDialogue(client, player);
        }
    }

    public List<DialogueChoice> FilterChoices(List<DialogueChoice> choices, Player player)
    {
        var result = new List<DialogueChoice>();
        foreach (var c in choices)
        {
            if (!string.IsNullOrEmpty(c.Condition) && !EvaluateCondition(c.Condition, player))
                continue;
            result.Add(c);
        }
        return result;
    }

    private bool EvaluateCondition(string condition, Player player)
    {
        if (condition.StartsWith("quest_active:"))
        {
            string qid = condition["quest_active:".Length..];
            return player.ActiveQuests.Any(q => q.QuestId == qid && !q.Completed);
        }
        if (condition.StartsWith("quest_complete:"))
        {
            string qid = condition["quest_complete:".Length..];
            // Выполнен: либо в истории (сдан), либо в активных с флагом Completed (готов к сдаче)
            if (player.CompletedQuestIds.Contains(qid)) return true;
            return player.ActiveQuests.Any(q => q.QuestId == qid && q.Completed);
        }
        if (condition.StartsWith("quest_not_active:"))
        {
            string qid = condition["quest_not_active:".Length..];
            return !player.ActiveQuests.Any(q => q.QuestId == qid);
        }
        if (condition.StartsWith("quest_not_started:"))
        {
            // Квест ещё не брался: не активен И не выполнен ранее.
            // В отличие от quest_not_active не показывает «тупиковые» ветки
            // для уже сданных квестов. Для повторяемых квестов история
            // выполнения не мешает взять квест снова.
            string qid = condition["quest_not_started:".Length..];
            if (player.ActiveQuests.Any(q => q.QuestId == qid)) return false;
            if (player.CompletedQuestIds.Contains(qid))
            {
                var def = _quests.FindQuest(qid);
                return def != null && def.Repeatable;
            }
            return true;
        }
        if (condition.StartsWith("quest_ready:"))
        {
            string qid = condition["quest_ready:".Length..];
            return player.ActiveQuests.Any(q => q.QuestId == qid && q.Completed);
        }
        if (condition.StartsWith("has_item:"))
        {
            // has_item:I0015 или has_item:I0015:3 — нужное количество в инвентаре
            var parts = condition["has_item:".Length..].Split(':');
            string itemId = parts[0];
            int required = parts.Length > 1 && int.TryParse(parts[1], out var req) ? Math.Max(1, req) : 1;
            int available = player.Inventory
                .Where(i => i.TemplateId == itemId || i.Id == itemId)
                .Sum(i => i.Quantity);
            return available >= required;
        }
        if (condition.StartsWith("level:"))
        {
            int minLevel = 0;
            if (int.TryParse(condition["level:".Length..], out minLevel))
                return player.Level >= minLevel;
            return false;
        }
        if (condition.StartsWith("gold:"))
        {
            int minGold = 0;
            if (int.TryParse(condition["gold:".Length..], out minGold))
                return player.Gold >= minGold;
            return false;
        }
        return true;
    }

    private async Task<bool> ApplyAction(ClientConnection client, Player player, string action)
    {
        if (_hub == null) return false;
        var svc = _svc;
        if (action.StartsWith("accept_quest:"))
        {
            string qid = action["accept_quest:".Length..];
            var def = _quests.FindQuest(qid);
            if (def != null && _quests.CanTakeQuest(player, def))
            {
                _quests.TakeQuest(player, def);
                await _hub.SendQuestLog(client, player);
                await _svc.ChatTo(client, ChatChannel.System, "Система", $"Задание принято: {def.Title}");
                _hub.MarkZoneDirty(player.CurrentZoneId);
                await _hub.BroadcastMapAsync();
            }
        }
        else if (action.StartsWith("complete_quest:"))
        {
            string qid = action["complete_quest:".Length..];
            var result = _quests.CompleteQuest(player, qid);
            if (result.Success)
            {
                await _hub.SendQuestLog(client, player);
                await _svc.ChatTo(client, ChatChannel.System, "Система", result.Message);
                await _hub.SendStatusAsync(client, player);
                _hub.MarkZoneDirty(player.CurrentZoneId);
                await _hub.BroadcastMapAsync();
                await CloseDialogue(client, player);
                return true;
            }
        }
                else if (action == "open_shop")
                {
                    await CloseDialogue(client, player);
                    await _svc.Interactions.OpenShop(player, client);
                    return true;
                }
        else if (action == "close")
        {
            await CloseDialogue(client, player);
            return true;
        }
        else if (action.StartsWith("enter_instance:"))
        {
            string templateId = action["enter_instance:".Length..];
            await CloseDialogue(client, player);
            await _svc.Instances.TryEnter(player, templateId, client);
            return true;
        }
        else if (action == "open_instances")
        {
            // Открыть окно выбора инстансов (соло/группа)
            await CloseDialogue(client, player);
            await _hub.SendToClient(client, new GameMessage
            {
                Type = "instance_window_open",
                Data = new { }
            });
            return true;
        }
        else if (action.StartsWith("give_item:"))
        {
            // give_item:I0015[:кол-во] — выдать предмет из таблицы items
            var parts = action["give_item:".Length..].Split(':');
            string itemId = parts[0];
            int count = parts.Length > 1 && int.TryParse(parts[1], out var c) ? Math.Max(1, c) : 1;
            var template = DatabaseManager.LoadItems().FirstOrDefault(i => i.Id == itemId);
            if (template != null)
            {
                var gift = template.Clone();
                gift.Quantity = count;
                InventoryHelper.AddItem(player, gift);
                await _hub.SendInventoryAndStatus(client, player);
                await _svc.ChatTo(client, ChatChannel.System, "Система", $"Вы получили: {count}× {template.Name}");
            }
        }
        else if (action.StartsWith("give_gold:"))
        {
            if (int.TryParse(action["give_gold:".Length..], out int amount) && amount > 0)
            {
                player.Gold += amount;
                await _hub.SendStatusAsync(client, player);
                await _svc.ChatTo(client, ChatChannel.System, "Система", $"Вы получили {amount} золота.");
            }
        }
        else if (action.StartsWith("take_item:"))
        {
            // take_item:I0015[:кол-во] — забрать предмет у игрока (квестовая сдача)
            var parts = action["take_item:".Length..].Split(':');
            string itemId = parts[0];
            int count = parts.Length > 1 && int.TryParse(parts[1], out var c) ? Math.Max(1, c) : 1;
            var records = player.Inventory.Where(i => i.TemplateId == itemId || i.Id == itemId).ToList();
            int available = records.Sum(i => i.Quantity);
            int toRemove = Math.Min(count, available);
            foreach (var rec in records)
            {
                if (toRemove <= 0) break;
                int take = Math.Min(toRemove, rec.Quantity);
                InventoryHelper.RemoveFromRecord(player, rec.Id, take);
                toRemove -= take;
            }
            if (toRemove < count)
            {
                await _hub.SendInventoryAndStatus(client, player);
                string itemName = records.FirstOrDefault()?.Name ?? itemId;
                await _svc.ChatTo(client, ChatChannel.System, "Система", $"У вас забрали: {Math.Min(count, available)}× {itemName}");
            }
        }
        else if (action.StartsWith("teleport:"))
        {
            // teleport:зона:x:y — переместить игрока в точку другой (или той же) зоны
            var parts = action["teleport:".Length..].Split(':');
            if (parts.Length >= 3 &&
                int.TryParse(parts[1], out int tx) && int.TryParse(parts[2], out int ty))
            {
                await CloseDialogue(client, player);
                await TeleportPlayer(client, player, parts[0], tx, ty);
                return true;
            }
        }
        return false;
    }

    public async Task SendNode(ClientConnection client, Player player, DialogueTree tree, string nodeId)
    {
        if (_hub == null) return;
        if (!tree.Nodes.TryGetValue(nodeId, out var node)) return;

        var filtered = FilterChoices(node.Choices, player);

        var data = new
        {
            NpcId = player.Dialogue.NpcId,
            NodeId = nodeId,
            Speaker = node.Speaker,
            Text = node.Text,
            Choices = filtered.Select(c => new { c.Text, NextNodeId = c.NextNodeId }).ToList()
        };

        await _hub.SendToClient(client, new GameMessage
        {
            Type = "dialogue_open",
            Data = data
        });
    }

    public async Task CloseDialogue(ClientConnection client, Player player)
    {
        if (_hub == null) return;
        player.Dialogue.Clear();
        await _hub.SendToClient(client, new GameMessage
        {
            Type = "dialogue_close",
            Data = null
        });
    }

    /// <summary>Перемещение игрока в точку зоны (телепорт из диалога) с проверкой препятствий.</summary>
    private async Task TeleportPlayer(ClientConnection client, Player player, string zoneId, int x, int y)
    {
        if (_hub == null) return;
        var zoneMap = _svc.Zones.GetOrCreateMap(zoneId);
        if (x < 0 || y < 0 || x >= zoneMap.Width || y >= zoneMap.Height || zoneMap.IsObstacle(x, y))
        {
            await _svc.ChatTo(client, ChatChannel.System, "Система", "Невозможно переместиться туда.");
            return;
        }

        string fromZone = player.CurrentZoneId;
        player.Movement.Stop();
        player.CurrentZoneId = zoneId;
        player.X = x;
        player.Y = y;

        var zone = _svc.Zones.GetZone(zoneId);
        string zoneName = zone?.Name ?? zoneId;
        await _hub.SendZoneTransition(client, player);
        await _svc.ChatTo(client, ChatChannel.System, "Система", $"Вы переместились в зону: {zoneName}");
        _hub.MarkZoneDirty(fromZone);
        _hub.MarkZoneDirty(zoneId);
        await _hub.BroadcastMapAsync();

        // Квесты: explore/авто-выдача/travel в точке прибытия
        var results = _svc.Quests.IncrementExploreProgress(player, zoneId);
        foreach (var (title, current, target, completed) in results)
            await _svc.ChatTo(client, ChatChannel.System, "Система",
                completed ? $"[Задание] {title}: зона исследована!" : $"[Задание] {title}: {current}/{target}");

        var granted = _svc.Quests.TryAutoGrant(player, zoneId);
        foreach (var d in granted)
            await _svc.ChatTo(client, ChatChannel.System, "Система", $"Новое задание: {d.Title}");

        var travelResults = _svc.Quests.IncrementTravelProgress(player, zoneId, x, y);
        foreach (var (title, current, target, completed) in travelResults)
            await _svc.ChatTo(client, ChatChannel.System, "Система",
                completed ? $"[Задание] {title}: цель достигнута!" : $"[Задание] {title}: {current}/{target}");

        if (results.Count + granted.Count + travelResults.Count > 0)
            await _hub.SendQuestLog(client, player);
    }

    private async Task ProcessPendingInteraction(Player player, string type)
    {
        if (_hub == null) return;
        var client = _world.FindClientByPlayer(player);
        if (client == null) return;

        switch (type)
        {
            case "merchant":
                Log.Debug($"{player.Name} открыл магазин");
                await _hub.SendToClient(client, new GameMessage
                {
                    Type = "shop_response",
                    Data = new
                    {
                        MerchantX = _merchant.MerchantX,
                        MerchantY = _merchant.MerchantY,
                        MerchantName = "Торговец",
                        Discount = 0,
                        Items = _merchant.ShopItems.Select(i => new
                        {
                            i.Id, i.Name, i.Type, i.WeaponSubtype,
                            Value = Balance.BuyPrice(i.Value),
                            OriginalValue = i.Value,
                            i.MaxHealthBonus, i.HealAmount, i.RestoreMana, i.Description,
                            i.Stock,
                            IsBuyback = false
                        }).ToList(),
                        Buyback = player.BuybackItems.Select(b => new
                        {
                            b.Id, b.Name, b.Type, b.WeaponSubtype,
                            Value = Balance.BuybackPrice(b.Value),
                            OriginalValue = b.Value,
                        b.MaxHealthBonus, b.HealAmount, b.RestoreMana, b.Description,
                        b.Quantity, IsBuyback = true
                        }).ToList(),
                        PlayerGold = player.Gold
                    }
                });
                break;
        }
    }
}
