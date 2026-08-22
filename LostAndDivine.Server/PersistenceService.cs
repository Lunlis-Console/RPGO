using System.Collections.Concurrent;
using System.Threading.Channels;
using LostAndDivine.Server.Repositories;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server;

/// <summary>
/// Пакетное сохранение прогресса игроков в БД.
/// Вместо мгновенного SavePlayerProgress при каждом action,
/// игроки ставятся в очередь и сохраняются пачками раз в секунду.
/// Снижает DB load в 10-50x при сохранении на каждый action.
/// Bounded канал на 10000 защищает от OOM при 2000 CCU (P1-1/P1-3).
/// </summary>
public sealed class PersistenceService
{
    private readonly Channel<Player> _channel = Channel.CreateBounded<Player>(new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
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
        // DropOldest при переполнении 10000 — не растём в OOM
        _channel.Writer.TryWrite(player);
    }

    /// <summary>Немедленно сохранить всех грязных игроков (вызывать при shutdown).</summary>
    public void FlushNow()
    {
        var drained = new List<Player>();
        while (_channel.Reader.TryRead(out var player))
            drained.Add(player);
        foreach (var p in drained)
        {
            try
            {
                DatabaseManager.SavePlayerProgress(p);
            }
            catch (Exception ex)
            {
                Log.Error($"[Persistence] Ошибка сохранения {p.Name}", ex);
            }
            lock (_dirtyLock) { _dirtyNames.Remove(p.Name); }
        }
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
        while (_channel.Reader.TryRead(out var player))
            batch.Add(player);

        if (batch.Count == 0) return;

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
            lock (_dirtyLock) { _dirtyNames.Remove(player.Name); }
        }

        Log.Debug($"[Persistence] Сохранено {batch.Count} игроков");
    }
}
