using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public static class AdminCommands
{
    public static async Task<bool> TryHandle(
        ClientConnection connection, Player player, string text,
        GameWorld world, INetworkHub hub)
    {
        if (!player.IsAdmin) return false;

        var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return false;

        string cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/gold":
                return await HandleGold(connection, player, args, hub);
            case "/item":
                return await HandleItem(connection, player, args, hub);
            case "/tp":
                return await HandleTeleport(connection, player, args, world, hub);
            case "/kick":
                return await HandleKick(player, args, world, hub);
            case "/ban":
                return await HandleBan(player, args, world, hub);
            case "/unban":
                return await HandleUnban(player, args, hub);
            default:
                return false;
        }
    }

    private static async Task<bool> HandleGold(ClientConnection connection, Player player, string[] args, INetworkHub hub)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int amount))
        {
            await SystemToSelf(player, hub, "Использование: /gold <количество>");
            return true;
        }

        player.Gold += amount;
        await SystemToSelf(player, hub, $"Золото: {player.Gold} (+{amount})");
        await hub.SendInventoryAndStatus(connection, player);
        return true;
    }

    private static async Task<bool> HandleItem(ClientConnection connection, Player player, string[] args, INetworkHub hub)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, hub, "Использование: /item <id> [количество]");
            return true;
        }

        string templateId = args[1];
        int count = 1;
        if (args.Length >= 3 && int.TryParse(args[2], out int parsed))
            count = Math.Max(1, parsed);

        var template = DatabaseManager.GetItemTemplate(templateId);
        if (template == null)
        {
            await SystemToSelf(player, hub, $"Предмет с ID «{templateId}» не найден.");
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            var clone = template.Clone();
            clone.Id = Guid.NewGuid().ToString();
            clone.Quantity = 1;
            player.Inventory.Add(clone);
        }

        await SystemToSelf(player, hub, $"Выдано: {template.Name} x{count}");
        await hub.SendInventoryAndStatus(connection, player);
        return true;
    }

    private static async Task<bool> HandleTeleport(ClientConnection connection, Player player, string[] args, GameWorld world, INetworkHub hub)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, hub, "Использование: /tp <x> <y> или /tp <имя>");
            return true;
        }

        if (args.Length >= 3 && int.TryParse(args[1], out int tx) && int.TryParse(args[2], out int ty))
        {
            player.X = Math.Clamp(tx, 0, world.Map.Width - 1);
            player.Y = Math.Clamp(ty, 0, world.Map.Height - 1);
            await SystemToSelf(player, hub, $"Телепорт: ({player.X}, {player.Y})");
            await hub.SendInventoryAndStatus(connection, player);
            await hub.BroadcastMapAsync();
            return true;
        }

        string targetName = args[1];
        if (world.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            player.X = target.X;
            player.Y = target.Y;
            await SystemToSelf(player, hub, $"Телепорт к {target.Name}: ({player.X}, {player.Y})");
            await hub.SendInventoryAndStatus(connection, player);
            await hub.BroadcastMapAsync();
            return true;
        }

        await SystemToSelf(player, hub, $"Игрок «{targetName}» не найден.");
        return true;
    }

    private static async Task<bool> HandleKick(Player player, string[] args, GameWorld world, INetworkHub hub)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, hub, "Использование: /kick <имя>");
            return true;
        }

        string targetName = args[1];
        if (!world.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SystemToSelf(player, hub, $"Игрок «{targetName}» не найден.");
            return true;
        }

        var targetConn = world.FindClientByPlayer(target);
        if (targetConn == null)
        {
            await SystemToSelf(player, hub, $"Игрок «{targetName}» не найден.");
            return true;
        }

        await hub.KickPlayer(targetConn, "Вы были кикнуты администратором.");
        await SystemToSelf(player, hub, $"Игрок {target.Name} кикнут.");
        return true;
    }

    private static async Task<bool> HandleBan(Player player, string[] args, GameWorld world, INetworkHub hub)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, hub, "Использование: /ban <имя> [причина]");
            return true;
        }

        string targetName = args[1];
        string reason = args.Length >= 3 ? string.Join(' ', args.Skip(2)) : "Без причины";

        var login = DatabaseManager.GetLoginByPlayerName(targetName);
        if (login == null)
        {
            await SystemToSelf(player, hub, $"Игрок «{targetName}» не найден.");
            return true;
        }

        DatabaseManager.SetBanned(login, true, reason);

        if (world.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            var targetConn = world.FindClientByPlayer(target);
            if (targetConn != null)
                await hub.KickPlayer(targetConn, $"Вы заблокированы. Причина: {reason}");
        }

        await SystemToSelf(player, hub, $"Игрок {targetName} заблокирован. Причина: {reason}");
        return true;
    }

    private static async Task<bool> HandleUnban(Player player, string[] args, INetworkHub hub)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, hub, "Использование: /unban <имя>");
            return true;
        }

        string targetName = args[1];
        var login = DatabaseManager.GetLoginByPlayerName(targetName);
        if (login == null)
        {
            await SystemToSelf(player, hub, $"Игрок «{targetName}» не найден.");
            return true;
        }

        DatabaseManager.SetBanned(login, false, "");
        await SystemToSelf(player, hub, $"Игрок {targetName} разблокирован.");
        return true;
    }

    private static async Task SystemToSelf(Player player, INetworkHub hub, string msg)
    {
        var conn = Program.Services.World.FindClientByPlayer(player);
        if (conn != null)
            await hub.SendChatToAsync(conn, ChatChannel.System, "Система", msg);
    }
}
