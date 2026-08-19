using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

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

    // Справочники названий для меток целей (заполняются в Initialize/Reload)
    private readonly Dictionary<string, string> _monsterNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _itemNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _itemTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _npcNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _zoneNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _labelsLock = new();

    private int? _tiledX;
    private int? _tiledY;

    /// <summary>Id NPC-доски (задаётся в БД); квесты с таким выдающим показываются на доске.</summary>
    public string BoardNpcId { get; private set; } = "N0002";

    // Предметы с флагом quest_item (шаблоны items.quest_item)
    private readonly HashSet<string> _questItemIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _questItemNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _questItemsLock = new();

    /// <summary>Поиск NPC по зоне и id (задаётся сервером; travel-квесты с целью-NPC).</summary>
    public Func<string, string, NpcPosition?>? NpcLookup { get; set; }

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
        if (npc != null) BoardNpcId = npc.Id;
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
        ReloadQuestItems();
        ReloadLabelDictionaries();
    }

    /// <summary>
    /// Справочники названий монстров/предметов/NPC/зон для меток целей в журнале.
    /// </summary>
    public void ReloadLabelDictionaries()
    {
        try
        {
            lock (_labelsLock)
            {
                _monsterNames.Clear();
                foreach (var t in DatabaseManager.LoadMonsterTemplates())
                    _monsterNames[t.Id] = t.Name;
                _itemNames.Clear();
                _itemTypes.Clear();
                foreach (var i in DatabaseManager.LoadItems())
                {
                    _itemNames[i.Id] = i.Name;
                    _itemTypes[i.Id] = i.Type;
                }
                _npcNames.Clear();
                foreach (var n in DatabaseManager.LoadNpcs())
                    _npcNames[n.Id] = n.Name;
                _zoneNames.Clear();
                foreach (var z in DatabaseManager.LoadZones())
                    _zoneNames[z.Id] = z.Name;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Не удалось загрузить справочники меток квестов: {ex.Message}");
        }
    }

    /// <summary>
    /// Перезагружает наборы квестовых предметов (шаблоны items.quest_item).
    /// Вызывается при старте и горячей перезагрузке.
    /// </summary>
    public void ReloadQuestItems()
    {
        var ids = DatabaseManager.LoadItems().Where(i => i.QuestItem).Select(i => i.Id).ToList();
        lock (_questItemsLock)
        {
            _questItemIds.Clear();
            _questItemNames.Clear();
            foreach (var id in ids) _questItemIds.Add(id);
        }
    }

    /// <summary>Задание наборов вручную (для тестов).</summary>
    public void SetQuestItemIds(IEnumerable<string> ids)
    {
        lock (_questItemsLock)
        {
            _questItemIds.Clear();
            foreach (var id in ids) _questItemIds.Add(id);
        }
    }

    /// <summary>Задание названий квестового лута вручную (для тестов).</summary>
    public void SetQuestItemNames(IEnumerable<string> names)
    {
        lock (_questItemsLock)
        {
            _questItemNames.Clear();
            foreach (var n in names) _questItemNames.Add(n);
        }
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

    /// <summary>Все определения квестов (включая уже взятые/выполненные) — для маркеров NPC.</summary>
    public List<QuestDefinition> GetAllDefinitions() => _quests.ToList();

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
        // Повторяемые квесты можно брать повторно, даже если они есть в истории выполнения.
        if (player.CompletedQuestIds.Contains(def.Id) && !def.Repeatable) return false;
        if (player.ActiveQuests.Any(q => q.QuestId == def.Id)) return false;
        if (player.Level < def.MinLevel) return false;
        if (!string.IsNullOrEmpty(def.PrerequisiteQuestId) &&
            !player.CompletedQuestIds.Contains(def.PrerequisiteQuestId))
            return false;
        return true;
    }

    /// <summary>
    /// Квесты, доступные на доске заданий (с учётом цепочек).
    /// Доска показывает только квесты, которые назначены на неё (GiverNpcId = доска);
    /// квесты других NPC-выдатчиков на доске не появляются.
    /// </summary>
    public List<QuestDefinition> GetAvailableQuests(Player player) =>
        _quests.Where(q => q.GiverNpcId == BoardNpcId && CanTakeQuest(player, q)).ToList();

    /// <summary>
    /// Взять квест: добавляет в активные с прогрессом по каждой цели
    /// (для collect — по текущему количеству в инвентаре).
    /// Возвращает false, если квест недоступен.
    /// </summary>
    public bool TakeQuest(Player player, QuestDefinition def)
    {
        if (!CanTakeQuest(player, def)) return false;

        var objectives = GetObjectives(def);
        var currents = new List<int>(objectives.Count);
        foreach (var obj in objectives)
        {
            int cur = 0;
            if (obj.Type == "collect" && !string.IsNullOrEmpty(obj.Target))
                cur = CountItems(player, obj.Target);
            currents.Add(Math.Min(cur, obj.Count));
        }
        bool alreadyCompleted = IsAllCompleted(objectives, currents);

        player.ActiveQuests.Add(new QuestProgress { QuestId = def.Id, Currents = currents, Completed = alreadyCompleted });
        return true;
    }

    /// <summary>Количество предметов шаблона в инвентаре игрока.</summary>
    private static int CountItems(Player player, string itemId)
        => player.Inventory
            .Where(i => i.TemplateId == itemId || i.Id == itemId)
            .Sum(i => i.Quantity);

    /// <summary>Снять заданное количество предметов из инвентаря (для сдачи collect-целей).</summary>
    private void RemoveItems(Player player, string itemId, int count)
    {
        var records = player.Inventory
            .Where(i => i.TemplateId == itemId || i.Id == itemId)
            .ToList();
        int toRemove = Math.Min(count, records.Sum(i => i.Quantity));
        foreach (var rec in records)
        {
            if (toRemove <= 0) break;
            int take = Math.Min(toRemove, rec.Quantity);
            InventoryHelper.RemoveFromRecord(player, rec.Id, take);
            toRemove -= take;
        }
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
            return new QuestCompleteResult(false, 2, $"Задание ещё не выполнено.", false);

        // Списываем предметы для каждой collect-цели
        foreach (var obj in GetObjectives(def))
        {
            if (obj.Type == "collect" && !string.IsNullOrEmpty(obj.Target))
                RemoveItems(player, obj.Target, obj.Count);
        }

        player.ActiveQuests.Remove(prog);
        // Повторяемые квесты не попадают в историю выполненных — их можно взять снова.
        if (!def.Repeatable && !player.CompletedQuestIds.Contains(def.Id))
        {
            player.CompletedQuestIds.Add(def.Id);
            player.CompletedQuestTimes[def.Id] = DateTime.UtcNow.ToString("o");
        }

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
    /// Нормализует цели квеста: если Objectives пуст (legacy-запись), строит
    /// список из одной цели по legacy-полям (Type/Target*/Target).
    /// </summary>
    public static List<QuestObjective> GetObjectives(QuestDefinition def)
    {
        if (def == null) return new List<QuestObjective>();
        if (def.Objectives != null && def.Objectives.Count > 0)
            return def.Objectives;

        var obj = new QuestObjective { Type = def.Type, Count = Math.Max(1, def.Target) };
        switch (def.Type)
        {
            case "kill": obj.Target = def.TargetMonsterId; break;
            case "collect":
            case "use": obj.Target = def.TargetItemId; break;
            case "talk":
            case "travel": obj.Target = def.TargetNpcId; obj.TargetX = def.TargetX; obj.TargetY = def.TargetY; break;
            case "explore": obj.Target = def.TargetZoneId; break;
        }
        return new List<QuestObjective> { obj };
    }

    /// <summary>Прогресс цели по индексу (с защитой от несовпадения размеров).</summary>
    public static int GetObjectiveCurrent(QuestProgress prog, int index)
        => prog != null && index >= 0 && index < prog.Currents.Count ? prog.Currents[index] : 0;

    /// <summary>Все ли цели квеста выполнены.</summary>
    public static bool IsAllCompleted(List<QuestObjective> objectives, List<int> currents)
    {
        for (int i = 0; i < objectives.Count; i++)
        {
            if (i >= currents.Count || currents[i] < objectives[i].Count)
                return false;
        }
        return true;
    }

    /// <summary>Текущий прогресс по каждой цели квеста (нормализованный список с Current).</summary>
    public List<QuestObjective> GetObjectiveStates(Player player, QuestProgress prog)
    {
        var def = FindQuest(prog.QuestId);
        if (def == null) return new List<QuestObjective>();
        var objectives = GetObjectives(def);
        for (int i = 0; i < objectives.Count; i++)
            objectives[i].Current = GetObjectiveCurrent(prog, i);
        return objectives;
    }

    /// <summary>Текстовая метка цели: «Убить: Крыса», «Собрать: Ягоды (5)» и т.п.</summary>
    public string ObjectiveLabel(QuestObjective obj)
    {
        string verb = obj.Type?.ToLower() switch
        {
            "kill" => "Убить",
            "collect" => "Собрать",
            "talk" => "Поговорить",
            "travel" => "Отправиться",
            "use" => "Использовать",
            "explore" => "Исследовать",
            _ => "Выполнить"
        };

        string target;
        switch (obj.Type?.ToLower())
        {
            case "kill":
                lock (_labelsLock) target = _monsterNames.TryGetValue(obj.Target ?? "", out var mn) ? mn ?? "" : obj.Target ?? "";
                break;
            case "collect":
            case "use":
                lock (_labelsLock) target = _itemNames.TryGetValue(obj.Target ?? "", out var iname) ? iname ?? "" : obj.Target ?? "";
                break;
            case "talk":
            case "travel":
                lock (_labelsLock) target = _npcNames.TryGetValue(obj.Target ?? "", out var nn) ? nn ?? "" : obj.Target ?? "";
                break;
            case "explore":
                lock (_labelsLock) target = _zoneNames.TryGetValue(obj.Target ?? "", out var zn) ? zn ?? "" : obj.Target ?? "";
                break;
            default:
                target = obj.Target ?? "";
                break;
        }

        if (obj.Type?.ToLower() == "travel" && string.IsNullOrEmpty(target))
            return $"{verb}: ({obj.TargetX}, {obj.TargetY})";
        if (string.IsNullOrEmpty(target)) target = obj.Target ?? "";
        return target.Length > 0 ? $"{verb}: {target}" : $"{verb}";
    }

    /// <summary>
    /// Ключ иконки квеста (по первой цели) для клиента:
    /// monster:{id} — спрайт монстра, item:{type} — иконка предмета по типу,
    /// npc — разговор, worldmap — путешествие/исследование, type-символы как запасной вариант.
    /// </summary>
    public string QuestIconKey(List<QuestObjective> objectives)
    {
        var obj = objectives.FirstOrDefault();
        if (obj == null) return "default";
        switch (obj.Type?.ToLower())
        {
            case "kill":
                return string.IsNullOrEmpty(obj.Target) ? "kill" : $"monster:{obj.Target}";
            case "collect":
            case "use":
                string? itemType = null;
                if (!string.IsNullOrEmpty(obj.Target))
                {
                    lock (_labelsLock) _itemTypes.TryGetValue(obj.Target, out itemType);
                }
                return string.IsNullOrEmpty(itemType) ? "item" : $"item:{itemType}";
            case "talk":
                return "npc";
            case "travel":
            case "explore":
                return "worldmap";
            default:
                return "default";
        }
    }

    /// <summary>
    /// Общий механизм прогресса: ищет у всех активных квестов цели заданного типа,
    /// подходящие под предикат, инкрементит их и помечает квест выполненным,
    /// когда выполнены ВСЕ его цели.
    /// </summary>
    private List<(string Title, int Current, int Target, bool Completed)> IncrementProgress(
        Player player, string objectiveType, Func<QuestObjective, bool> match)
    {
        var results = new List<(string, int, int, bool)>();
        foreach (var q in player.ActiveQuests)
        {
            if (q.Completed) continue;
            var def = FindQuest(q.QuestId);
            if (def == null) continue;
            var objectives = GetObjectives(def);
            if (objectives.Count == 0) continue;

            bool anyChanged = false;
            (int Cur, int Tgt) lastChanged = (0, 0);
            for (int i = 0; i < objectives.Count; i++)
            {
                var obj = objectives[i];
                if (obj.Type != objectiveType || !match(obj)) continue;
                int cur = GetObjectiveCurrent(q, i);
                if (cur >= obj.Count) continue;
                while (q.Currents.Count <= i) q.Currents.Add(0);
                q.Currents[i] = cur + 1;
                lastChanged = (q.Currents[i], obj.Count);
                anyChanged = true;
            }
            if (!anyChanged) continue;

            q.Completed = IsAllCompleted(objectives, q.Currents);
            results.Add((def.Title, lastChanged.Cur, lastChanged.Tgt, q.Completed));
        }
        return results;
    }

    /// <summary>
    /// Прогресс talk-квестов при разговоре с NPC: все активные невыполненные
    /// talk-цели, нацеленные на этого NPC, получают +1 к прогрессу.
    /// </summary>
    public void IncrementTalkProgress(Player player, string npcId)
        => IncrementProgress(player, "talk", obj => obj.Target == npcId);

    public List<(string Title, int Current, int Target, bool Completed)> IncrementKillProgress(Player player, string monsterTemplateId)
        => IncrementProgress(player, "kill", obj => obj.Target == monsterTemplateId);

    public List<(string Title, int Current, int Target, bool Completed)> IncrementCollectProgress(Player player, string itemId)
        => IncrementProgress(player, "collect", obj => obj.Target == itemId);

    /// <summary>
    /// Прогресс travel-квестов: цель — NPC (рядом с ним) или точка на карте.
    /// Проверяется после каждого перемещения игрока.
    /// </summary>
    public List<(string Title, int Current, int Target, bool Completed)> IncrementTravelProgress(Player player, string zoneId, int x, int y)
        => IncrementProgress(player, "travel", obj =>
        {
            if (!string.IsNullOrEmpty(obj.Target))
            {
                var npc = NpcLookup?.Invoke(zoneId, obj.Target);
                return npc != null && Math.Abs(npc.X - x) + Math.Abs(npc.Y - y) <= 1;
            }
            return obj.TargetX == x && obj.TargetY == y;
        });

    /// <summary>
    /// Прогресс use-квестов: используется предмет нужного шаблона.
    /// Проверяется после успешного использования предмета.
    /// </summary>
    public List<(string Title, int Current, int Target, bool Completed)> IncrementUseProgress(Player player, string itemId)
        => IncrementProgress(player, "use", obj => obj.Target == itemId);

    /// <summary>
    /// Прогресс explore-квестов: игрок вошёл в целевую зону.
    /// Проверяется при смене зоны.
    /// </summary>
    public List<(string Title, int Current, int Target, bool Completed)> IncrementExploreProgress(Player player, string zoneId)
        => IncrementProgress(player, "explore", obj => obj.Target == zoneId);

    /// <summary>
    /// Авто-выдача квестов при входе в зону: выдаёт все квесты с auto_grant,
    /// которые можно взять (и которые привязаны к этой зоне или без привязки).
    /// </summary>
    public List<QuestDefinition> TryAutoGrant(Player player, string zoneId)
    {
        var granted = new List<QuestDefinition>();
        foreach (var def in _quests.ToList())
        {
            if (!def.AutoGrant) continue;
            if (!string.IsNullOrEmpty(def.TargetZoneId) && def.TargetZoneId != zoneId) continue;
            if (!CanTakeQuest(player, def)) continue;
            if (TakeQuest(player, def))
                granted.Add(def);
        }
        return granted;
    }

    /// <summary>
    /// Является ли предмет квестовым (флаг quest_item на шаблоне или на луте) —
    /// такие нельзя продавать. Обычные собираемые предметы продаются всегда.
    /// </summary>
    public bool IsQuestItem(Item item)
    {
        if (item == null) return false;
        if (!string.IsNullOrEmpty(item.TemplateId) && IsQuestItem(item.TemplateId)) return true;
        if (IsQuestItem(item.Id)) return true;
        // Лут (трофеи) без шаблона опознаётся по названию
        if (string.IsNullOrEmpty(item.TemplateId) && !string.IsNullOrEmpty(item.Name))
        {
            lock (_questItemsLock)
                return _questItemNames.Contains(item.Name);
        }
        return false;
    }

    public bool IsQuestItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        lock (_questItemsLock)
            return _questItemIds.Contains(itemId);
    }
}
