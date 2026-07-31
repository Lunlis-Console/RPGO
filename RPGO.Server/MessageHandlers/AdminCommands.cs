using RPGGame.Server.Network;
using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;

namespace RPGGame.Server.MessageHandlers;

public static class AdminCommands
{
    public static async Task<bool> TryHandle(
        ClientConnection connection, Player player, string text,
        GameServices svc)
    {
        if (!player.IsAdmin) return false;

        var args = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0) return false;

        string cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/gold":
                return await HandleGold(connection, player, args, svc);
            case "/item":
                return await HandleItem(connection, player, args, svc);
            case "/tp":
                return await HandleTeleport(connection, player, args, svc);
            case "/kick":
                return await HandleKick(player, args, svc);
            case "/ban":
                return await HandleBan(player, args, svc);
            case "/unban":
                return await HandleUnban(connection, player, args, svc);
            case "/level":
                return await HandleLevel(connection, player, args, svc);
            default:
                return false;
        }
    }

    private static async Task<bool> HandleGold(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int amount))
        {
            await SystemToSelf(player, svc, "Использование: /gold <количество>");
            return true;
        }

        player.Gold += amount;
        await SystemToSelf(player, svc, $"Золото: {player.Gold} (+{amount})");
        await svc.Hub.SendInventoryAndStatus(connection, player);
        return true;
    }

    private static async Task<bool> HandleItem(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /item <id> [количество]");
            return true;
        }

        string templateId = args[1];
        int count = 1;
        if (args.Length >= 3 && int.TryParse(args[2], out int parsed))
            count = Math.Max(1, parsed);

        var template = DatabaseManager.GetItemTemplate(templateId);
        if (template == null)
        {
            await SystemToSelf(player, svc, $"Предмет с ID «{templateId}» не найден.");
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            var clone = template.Clone();
            clone.Id = Guid.NewGuid().ToString();
            clone.Quantity = 1;
            player.Inventory.Add(clone);
        }

        await SystemToSelf(player, svc, $"Выдано: {template.Name} x{count}");
        await svc.Hub.SendInventoryAndStatus(connection, player);
        return true;
    }

    private static async Task<bool> HandleTeleport(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /tp <x> <y> или /tp <имя> или /tp <zone> <x> <y>");
            return true;
        }

        if (args.Length >= 4 && int.TryParse(args[1], out int tz) && int.TryParse(args[2], out int tx2) && int.TryParse(args[3], out int ty2))
        {
            var zoneId = args[0] == "/tp" ? player.CurrentZoneId : args[1];
            var zone = svc.Zones.GetZone(zoneId);
            if (zone == null)
            {
                await SystemToSelf(player, svc, $"Зона '{zoneId}' не найдена.");
                return true;
            }
            player.CurrentZoneId = zoneId;
            player.X = Math.Clamp(tx2, 0, zone.Width - 1);
            player.Y = Math.Clamp(ty2, 0, zone.Height - 1);
            await SystemToSelf(player, svc, $"Телепорт в '{zone.Name}': ({player.X}, {player.Y})");
            await svc.Hub.SendInventoryAndStatus(connection, player);
            await svc.Hub.BroadcastMapAsync();
            return true;
        }

        if (args.Length >= 3 && int.TryParse(args[1], out int tx) && int.TryParse(args[2], out int ty))
        {
            var zoneMap = svc.Zones.GetOrCreateMap(player.CurrentZoneId);
            player.X = Math.Clamp(tx, 0, zoneMap.Width - 1);
            player.Y = Math.Clamp(ty, 0, zoneMap.Height - 1);
            await SystemToSelf(player, svc, $"Телепорт: ({player.X}, {player.Y})");
            await svc.Hub.SendInventoryAndStatus(connection, player);
            await svc.Hub.BroadcastMapAsync();
            return true;
        }

        string targetName = args[1];
        if (svc.World.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            player.X = target.X;
            player.Y = target.Y;
            await SystemToSelf(player, svc, $"Телепорт к {target.Name}: ({player.X}, {player.Y})");
            await svc.Hub.SendInventoryAndStatus(connection, player);
            await svc.Hub.BroadcastMapAsync();
            return true;
        }

        await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
        return true;
    }

    private static async Task<bool> HandleKick(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /kick <имя>");
            return true;
        }

        string targetName = args[1];
        if (!svc.World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }

        var targetConn = svc.World.FindClientByPlayer(target);
        if (targetConn == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }

        await svc.Hub.KickPlayer(targetConn, "Вы были кикнуты администратором.");
        await SystemToSelf(player, svc, $"Игрок {target.Name} кикнут.");
        return true;
    }

    private static async Task<bool> HandleBan(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /ban <имя> [причина]");
            return true;
        }

        string targetName = args[1];
        string reason = args.Length >= 3 ? string.Join(' ', args.Skip(2)) : "Без причины";

        var login = DatabaseManager.GetLoginByPlayerName(targetName);
        if (login == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }

        DatabaseManager.SetBanned(login, true, reason);

        if (svc.World.TryGetPlayerByName(targetName, out var target) && target != null)
        {
            var targetConn = svc.World.FindClientByPlayer(target);
            if (targetConn != null)
                await svc.Hub.KickPlayer(targetConn, $"Вы заблокированы. Причина: {reason}");
        }

        await SystemToSelf(player, svc, $"Игрок {targetName} заблокирован. Причина: {reason}");
        return true;
    }

    private static async Task<bool> HandleUnban(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /unban <имя>");
            return true;
        }

        string targetName = args[1];
        var login = DatabaseManager.GetLoginByPlayerName(targetName);
        if (login == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }

        DatabaseManager.SetBanned(login, false, "");
        await SystemToSelf(player, svc, $"Игрок {targetName} разблокирован.");
        return true;
    }

    private static async Task<bool> HandleLevel(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int targetLevel) || targetLevel < 1)
        {
            await SystemToSelf(player, svc, "Использование: /level <уровень> [имя_игрока]");
            return true;
        }

        targetLevel = Math.Min(targetLevel, BalanceStatic.MaxLevel);

        Player target;
        ClientConnection? targetConn;

        if (args.Length >= 3)
        {
            string targetName = args[2];
            if (!svc.World.TryGetPlayerByName(targetName, out var found) || found == null)
            {
                await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
                return true;
            }
            target = found;
            targetConn = svc.World.FindClientByPlayer(target);
        }
        else
        {
            target = player;
            targetConn = connection;
        }

        int oldLevel = target.Level;
        target.Level = targetLevel;
        target.MaxHealth = 100 + (targetLevel - 1) * BalanceStatic.MaxHealthPerLevel;
        target.Health = target.MaxHealth;
        target.AttributePoints = (targetLevel - 1) * BalanceStatic.AttributePointsPerLevel;
        target.SkillPoints = targetLevel / 2;
        target.Experience = 0;

        svc.Persistence.EnqueueSave(target);

        string diff = targetLevel > oldLevel ? $"+{targetLevel - oldLevel}" : $"{targetLevel - oldLevel}";
        string who = target == player ? "" : $" для {target.Name}";
        await SystemToSelf(player, svc, $"Уровень изменён{who}: {oldLevel} → {targetLevel} ({diff}). HP/Очк. навыков/атрибутов обновлены.");

        if (target == player)
        {
            await svc.Hub.SendInventoryAndStatus(connection, player);
        }
        else
        {
            if (targetConn != null)
                await svc.Hub.SendInventoryAndStatus(targetConn, target);
            await svc.Hub.SendInventoryAndStatus(connection, player);
        }

        return true;
    }

    private static async Task SystemToSelf(Player player, GameServices svc, string msg)
    {
        var conn = svc.World.FindClientByPlayer(player);
        if (conn != null)
            await svc.Hub.SendChatToAsync(conn, ChatChannel.System, "Система", msg);
    }
}
