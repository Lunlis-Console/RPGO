using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class ResetSkillsHandler : BaseHandler
{
    public ResetSkillsHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        int refunded = player.LearnedSkills.Count;
        if (refunded == 0)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Нет изученных навыков для сброса!");
            return;
        }

        player.LearnedSkills.Clear();
        player.SkillPoints = player.Level / 2;

        Log.Info($"{player.Name} сбросил навыки. Возвращено {player.SkillPoints} очков.");
        DatabaseManager.SavePlayerProgress(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Навыки сброшены! Все очки навыков возвращены ({player.SkillPoints})." }
        });
        await Hub.SendSkills(connection);
    }
}
