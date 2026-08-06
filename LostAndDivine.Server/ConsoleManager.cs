namespace LostAndDivine.Server;

/// <summary>
/// Синхронизирует вывод логов с вводом команд в консоли сервера. Без этого
/// логи (поток Serilog) и echo ввода (Console.ReadLine) пишут в один stdout
/// одновременно, и символы набираемой команды «размазываются» по экрану.
/// Ввод читается вручную (Console.ReadKey с intercept), поэтому echo не мешает.
/// </summary>
public static class ConsoleManager
{
    private const string Prompt = "> ";

    private static readonly object _sync = new();
    private static string _input = "";
    private static bool _inputActive;

    public static bool InputActive
    {
        get { lock (_sync) return _inputActive; }
        set { lock (_sync) _inputActive = value; }
    }

    public static void SetInput(string line)
    {
        lock (_sync) _input = line ?? "";
    }

    /// <summary>
    /// Отрисовывает строку ввода (приглашение + текущий буфер) заново.
    /// </summary>
    public static void RenderInput()
    {
        lock (_sync)
        {
            if (!_inputActive) return;
            ClearLine();
            Console.Write(Prompt + _input);
            Console.Out.Flush();
        }
    }

    /// <summary>
    /// Пишет сообщение лога так, чтобы строка ввода не «поехала»:
    /// очищает строку ввода, выводит лог, затем перерисовывает ввод.
    /// </summary>
    public static void WriteLog(Action write)
    {
        bool interactive;
        lock (_sync)
        {
            interactive = _inputActive && IsInteractiveConsole();
            if (interactive) ClearLine();
            write();
            if (interactive)
            {
                Console.Write(Prompt + _input);
                Console.Out.Flush();
            }
        }
    }

    public static bool IsInteractiveConsole()
    {
        try { return !Console.IsOutputRedirected && !Console.IsInputRedirected; }
        catch { return false; }
    }

    private static void ClearLine()
    {
        int width = 120;
        try { if (Console.WindowWidth > 0) width = Console.WindowWidth; } catch { }
        Console.Write('\r');
        Console.Write(new string(' ', width - 1));
        Console.Write('\r');
    }
}
