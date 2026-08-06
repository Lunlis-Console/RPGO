using System.Collections.Concurrent;
using LostAndDivine.Server.Repositories;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

/// <summary>
/// Пакетное сохранение прогресса игроков в БД.
/// Вместо мгновенного SavePlayerProgress при каждом action,
/// игроки ставятся в очередь и сохраняются пачками раз в секунду.
/// Снижает DB load в 10-50x при сохранении на каждый action.
/// </summary>
public sealed class PersistenceService
{
    private readonly ConcurrentQueue<Player> _dirtyPlayers = new();
    private readonly HashSet<string> _dirtyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _dirtyLock = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public void Start()
    {
        _loopTask = Task.Run(RunLoop);
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
        FlushNow();
    }

    /// <summary>Поставить игрока в очередь на сохранение. Дедупликация по имени.</summary>
    public void EnqueueSave(Player player)
    {
        lock (_dirtyLock)
        {
            if (_dirtyNames.Contains(player.Name)) return;
            _dirtyNames.Add(player.Name);
        }
        _dirtyPlayers.Enqueue(player);
    }

    /// <summary>Немедленно сохранить всех грязных игроков (вызывать при shutdown).</summary>
    public void FlushNow()
    {
        while (_dirtyPlayers.TryDequeue(out var player))
        {
            try
            {
                DatabaseManager.SavePlayerProgress(player);
            }
            catch (Exception ex)
            {
                Log.Error($"[Persistence] Ошибка сохранения {player.Name}", ex);
            }
        }
        lock (_dirtyLock) { _dirtyNames.Clear(); }
    }

    private async Task RunLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, _cts.Token);
                SaveBatch();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Error("[Persistence] Ошибка цикла сохранения", ex);
            }
        }
        FlushNow();
    }

    private void SaveBatch()
    {
        var batch = new List<Player>();
        while (_dirtyPlayers.TryDequeue(out var player))
            batch.Add(player);

        if (batch.Count == 0) return;

        lock (_dirtyLock) { _dirtyNames.Clear(); }

        foreach (var player in batch)
        {
            try
            {
                DatabaseManager.SavePlayerProgress(player);
            }
            catch (Exception ex)
            {
                Log.Error($"[Persistence] Ошибка сохранения {player.Name}", ex);
            }
        }

        Log.Debug($"[Persistence] Сохранено {batch.Count} игроков");
    }
}
