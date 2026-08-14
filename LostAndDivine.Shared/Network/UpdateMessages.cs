namespace LostAndDivine.Shared.Network;

/// <summary>Запись манифеста клиента: файл в client_build/files.</summary>
public class UpdateFileEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>Запрос проверки версии (клиент -> сервер).</summary>
public class UpdateCheckRequest
{
    public string Version { get; set; } = "";
}

/// <summary>Ответ сервера: версия клиента + полный манифест файлов.</summary>
public class UpdateInfo
{
    public string Version { get; set; } = "";
    public List<UpdateFileEntry> Files { get; set; } = new();
}

/// <summary>Запрос на скачивание файла (клиент -> сервер).</summary>
public class UpdateFileRequest
{
    public string Path { get; set; } = "";
}

/// <summary>Чанк файла (сервер -> клиент). Data — base64. Done на последнем чанке.</summary>
public class UpdateFileChunk
{
    public string Path { get; set; } = "";
    public long Offset { get; set; }
    public string Data { get; set; } = "";
    public long TotalLength { get; set; }
    public string Sha256 { get; set; } = "";
    public bool Done { get; set; }
}

/// <summary>Одна запись «Что нового» — версия обновления и список изменений.</summary>
public class ChangelogEntry
{
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public List<string> Items { get; set; } = new();
}

/// <summary>Сообщение changelog (сервер -> клиент после welcome). Version — текущая версия сборки.</summary>
public class ChangelogData
{
    public string Version { get; set; } = "";
    public List<ChangelogEntry> Entries { get; set; } = new();
}
