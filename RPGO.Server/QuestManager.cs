using RPGGame.Shared.Models;

namespace RPGGame.Server;

public static class QuestManager
{
    public static int BoardX
    {
        get => Program.World.Map.BoardX;
        private set => Program.World.Map.BoardX = value;
    }

    public static int BoardY
    {
        get => Program.World.Map.BoardY;
        private set => Program.World.Map.BoardY = value;
    }

    private static List<QuestDefinition> _quests = new();

    public static void Initialize()
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

    public static QuestBoardPosition Board =>
        new QuestBoardPosition { X = BoardX, Y = BoardY, Name = "Доска заданий" };

    public static List<QuestDefinition> GetAvailableQuests() => _quests.ToList();

    public static QuestDefinition? FindQuest(string id) =>
        _quests.FirstOrDefault(q => q.Id == id);

    public static bool IsAtBoard(int x, int y) => Math.Abs(x - BoardX) + Math.Abs(y - BoardY) <= 1;

    public static List<(string Title, int Current, int Target, bool Completed)> IncrementKillProgress(Player player, string monsterTemplateId)
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

    public static List<(string Title, int Current, int Target, bool Completed)> IncrementCollectProgress(Player player, string itemId)
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
