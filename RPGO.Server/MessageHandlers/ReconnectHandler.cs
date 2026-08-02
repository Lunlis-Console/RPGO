using RPGGame.Server.Network;

using RPGGame.Server.Services;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

public class ReconnectHandler : BaseHandler
{
    public ReconnectHandler(GameServices svc) : base(svc) { }

    public override async Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        if (message.Data is not JsonElement el) return;
        var req = JsonSerializer.Deserialize<ReconnectRequest>(el.GetRawText());
        if (req == null)
        {
            await SendError(connection, "invalid_request", "Неверный формат запроса");
            return;
        }

        var playerName = SessionManager.Validate(req.Token);
        if (playerName == null)
        {
            await SendToClient(connection, new GameMessage
            {
                Type = "reconnect_fail",
                Data = new ReconnectResponse(false, "token_expired", null)
            });
            return;
        }

        var existingConn = World.GetConnectionByPlayerName(playerName);
        if (existingConn != null && existingConn != connection)
        {
            Log.Info($"[Reconnect] Kicking old session for {playerName}");
            await SendToClient(existingConn, new GameMessage
            {
                Type = "kick",
                Data = new { Reason = "reconnect" }
            });
            World.RemoveClient(existingConn);
            try { existingConn.Client.Close(); } catch { /* already disconnecting old session */ }
        }

        Player? loadedPlayer;
        if (!World.TryGetPlayerByName(playerName, out loadedPlayer) || loadedPlayer == null)
        {
            // После перезапуска сервера мир пуст — восстанавливаем игрока из БД.
            var account = DatabaseManager.GetAccountByPlayerName(playerName);
            if (account == null || account.IsBanned)
            {
                await SendToClient(connection, new GameMessage
                {
                    Type = "reconnect_fail",
                    Data = new ReconnectResponse(false, "player_not_found", null)
                });
                return;
            }

            Log.Info($"[Reconnect] World empty, reloading {playerName} from DB");
            loadedPlayer = PlayerFactory.FromAccount(account, Svc);
            World.AddPlayer(loadedPlayer);
        }

        // Игрок переподключился: снимаем метку pending, чтобы фоновый sweep
        // не финализировал дисконнект, и выдаём свежий одноразовый токен.
        World.CancelPendingReconnect(loadedPlayer);
        SessionManager.Revoke(req.Token);
        var newToken = SessionManager.CreateToken(playerName);

        connection.Player = loadedPlayer;
        connection.IsReconnecting = false;

        var state = BuildPlayerState(loadedPlayer);
        await SendToClient(connection, new GameMessage
        {
            Type = "reconnect_ok",
            Data = new ReconnectResponse(true, null, state, newToken, loadedPlayer.Id)
        });

        await SendInventoryAndStatus(connection, loadedPlayer);
        await SendQuestLog(connection, loadedPlayer);
        await Hub.SendHotbar(connection, loadedPlayer);
        await Hub.SendSkills(connection);

        // Сразу отправляем свежую карту (у нового соединения тайлы не отправлены
        // ни для одной зоны, поэтому клиент получит и тайлы, и сущности текущей зоны).
        await Hub.BroadcastMapAsync();

        Log.Info($"[Reconnect] {playerName} reconnected successfully");
    }

    private PlayerState BuildPlayerState(Player p)
    {
        return new PlayerState(
            p.Name,
            p.Level,
            p.X,
            p.Y,
            p.Health,
            p.MaxHealth + p.Equipment.GetBonusMaxHealth(),
            p.Mana,
            p.MaxMana,
            p.Experience,
            p.AttributePoints,
            p.SkillPoints,
            p.Strength,
            p.Endurance,
            p.Agility,
            p.Cunning,
            p.Intellect,
            p.Wisdom,
            p.GetPhysAttack(),
            p.GetDefense(),
            p.Gold,
            p.Inventory.Select(i => new ItemState(i.Id, 1, null)).ToList(),
            p.HotbarSlots.Where(s => s != null).Select(s => new HotbarSlotState(
                s!.StartsWith("item:") ? "item" : (s.StartsWith("skill:") ? "skill" : "empty"),
                s
            )).ToList(),
            p.ActiveQuests.Select(q => new ActiveQuestState(q.QuestId, q.Current, q.Completed)).ToList(),
            p.GetDebuffsSnapshot().Select(d => new DebuffState(d.Type.ToString(), d.DisplayName, d.Description, Math.Round(d.Value, 2), d.RemainingMs, d.DurationMs)).ToList()
        );
    }
}
