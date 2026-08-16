using LostAndDivine.Server;
using LostAndDivine.Server.Network;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using Microsoft.Data.Sqlite;
using System.Reflection;
using Xunit;
using System.Threading.Tasks;

namespace LostAndDivine.Tests;

public class ElderDialogueQuestTests
{
    private sealed class StubHub : INetworkHub
    {
        public Task BroadcastMapAsync() => Task.CompletedTask;
        public Task BroadcastChatAsync(string playerName, string text) => Task.CompletedTask;
        public Task BroadcastChatAsync(ChatChannel channel, string from, string text) => Task.CompletedTask;
        public Task SendChatToAsync(ClientConnection connection, ChatChannel channel, string from, string text, string? to = null) => Task.CompletedTask;
        public Task SendToClient(ClientConnection connection, GameMessage message) => Task.CompletedTask;
        public Task SendToAllAsync(GameMessage message) => Task.CompletedTask;
        public Task SendStatusAsync(ClientConnection connection, Player player) => Task.CompletedTask;
        public Task SendInventoryAndStatus(ClientConnection connection, Player player, bool fromUnequip = false) => Task.CompletedTask;
        public Task SendDamageNearbyAsync(int x, int y, GameMessage damageMsg, Player? exclude) => Task.CompletedTask;
        public Task SendQuestLog(ClientConnection connection, Player player) => Task.CompletedTask;
        public Task SendHotbar(ClientConnection connection, Player player) => Task.CompletedTask;
        public Task SendSkills(ClientConnection connection) => Task.CompletedTask;
        public Task SendError(ClientConnection connection, string code, string message) => Task.CompletedTask;
        public Task SendFriendListToAsync(ClientConnection connection, Player player) => Task.CompletedTask;
        public StatsBreakdown BuildBreakdown(Player player) => new();
        public Task KickPlayer(ClientConnection connection, string reason) => Task.CompletedTask;
        public Task SendZoneTransition(ClientConnection connection, Player player) => Task.CompletedTask;
        public void LoadNpcCache() { }
        public NpcPosition? FindNpcAt(string zoneId, int x, int y) => null;
        public NpcPosition? FindNpcById(string zoneId, string npcId) => null;
        public void MarkZoneDirty(string zoneId) { }
    }

    private static string ContentDbPath()
    {
        var bin = AppContext.BaseDirectory;
        // LostAndDivine.Tests/bin/Debug/net8.0 -> solution root is 4 levels up
        var root = Path.GetFullPath(Path.Combine(bin, "..", "..", "..", ".."));
        return Path.Combine(root, "LostAndDivine.Server", "content.db");
    }

    private static (DialogueManager dm, QuestManager qm) Build()
    {
        var world = new GameWorld(100, 100);
        var qm = new QuestManager(world);
        var merchant = new MerchantManager(world);
        var pathfinding = new PathfindingService(world, merchant, qm);
        var hub = new StubHub();
        var dm = new DialogueManager(world, qm, merchant);

        // Load real Q0007 definition
        using var conn = new SqliteConnection($"Data Source={ContentDbPath()}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,type,target_monster_id,target_npc_id,target,xp_reward,gold_reward,min_level,giver_npc_id FROM quests_def WHERE id='Q0007'";
        using var r = cmd.ExecuteReader();
        r.Read();
        var def = new QuestDefinition
        {
            Id = r.GetString(0),
            Type = r.GetString(1),
            TargetMonsterId = r.IsDBNull(2) ? "" : r.GetString(2),
            TargetNpcId = r.IsDBNull(3) ? "" : r.GetString(3),
            Target = r.GetInt32(4),
            XpReward = r.GetInt32(5),
            GoldReward = r.GetInt32(6),
            MinLevel = r.GetInt32(7),
            GiverNpcId = r.IsDBNull(8) ? "" : r.GetString(8),
        };
        qm.SetDefinitions(new[] { def });

        // Load real N0003 dialogue and inject into cache
        using var c2 = new SqliteConnection($"Data Source={ContentDbPath()}");
        c2.Open();
        using var cmd2 = c2.CreateCommand();
        cmd2.CommandText = "SELECT data FROM npcs WHERE id='N0003'";
        var data = cmd2.ExecuteScalar()?.ToString();
        var tree = DialogueParser.Parse(data);
        Assert.NotNull(tree);
        var cache = (System.Collections.IDictionary)typeof(DialogueManager)
            .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(dm)!;
        cache["N0003"] = tree!;

        var svc = new GameServices(world, hub, null!, null!, null!, qm, merchant, null!, null!, dm, null!, null!, null!, pathfinding, null!, null!, null!, null!, null!, null!);
        dm.SetHub(hub);
        dm.SetServices(svc);
        return (dm, qm);
    }

    [Fact]
    public async Task ElderBranch_Grants_Q0007_ToFreshPlayer()
    {
        var (dm, qm) = Build();
        var player = new Player { Name = "Tester", Level = 1 };
        player.Dialogue.Start("N0003", "greeting");

        // greeting -> "Что случилось?" is index 0
        await dm.HandleChoice(null, player, 0);
        Assert.Equal("story1", player.Dialogue.CurrentNodeId);

        // story1 -> "Волки? Разве это серьёзная угроза?" is index 0
        await dm.HandleChoice(null, player, 0);
        Assert.Equal("story2", player.Dialogue.CurrentNodeId);

        // story2 -> "Я помогу..." (accept_quest:Q0007) is index 0
        await dm.HandleChoice(null, player, 0);

        Assert.Contains(player.ActiveQuests, q => q.QuestId == "Q0007");
    }

    [Fact]
    public void ElderBranch_Hidden_WhenQ0007Completed()
    {
        var (dm, qm) = Build();
        var player = new Player { Name = "Tester", Level = 1 };
        player.CompletedQuestIds.Add("Q0007");
        var tree = dm.GetTree("N0003");
        Assert.NotNull(tree);

        var greetingVisible = dm.FilterChoices(tree!.Nodes["greeting"].Choices, player);
        Assert.DoesNotContain(greetingVisible, c => c.NextNodeId == "story1");

        var story2Visible = dm.FilterChoices(tree.Nodes["story2"].Choices, player);
        Assert.DoesNotContain(story2Visible, c => c.Action == "accept_quest:Q0007");
    }
}
