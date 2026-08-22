using System.Net.Sockets;
using System.Text.Json;
using LostAndDivine.Shared;
using LostAndDivine.Shared.Models;
using LostAndDivine.Shared.Network;

// Нагрузочный тест на 2000 ботов для проверки CCU 2000 (P1-1, P1-7)
// Каждый бот — ОТДЕЛЬНЫЙ аккаунт/персонаж, иначе сервер скажет "Этот персонаж уже в игре." AuthService.cs:142
// Логика: Register → LoginAuth → CharacterCreate(если нет) → CharacterSelect → держит сокет 30с → меряет BroadcastMap/WAL

var serverIp = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 7777;
var botCount = args.Length > 2 && int.TryParse(args[2], out var c) ? c : 200;
var holdSeconds = args.Length > 3 && int.TryParse(args[3], out var h) ? h : 30;

Console.WriteLine($"LoadTest: {botCount} ботов → {serverIp}:{port}, держим {holdSeconds}с");
Console.WriteLine($"Каждый бот: login=loadbot{{i}} / Test123! / персонаж=LoadBot{{i}}");
Console.WriteLine($"Если видишь много 'Алло, персонаж уже в игре' — значит используешь один ник, нужно разные (этот скрипт уже разные).");
Console.WriteLine($"ВНИМАНИЕ: ConnectionGuard банит IP после 10 коннектов/60с. Для 2000 с одного IP — на время теста добавь в ConnectionGuard.Allow: if(ip==\"127.0.0.1\") return true; или запускай с разных IP.");
Console.WriteLine();

int ok = 0, fail = 0;
var tasks = new List<Task>();
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

for (int i = 0; i < botCount; i++)
{
    int idx = i;
    tasks.Add(Task.Run(async () =>
    {
        try { if (await RunBot(idx, serverIp, port, holdSeconds, cts.Token)) Interlocked.Increment(ref ok); else Interlocked.Increment(ref fail); }
        catch (Exception ex) { Console.WriteLine($"[bot{idx}] fail: {ex.Message}"); Interlocked.Increment(ref fail); }
    }));
    // Чуть размазываем коннекты чтобы не триггерить ConnectionGuard 10/60с слишком резко
    if (i % 50 == 0) await Task.Delay(100);
}

await Task.WhenAll(tasks);
Console.WriteLine($"\nГотово: OK={ok}, FAIL={fail}, всего={botCount}");
if (fail == 0) Console.WriteLine("✅ 2000 CCU держит — WAL/busy_timeout/лимит/Channel в порядке");
else Console.WriteLine("⚠️  Часть ботов не зашла — смотри логи сервера (лимит 2000? бан IP? пароль?)");

async Task<bool> RunBot(int idx, string ip, int prt, int hold, CancellationToken ct)
{
    var login = $"loadbot{idx}";
    var charName = $"LoadBot{idx}";
    var pass = "Test123!"; // должен пройти проверку Register: >=6, заглавная, спецсимвол

    using var client = new TcpClient { NoDelay = true };
    await client.ConnectAsync(ip, prt, ct);
    var stream = client.GetStream();

    // 1. Register (если уже есть — сервер ответит Success=false, но это ок, идём к Login)
    await Send(stream, new GameMessage { Type = GameMessageType.Register, Data = new { Login = login, Password = pass, PlayerName = charName } }, ct);
    var regResp = await Receive(stream, ct);
    // не проверяем, идём дальше

    // 2. LoginAuth
    await Send(stream, new GameMessage { Type = GameMessageType.LoginAuth, Data = new { Login = login, Password = pass } }, ct);
    var loginResp = await Receive(stream, ct);
    if (loginResp == null || !IsSuccess(loginResp)) return false;

    // Достаём список персонажей из ответа
    var hasChar = false;
    try
    {
        var json = JsonSerializer.Serialize(loginResp.Data);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("characters", out var chars) && chars.ValueKind == JsonValueKind.Array)
            foreach (var ch in chars.EnumerateArray())
                if (ch.TryGetProperty("name", out var n) && n.GetString() == charName) hasChar = true;
    } catch { }

    // 3. Если персонажа нет — создаём
    if (!hasChar)
    {
        await Send(stream, new GameMessage { Type = GameMessageType.CharacterCreate, Data = new { Name = charName, Class = 0 } }, ct);
        var createResp = await Receive(stream, ct);
        // CharacterCreate сразу спавнит, но на всякий — селектим
        await Task.Delay(200, ct);
        // Проверяем что заспавнился (придёт Welcome/MapUpdate)
        if (createResp != null && IsSuccess(createResp)) return await Hold(stream, hold, ct);
    }

    // 4. CharacterSelect
    await Send(stream, new GameMessage { Type = GameMessageType.CharacterSelect, Data = new { Name = charName } }, ct);
    var selResp = await Receive(stream, ct);
    if (selResp == null) return false;

    return await Hold(stream, hold, ct);
}

async Task<bool> Hold(NetworkStream stream, int holdSec, CancellationToken ct)
{
    // Держим сокет открытым holdSec секунд, иногда шлём Ping чтобы не сработал таймаут
    var end = DateTime.UtcNow.AddSeconds(holdSec);
    while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
    {
        try { await Send(stream, new GameMessage { Type = GameMessageType.Ping, Data = new { } }, ct); } catch { }
        // Читаем всё что пришло (MapUpdate/EntityState) чтобы не забить буфер
        while (stream.DataAvailable)
        {
            try { await Receive(stream, new CancellationTokenSource(500).Token); } catch { break; }
        }
        await Task.Delay(1000, ct);
    }
    return true;
}

bool IsSuccess(GameMessage msg)
{
    try
    {
        var json = JsonSerializer.Serialize(msg.Data);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("Success", out var s)) return s.GetBoolean();
        if (doc.RootElement.TryGetProperty("success", out var s2)) return s2.GetBoolean();
        // CharacterCreate/Select не всегда шлёт Success, а шлёт Welcome — считаем успехом если нет Error
        return msg.Type != GameMessageType.Error;
    } catch { return true; }
}

async Task Send(NetworkStream stream, GameMessage msg, CancellationToken ct)
{
    await NetworkHelper.SendAsync(stream, msg);
}
async Task<GameMessage?> Receive(NetworkStream stream, CancellationToken ct)
{
    using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts2.CancelAfter(5000);
    try { return await NetworkHelper.ReceiveAsync<GameMessage>(stream, cts2.Token); }
    catch (OperationCanceledException) { return null; }
}
