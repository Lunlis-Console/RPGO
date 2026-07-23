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

    public QuestManager(GameWorld world)
    {
        _world = world;
    }

    public void Initialize()
    {
        var npc = DatabaseManager.LoadNpcs().FirstOrDefault(n => n.Type == "board");
        if (npc != null)
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

    public QuestBoardPosition Board =>
        new QuestBoardPosition { X = BoardX, Y = BoardY, Name = "Доска заданий" };

    public List<QuestDefinition> GetAvailableQuests() => _quests.ToList();

    public QuestDefinition? FindQuest(string id) =>
        _quests.FirstOrDefault(q => q.Id == id);

    public bool IsAtBoard(int x, int y) => Math.Abs(x - BoardX) + Math.Abs(y - BoardY) <= 1;

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
