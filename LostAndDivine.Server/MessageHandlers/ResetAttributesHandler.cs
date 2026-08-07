using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;
using System.Text.Json;

namespace LostAndDivine.Server.MessageHandlers;

public class ResetAttributesHandler : BaseHandler
{
    public ResetAttributesHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (player == null) return;

        int spentStrength = player.Strength - 1;
        int spentEndurance = player.Endurance - 1;
        int spentAgility = player.Agility - 1;
        int spentCunning = player.Cunning - 1;
        int spentIntellect = player.Intellect - 1;
        int spentWisdom = player.Wisdom - 1;
        int totalSpent = spentStrength + spentEndurance + spentAgility + spentCunning + spentIntellect + spentWisdom;

        if (totalSpent == 0)
        {
            await SendError(connection, ErrorCodes.InvalidRequest, "Нет потраченных атрибутов для сброса!");
            return;
        }

        player.Strength = 1;
        player.Endurance = 1;
        player.Agility = 1;
        player.Cunning = 1;
        player.Intellect = 1;
        player.Wisdom = 1;

        player.MaxHealth = 100 + (player.Level - 1) * BalanceStatic.MaxHealthPerLevel;
        player.MaxMana = Balance.MaxMana(player.Wisdom);
        if (player.Health > player.MaxHealth) player.Health = player.MaxHealth;
        if (player.Mana > player.MaxMana) player.Mana = player.MaxMana;
        player.AttributePoints += totalSpent;

        Log.Info($"{player.Name} сбросил атрибуты. Возвращено {totalSpent} очков. MaxHP={player.MaxHealth}, MaxMP={player.MaxMana}");
        Svc.Persistence.EnqueueSave(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Атрибуты сброшены! Возвращено {totalSpent} очков." }
        });
        await SendInventoryAndStatus(connection, player);
    }
}