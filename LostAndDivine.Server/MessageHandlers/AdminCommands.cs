using LostAndDivine.Server.Network;
using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Server.MessageHandlers;

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
            case "/help":
                return await HandleHelp(player, svc);
            case "/who":
                return await HandleWho(player, svc);
            case "/goto":
                return await HandleGoto(player, args, svc);
            case "/bring":
                return await HandleBring(player, args, svc);
            case "/heal":
                return await HandleHeal(connection, player, args, svc);
            case "/announce":
                return await HandleAnnounce(player, args, svc);
            case "/reload":
                return await HandleReloadContent(player, svc);
            case "/save":
                return await HandleSave(player, svc);
            case "/mute":
                return await HandleMute(player, args, svc);
            case "/unmute":
                return await HandleUnmute(player, args, svc);
            case "/setadmin":
                return await HandleSetAdmin(player, args, svc);
            case "/spawn":
                return await HandleSpawn(player, args, svc);
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

    private static async Task<bool> HandleHelp(Player player, GameServices svc)
    {
        var lines = new[]
        {
            "/help — список команд",
            "/who — список игроков онлайн",
            "/gold <количество> — выдать себе золото",
            "/item <id> [количество] — выдать предмет",
            "/tp <x> <y> или /tp <имя> или /tp <zone> <x> <y> — телепортация",
            "/goto <имя> — телепорт к игроку",
            "/bring <имя> — телепорт игрока к себе",
            "/heal [имя] — восстановить HP/MP",
            "/announce <текст> — объявление на весь сервер",
            "/kick <имя> — кикнуть игрока",
            "/ban <имя> [причина] — заблокировать",
            "/unban <имя> — разблокировать",
            "/mute <имя> — заглушить игрока",
            "/unmute <имя> — снять глушение",
            "/level <уровень> [имя] — изменить уровень",
            "/setadmin <имя> — выдать/снять админку",
            "/spawn <id_монстра> — заспавнить монстра",
            "/reload — перезагрузить контент",
            "/save — сохранить всех игроков",
        };
        foreach (var line in lines)
            await SystemToSelf(player, svc, line);
        return true;
    }

    private static async Task<bool> HandleWho(Player player, GameServices svc)
    {
        var players = svc.World.GetPlayersSnapshot();
        await SystemToSelf(player, svc, $"Онлайн игроков: {players.Count}");
        foreach (var p in players)
        {
            string admin = p.IsAdmin ? " [A]" : "";
            await SystemToSelf(player, svc, $"  {p.Name} (ур.{p.Level}){admin}");
        }
        return true;
    }

    private static async Task<bool> HandleGoto(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /goto <имя>");
            return true;
        }
        string targetName = args[1];
        if (!svc.World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }
        player.X = target.X;
        player.Y = target.Y;
        player.CurrentZoneId = target.CurrentZoneId;
        await SystemToSelf(player, svc, $"Телепорт к {target.Name}: ({player.X}, {player.Y})");
        var conn = svc.World.FindClientByPlayer(player);
        if (conn != null)
            await svc.Hub.SendInventoryAndStatus(conn, player);
        await svc.Hub.BroadcastMapAsync();
        return true;
    }

    private static async Task<bool> HandleBring(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /bring <имя>");
            return true;
        }
        string targetName = args[1];
        if (!svc.World.TryGetPlayerByName(targetName, out var target) || target == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }
        target.X = player.X;
        target.Y = player.Y;
        target.CurrentZoneId = player.CurrentZoneId;
        await SystemToSelf(player, svc, $"{target.Name} телепортирован к вам.");
        var targetConn = svc.World.FindClientByPlayer(target);
        if (targetConn != null)
        {
            await svc.Hub.SendChatToAsync(targetConn, ChatChannel.System, "Система", "Администратор телепортировал вас к себе.");
            await svc.Hub.SendInventoryAndStatus(targetConn, target);
        }
        await svc.Hub.BroadcastMapAsync();
        return true;
    }

    private static async Task<bool> HandleHeal(ClientConnection connection, Player player, string[] args, GameServices svc)
    {
        Player target;
        ClientConnection? targetConn;
        if (args.Length >= 2)
        {
            string targetName = args[1];
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
        target.Health = target.MaxHealth;
        target.Mana = target.MaxMana;
        await SystemToSelf(player, svc, $"HP/MP восстановлены{(target != player ? $" для {target.Name}" : "")}.");
        if (targetConn != null)
            await svc.Hub.SendInventoryAndStatus(targetConn, target);
        return true;
    }

    private static async Task<bool> HandleAnnounce(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /announce <текст>");
            return true;
        }
        string text = string.Join(' ', args.Skip(1));
        await svc.Hub.BroadcastChatAsync(ChatChannel.System, "Администратор", text);
        return true;
    }

    private static async Task<bool> HandleReloadContent(Player player, GameServices svc)
    {
        await SystemToSelf(player, svc, "Перезагрузка контента...");
        var conn = svc.World.FindClientByPlayer(player);
        await svc.ReloadContent(conn);
        return true;
    }

    private static async Task<bool> HandleSave(Player player, GameServices svc)
    {
        var players = svc.World.GetPlayersSnapshot();
        foreach (var p in players)
            svc.Persistence.EnqueueSave(p);
        await SystemToSelf(player, svc, $"Сохранено {players.Count} игроков.");
        return true;
    }

    private static async Task<bool> HandleMute(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /mute <имя>");
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
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не в сети.");
            return true;
        }
        targetConn.IsMuted = true;
        await SystemToSelf(player, svc, $"{target.Name} заглушен.");
        await svc.Hub.SendChatToAsync(targetConn, ChatChannel.System, "Система", "Вы заглушены администратором.");
        return true;
    }

    private static async Task<bool> HandleUnmute(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /unmute <имя>");
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
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не в сети.");
            return true;
        }
        targetConn.IsMuted = false;
        await SystemToSelf(player, svc, $"Глушение с {target.Name} снято.");
        await svc.Hub.SendChatToAsync(targetConn, ChatChannel.System, "Система", "Глушение снято администратором.");
        return true;
    }

    private static async Task<bool> HandleSetAdmin(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /setadmin <имя>");
            return true;
        }
        string targetName = args[1];
        var login = DatabaseManager.GetLoginByPlayerName(targetName);
        if (login == null)
        {
            await SystemToSelf(player, svc, $"Игрок «{targetName}» не найден.");
            return true;
        }

        bool isCurrentlyAdmin;
        if (svc.World.TryGetPlayerByName(targetName, out var target) && target != null)
            isCurrentlyAdmin = target.IsAdmin;
        else
            isCurrentlyAdmin = DatabaseManager.GetAccountByPlayerName(targetName)?.IsAdmin ?? false;

        bool newStatus = !isCurrentlyAdmin;
        DatabaseManager.SetAdmin(login, newStatus);
        string status = newStatus ? "выдана" : "снята";
        await SystemToSelf(player, svc, $"Админка {status} для {targetName}.");

        if (svc.World.TryGetPlayerByName(targetName, out var online) && online != null)
        {
            online.IsAdmin = newStatus;
            var targetConn = svc.World.FindClientByPlayer(online);
            if (targetConn != null)
            {
                targetConn.IsAdmin = newStatus;
                await svc.Hub.SendChatToAsync(targetConn, ChatChannel.System, "Система",
                    newStatus ? "Вам выданы права администратора." : "Права администратора сняты.");
            }
        }
        return true;
    }

    private static async Task<bool> HandleSpawn(Player player, string[] args, GameServices svc)
    {
        if (args.Length < 2)
        {
            await SystemToSelf(player, svc, "Использование: /spawn <id_монстра> [x] [y]");
            return true;
        }
        string templateId = args[1];
        int spawnX = player.X + 1;
        int spawnY = player.Y;
        if (args.Length >= 4 && int.TryParse(args[2], out int sx) && int.TryParse(args[3], out int sy))
        {
            spawnX = sx;
            spawnY = sy;
        }
        bool success = svc.Monsters.SpawnNamedMonster(spawnX, spawnY, templateId);
        if (success)
            await SystemToSelf(player, svc, $"Монстр «{templateId}» заспавнен на ({spawnX}, {spawnY}).");
        else
            await SystemToSelf(player, svc, $"Не удалось заспавнить «{templateId}». Проверьте ID и проходимость клетки.");
        return true;
    }
}
