using RPGGame.Server.Network;
using RPGGame.Shared.Models;
using RPGGame.Shared.Network;
using System.Text.Json;

namespace RPGGame.Server.MessageHandlers;

/// <summary>
/// Принимает логи от клиента и перенаправляет их в серверный лог-файл
/// (logs/server-*.log), помечая именем игрока для удобства отладки.
/// </summary>
public class ClientLogHandler : BaseHandler
{
    public ClientLogHandler(GameWorld world, INetworkHub hub) : base(world, hub) { }

    public override Task Handle(ClientConnection connection, GameMessage message, Player? player)
    {
        string level = "Info";
        string text = "";

        if (message.Data is JsonElement el)
        {
            if (el.TryGetProperty("Level", out var lp)) level = lp.GetString() ?? level;
            if (el.TryGetProperty("Message", out var mp)) text = mp.GetString() ?? "";
        }

        string who = player?.Name ?? "unknown";
        string full = $"[CLIENT:{who}] {text}";

        switch (level)
        {
            case "Debug": Log.Debug(full); break;
            case "Warn":  Log.Warn(full); break;
            case "Error": Log.Error(full); break;
            default:      Log.Info(full); break;
        }

        return Task.CompletedTask;
    }
}
