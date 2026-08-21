using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using System.Net.Sockets;

namespace LostAndDivine.ClientMonoGame;

/// <summary>
/// Фасад обновлений для клиента. Вся логика (сеть, проверка подписи, применение)
/// вынесена в переиспользуемый LostAndDivine.Shared.GameUpdater, чтобы не дублироваться
/// с лаунчером.
/// </summary>
public static class UpdateManager
{
    public static string BaseDir => GameUpdater.BaseDir;
    public static string LocalVersion => GameUpdater.LocalVersion;

    /// <summary>Результат проверки обновлений.</summary>
    public sealed class UpdateCheckResult
    {
        public bool RestartRequired { get; init; }
        public string Message { get; init; } = "";
    }

    /// <summary>
    /// Стартовая проверка обновлений (из GameMain.Initialize). Возвращает true,
    /// если обновление применено и нужен перезапуск.
    /// </summary>
    public static bool RunStartupCheck()
    {
        try
        {
            if (!File.Exists(GameUpdater.VersionFile))
                return false; // dev-сборка без version.json: авто-апдейт не запускаем

            string ip = SettingsManager.Load().ServerIp;
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return GameUpdater.CheckAndApplyAsync(ip, null, cts.Token).GetAwaiter().GetResult().RestartRequired;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Update] Стартовая проверка пропущена: {ex.Message}");
            return false;
        }
    }

    /// <summary>Ручная проверка обновлений (из экрана логина).</summary>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(string ip, CancellationToken ct = default)
    {
        var r = await GameUpdater.CheckAndApplyAsync(ip, null, ct);
        return new UpdateCheckResult { RestartRequired = r.RestartRequired, Message = r.Message };
    }

    /// <summary>Запускает apply.cmd и завершает процесс (применение обновления).</summary>
    public static void RestartToApply() => GameUpdater.RestartToApply();
}
