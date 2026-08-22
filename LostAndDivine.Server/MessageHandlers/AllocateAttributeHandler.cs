using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class AllocateAttributeHandler : BaseHandler
{
    public AllocateAttributeHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;
        if (message.Data is not JsonElement attrEl) return;

        string? attrName = attrEl.TryGetProperty("Attribute", out var aProp) ? aProp.GetString() : null;
        if (attrName == null || player.AttributePoints <= 0) return;

        bool valid = true;
        int beforeVal = 0;
        switch (attrName)
        {
            case "strength": beforeVal = player.Strength; break;
            case "endurance": beforeVal = player.Endurance; break;
            case "agility": beforeVal = player.Agility; break;
            case "cunning": beforeVal = player.Cunning; break;
            case "intellect": beforeVal = player.Intellect; break;
            case "wisdom": beforeVal = player.Wisdom; break;
            default: valid = false; break;
        }

        if (!valid) return;
        if (beforeVal >= 50)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, $"{attrName} уже достиг максимума (50)!");
            return;
        }

        switch (attrName)
        {
            case "strength": player.Strength++; break;
            case "endurance": player.Endurance++; player.MaxHealth += Balance.MaxHealthPerEndurance; break;
            case "agility": player.Agility++; break;
            case "cunning": player.Cunning++; break;
            case "intellect": player.Intellect++; break;
            case "wisdom": player.Wisdom++; player.MaxMana += Balance.ManaPerWisdom; break;
        }

        player.AttributePoints--;
        Log.Debug($"{player.Name} повысил {attrName} (+1). Очков: {player.AttributePoints}");
        Svc.Persistence.EnqueueSave(player);
        await SendToClient(connection, new GameMessage
        {
            Type = GameMessageType.Chat,
            Data = new { Name = "Система", Text = $"+1 {attrName}. Осталось очков: {player.AttributePoints}" }
        });
        await SendInventoryAndStatus(connection, player);
    }
}
