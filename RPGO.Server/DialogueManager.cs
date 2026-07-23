using System.Text.Json;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using RPGGame.Server.Network;
using RPGGame.Server.Repositories;

namespace RPGGame.Server;

public static class DialogueManager
{
    private static readonly Dictionary<string, DialogueTree> _cache = new();
    private static readonly object _lock = new();

    private const string ElderDialogueJson = @"{
  ""greeting"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Приветствую, путник. Наше деревне нужна помощь."",
    ""choices"": [
      { ""text"": ""Что случилось?"", ""next"": ""story1"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Как идёт охота на волков?"", ""next"": ""quest_progress"", ""condition"": ""quest_active:Q0007"" },
      { ""text"": ""Волки побеждены. Все пятеро мертвы."", ""next"": ""quest_turnin"", ""condition"": ""quest_ready:Q0007"" },
      { ""text"": ""У меня есть задание от торговца."", ""next"": ""merchant_quest"", ""condition"": ""quest_ready:Q0001"" },
      { ""text"": ""Простите, мне пора."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_progress"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Ты ещё не вернулся с трофеями. Стая всё ещё бродит у околицы. Будь осторожен и возвращайся, когда справишься со всеми пятью."",
    ""choices"": [
      { ""text"": ""Постараюсь быстрее."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story1"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""На прошлой неделе охотник Тихон вышел в лес и не вернуся. Мы нашли его лук сломанным у развилки тропинок. А сегодня утром стая волков вышла к самой околице."",
    ""choices"": [
      { ""text"": ""Волки? Разве это серьёзная угроза?"", ""next"": ""story2"" },
      { ""text"": ""Мне жаль охотника, но у меня свои дела."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story2"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Серьёзная. Пять зверей — и не простых, а голодных. Они уже покалечили козу у ближайшего двора. Если стая наберётся смелости — нападут на людей. Дети боятся выходить во двор играть."",
    ""choices"": [
      { ""text"": ""Я помогу. Отправлюсь на охоту на волков."", ""next"": ""story_accept"", ""action"": ""accept_quest:Q0007"", ""condition"": ""quest_not_active:Q0007"" },
      { ""text"": ""Я уже охотюсь на них."", ""next"": null, ""condition"": ""quest_active:Q0007"", ""action"": ""close"" },
      { ""text"": ""Пять волков — это серьёзно. Мне нужно подготовиться."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""story_accept"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Благодарю! Будь осторожен — волки хитрые звери. Они держатся вместе и нападают стаей. Убей всех пятерых и возвращайся — я награжу тебя по заслугам."",
    ""choices"": [
      { ""text"": ""Вернусь с победой."", ""next"": null, ""action"": ""close"" }
    ]
  },
  ""quest_turnin"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""Неужели правда? Все пятеро?! Ты настоящий воин! Деревня будет помнить твой подвиг. Прими мою благодарность — и вот, возьми это. Ты заслужил."",
    ""choices"": [
      { ""text"": ""Спасибо, староста!"", ""next"": null, ""action"": ""complete_quest:Q0007"" }
    ]
  },
  ""merchant_quest"": {
    ""speaker"": ""Староста деревни"",
    ""text"": ""А, ты помогаешь торговцу? Передай ему мой привет. Ты заслужил награду!"",
    ""choices"": [
      { ""text"": ""Спасибо!"", ""next"": null, ""action"": ""complete_quest:Q0001"" }
    ]
  }
}";

    public static void LoadAll()
    {
        using (var conn = Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE npcs SET x = 48, y = 52, data = $data WHERE id = 'N0003'";
            cmd.Parameters.AddWithValue("$data", ElderDialogueJson);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT OR IGNORE INTO quests_def (id, title, description, type, target_monster_id, target_item_id, target_npc_id, target, xp_reward, gold_reward) VALUES ('Q0007', 'Волчья стая', 'Перед старостой стая волков угрожает деревне. Убей 5 волков.', 'kill', 'M0006', '', 'N0003', 5, 80, 50)";
            cmd.ExecuteNonQuery();
        }
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

    public static DialogueTree? GetTree(string npcId)
    {
        lock (_lock) return _cache.GetValueOrDefault(npcId);
    }

    public static string? GetStartNodeId(string npcId)
    {
        var tree = GetTree(npcId);
        if (tree == null) return null;
        return tree.Nodes.ContainsKey("greeting") ? "greeting" : tree.Nodes.Keys.FirstOrDefault();
    }

    public static async Task HandleChoice(ClientConnection client, Player player, int choiceIndex)
    {
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

    public static List<DialogueChoice> FilterChoices(List<DialogueChoice> choices, Player player)
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

    private static bool EvaluateCondition(string condition, Player player)
    {
        if (condition.StartsWith("quest_active:"))
        {
            string qid = condition["quest_active:".Length..];
            return player.ActiveQuests.Any(q => q.QuestId == qid && !q.Completed);
        }
        if (condition.StartsWith("quest_complete:"))
        {
            string qid = condition["quest_complete:".Length..];
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

    private static async Task<bool> ApplyAction(ClientConnection client, Player player, string action)
    {
        if (action.StartsWith("accept_quest:"))
        {
            string qid = action["accept_quest:".Length..];
            var def = QuestManager.FindQuest(qid);
            if (def != null && !player.ActiveQuests.Any(q => q.QuestId == qid))
            {
                player.ActiveQuests.Add(new QuestProgress { QuestId = qid });
                await Program.Hub.SendQuestLog(client, player);
                await Program.ChatTo(client, ChatChannel.System, "Система", $"Задание принято: {def.Title}");
            }
        }
        else if (action.StartsWith("complete_quest:"))
        {
            string qid = action["complete_quest:".Length..];
            var def = QuestManager.FindQuest(qid);
            var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == qid);
            if (def != null && prog != null && prog.Completed)
            {
                player.Experience += def.XpReward;
                player.Gold += def.GoldReward;
                player.ActiveQuests.Remove(prog);
                if (player.TryLevelUp())
                    Log.Info($"{player.Name} повысил уровень до {player.Level}!");
                await Program.Hub.SendQuestLog(client, player);
                await Program.ChatTo(client, ChatChannel.System, "Система",
                    $"Задание выполнено: {def.Title}! +{def.XpReward}XP, +{def.GoldReward} зол.");
                await Program.Hub.SendStatusAsync(client, player);
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
        return false;
    }

    public static async Task SendNode(ClientConnection client, Player player, DialogueTree tree, string nodeId)
    {
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

        await Program.Hub.SendToClient(client, new GameMessage
        {
            Type = "dialogue_open",
            Data = data
        });
    }

    public static async Task CloseDialogue(ClientConnection client, Player player)
    {
        player.Dialogue.Clear();
        await Program.Hub.SendToClient(client, new GameMessage
        {
            Type = "dialogue_close",
            Data = null
        });
    }

    private static async Task ProcessPendingInteraction(Player player, string type)
    {
        var client = Program.World.FindClientByPlayer(player);
        if (client == null) return;

        switch (type)
        {
            case "merchant":
                Log.Debug($"{player.Name} открыл магазин");
                await Program.Hub.SendToClient(client, new GameMessage
                {
                    Type = "shop_response",
                    Data = new
                    {
                        MerchantX = MerchantManager.MerchantX,
                        MerchantY = MerchantManager.MerchantY,
                        MerchantName = "Торговец",
                        Discount = 0,
                        Items = MerchantManager.ShopItems.Select(i => new
                        {
                            i.Id, i.Name, i.Type,
                            Value = Balance.BuyPrice(i.Value),
                            OriginalValue = i.Value,
                            i.MaxHealthBonus, i.HealAmount, i.Description,
                            i.Stock,
                            IsBuyback = false
                        }).ToList(),
                        Buyback = player.BuybackItems.Select(b => new
                        {
                            b.Id, b.Name, b.Type,
                            Value = Balance.BuybackPrice(b.Value),
                            OriginalValue = b.Value,
                            b.MaxHealthBonus, b.HealAmount, b.Description,
                            IsBuyback = true
                        }).ToList(),
                        PlayerGold = player.Gold
                    }
                });
                break;
        }
    }
}
