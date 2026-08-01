using RPGGame.Shared.Models;

namespace RPGGame.Server;

public class QuestManager
{
    private readonly GameWorld _world;

    public int BoardX
    {
        get => _world.Map.BoardX;
        private set => _world.Map.BoardX = value;
    }

    public int BoardY
    {
        get => _world.Map.BoardY;
        private set => _world.Map.BoardY = value;
    }

    private List<QuestDefinition> _quests = new();

    private int? _tiledX;
    private int? _tiledY;

    public QuestManager(GameWorld world)
    {
        _world = world;
    }

    /// <summary>Позиция доски заданий из Tiled-карты (приоритет над БД).</summary>
    public void SetTiledPosition(int x, int y)
    {
        _tiledX = x;
        _tiledY = y;
    }

    public void Initialize()
    {
        var npc = DatabaseManager.LoadNpcs().FirstOrDefault(n => n.Type == "board");
        if (_tiledX.HasValue && _tiledY.HasValue)
        {
            BoardX = _tiledX.Value;
            BoardY = _tiledY.Value;
        }
        else if (npc != null)
        {
            BoardX = npc.X;
            BoardY = npc.Y;
        }
        else
        {
            BoardX = DatabaseManager.GetWorldConfigInt("board_x", 48);
            BoardY = DatabaseManager.GetWorldConfigInt("board_y", 48);
        }
        _quests = DatabaseManager.LoadQuestDefinitions();
        Log.Info($"Загружено квестов: {_quests.Count}");
    }

    /// <summary>Заменяет набор определений квестов (используется для горячей перезагрузки и тестов).</summary>
    public void SetDefinitions(IEnumerable<QuestDefinition> definitions)
    {
        lock (_quests)
        {
            _quests = definitions?.ToList() ?? new List<QuestDefinition>();
        }
    }

    public QuestBoardPosition Board =>
        new QuestBoardPosition { X = BoardX, Y = BoardY, Name = "Доска заданий" };

    public List<QuestDefinition> GetAvailableQuests() => _quests.ToList();

    public QuestDefinition? FindQuest(string id) =>
        _quests.FirstOrDefault(q => q.Id == id);

    public bool IsAtBoard(int x, int y) => Math.Abs(x - BoardX) + Math.Abs(y - BoardY) <= 1;

    /// <summary>
    /// Может ли игрок взять квест: не взят, не выполнен ранее, уровень достаточен,
    /// предусловие (предыдущее звено цепочки) выполнено.
    /// </summary>
    public bool CanTakeQuest(Player player, QuestDefinition def)
    {
        if (def == null) return false;
        if (player.CompletedQuestIds.Contains(def.Id)) return false;
        if (player.ActiveQuests.Any(q => q.QuestId == def.Id)) return false;
        if (player.Level < def.MinLevel) return false;
        if (!string.IsNullOrEmpty(def.PrerequisiteQuestId) &&
            !player.CompletedQuestIds.Contains(def.PrerequisiteQuestId))
            return false;
        return true;
    }

    /// <summary>Квесты, доступные игроку сейчас (с учётом цепочек).</summary>
    public List<QuestDefinition> GetAvailableQuests(Player player) =>
        _quests.Where(q => CanTakeQuest(player, q)).ToList();

    /// <summary>
    /// Взять квест: добавляет в активные с текущим прогрессом (для collect — по инвентарю).
    /// Возвращает false, если квест недоступен.
    /// </summary>
    public bool TakeQuest(Player player, QuestDefinition def)
    {
        if (!CanTakeQuest(player, def)) return false;

        int currentProgress = 0;
        if (def.Type == "collect" && !string.IsNullOrEmpty(def.TargetItemId))
            currentProgress = player.Inventory.Count(i => i.Id == def.TargetItemId);
        bool alreadyCompleted = currentProgress >= def.Target;

        player.ActiveQuests.Add(new QuestProgress { QuestId = def.Id, Current = currentProgress, Completed = alreadyCompleted });
        return true;
    }

    /// <summary>
    /// Результат сдачи квеста. Success=false, если квест не активен/не выполнен.
    /// ErrorKind: 0 — нет, 1 — не в активных, 2 — не выполнен.
    /// </summary>
    public sealed record QuestCompleteResult(bool Success, int ErrorKind, string Message, bool LeveledUp);

    public QuestCompleteResult CompleteQuest(Player player, string qid)
    {
        var prog = player.ActiveQuests.FirstOrDefault(q => q.QuestId == qid);
        var def = FindQuest(qid);
        if (prog == null || def == null)
            return new QuestCompleteResult(false, 1, "У вас нет этого задания.", false);
        if (!prog.Completed)
            return new QuestCompleteResult(false, 2, $"Задание ещё не выполнено ({prog.Current}/{def.Target}).", false);

        // Списываем предметы, нужные для сдачи квеста (collect)
        if (def.Type == "collect" && !string.IsNullOrEmpty(def.TargetItemId))
        {
            var records = player.Inventory.Where(i => i.TemplateId == def.TargetItemId || i.Id == def.TargetItemId).ToList();
            int available = records.Sum(i => i.Quantity);
            int toRemove = Math.Min(def.Target, available);
            foreach (var rec in records)
            {
                if (toRemove <= 0) break;
                int take = Math.Min(toRemove, rec.Quantity);
                InventoryHelper.RemoveFromRecord(player, rec.Id, take);
                toRemove -= take;
            }
        }

        player.ActiveQuests.Remove(prog);
        if (!player.CompletedQuestIds.Contains(def.Id))
            player.CompletedQuestIds.Add(def.Id);

        player.Experience += def.XpReward;
        player.Gold += def.GoldReward;
        string rewardText = $"+{def.XpReward} опыта, +{def.GoldReward} золота";

        // Награда-предмет
        if (!string.IsNullOrEmpty(def.ItemRewardId) && def.ItemRewardCount > 0)
        {
            var template = DatabaseManager.LoadItems().FirstOrDefault(i => i.Id == def.ItemRewardId);
            if (template != null)
            {
                var gift = template.Clone();
                gift.Quantity = def.ItemRewardCount;
                InventoryHelper.AddItem(player, gift);
                rewardText += $", {def.ItemRewardCount}× {template.Name}";
            }
        }

        bool leveledUp = player.TryLevelUp();
        if (leveledUp)
            Log.Info($"{player.Name} повысил уровень до {player.Level}!");

        Log.Info($"{player.Name} сдал задание {def.Title}: {rewardText}");
        string msg = $"Задание выполнено! {def.Title}. Награда: {rewardText}.";
        if (leveledUp)
            msg += $" Уровень повышен! Теперь вы уровень {player.Level}!";
        return new QuestCompleteResult(true, 0, msg, leveledUp);
    }

    /// <summary>
    /// Прогресс talk-квестов при разговоре с NPC: все активные невыполненные
    /// talk-квесты, цель которых — этот NPC, получают +1 к прогрессу.
    /// </summary>
    public void IncrementTalkProgress(Player player, string npcId)
    {
        foreach (var q in player.ActiveQuests.Where(q => !q.Completed))
        {
            var def = FindQuest(q.QuestId);
            if (def == null || def.Type != "talk") continue;
            if (def.TargetNpcId != npcId) continue;
            if (q.Current < def.Target)
            {
                q.Current++;
                if (q.Current >= def.Target)
                    q.Completed = true;
            }
        }
    }

    public List<(string Title, int Current, int Target, bool Completed)> IncrementKillProgress(Player player, string monsterTemplateId)
    {
        var results = new List<(string, int, int, bool)>();
        foreach (var q in player.ActiveQuests)
        {
            if (q.Completed) continue;
            var def = FindQuest(q.QuestId);
            if (def == null || def.Type != "kill") continue;
            if (def.TargetMonsterId != monsterTemplateId) continue;
            if (q.Current < def.Target)
            {
                q.Current++;
                results.Add((def.Title, q.Current, def.Target, q.Current >= def.Target));
                if (q.Current >= def.Target)
                    q.Completed = true;
            }
        }
        return results;
    }

    public List<(string Title, int Current, int Target, bool Completed)> IncrementCollectProgress(Player player, string itemId)
    {
        var results = new List<(string, int, int, bool)>();
        foreach (var q in player.ActiveQuests)
        {
            if (q.Completed) continue;
            var def = FindQuest(q.QuestId);
            if (def == null || def.Type != "collect") continue;
            if (def.TargetItemId != itemId) continue;
            if (q.Current < def.Target)
            {
                q.Current++;
                results.Add((def.Title, q.Current, def.Target, q.Current >= def.Target));
                if (q.Current >= def.Target)
                    q.Completed = true;
            }
        }
        return results;
    }
}
