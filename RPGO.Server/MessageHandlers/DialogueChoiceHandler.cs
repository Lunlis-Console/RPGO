using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class DialogueChoiceHandler : BaseHandler
{
    public DialogueChoiceHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null || !player.Dialogue.IsActive) return;
        if (message.Data is not JsonElement el) return;

        int choiceIndex = el.TryGetProperty("ChoiceIndex", out var ci) ? ci.GetInt32() : -1;
        await Svc.Dialogue.HandleChoice(connection, player, choiceIndex);
    }
}
