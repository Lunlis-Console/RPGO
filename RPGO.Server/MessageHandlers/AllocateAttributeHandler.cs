using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class AllocateAttributeHandler : BaseHandler
{
    public AllocateAttributeHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement attrEl) return;

        string? attrName = attrEl.TryGetProperty("Attribute", out var aProp) ? aProp.GetString() : null;
        if (attrName == null || player.AttributePoints <= 0) return;

        bool valid = true;
        switch (attrName)
        {
            case "strength": player.Strength++; break;
            case "stamina": player.Stamina++; player.MaxHealth += Balance.MaxHealthPerStamina; break;
            case "agility": player.Agility++; break;
            case "cunning": player.Cunning++; break;
            case "wisdom": player.Wisdom++; break;
            case "will": player.Will++; break;
            default: valid = false; break;
        }

        if (!valid) return;

        player.AttributePoints--;
        Log.Debug($"{player.Name} повысил {attrName} (+1). Очков: {player.AttributePoints}");
        // Сохраняем сразу, чтобы изменения не потерялись при выходе без корректного logout
        DatabaseManager.SavePlayerProgress(player);
        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"+1 {attrName}. Осталось очков: {player.AttributePoints}" }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
