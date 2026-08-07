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
    public bool OffersQuest(string npcId, string questId)
    {
        var tree = GetTree(npcId);
        if (tree == null) return false;
        string action = "accept_quest:" + questId;
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
        if (condition.StartsWith("quest_ready:"))
        {
            string qid = condition["quest_ready:".Length..];
            return player.ActiveQuests.Any(q => q.QuestId == qid && q.Completed);
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
            await ProcessPendingInteraction(player, "merchant");
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
