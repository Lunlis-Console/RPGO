using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class ResetAttributesHandler : BaseHandler
{
    public ResetAttributesHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

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
        DatabaseManager.SavePlayerProgress(player);

        await SendToClient(connection, new GameMessage
        {
            Type = "chat",
            Data = new { Name = "Система", Text = $"Атрибуты сброшены! Возвращено {totalSpent} очков." }
        });
        await SendInventoryAndStatus(connection, player);
    }
}