namespace LostAndDivine.Server;

/// <summary>
/// Разрешение пути к папке контента (.tmj-карты, секторы, тайлсеты).
/// В разработке используется ЖИВАЯ исходная папка LostAndDivine.ClientMonoGame\Content:
/// правки карт в Tiled видны через /reloadmap без пересборки сервера.
/// В проде (где исходной папки нет) — копия Content рядом с исполняемым файлом.
/// </summary>
public static class ContentPaths
{
    private static string? _contentDir;

    public static string ContentDir => _contentDir ??= Resolve();

    public static string SectorsDir => Path.Combine(ContentDir, "Sectors");

    private static string Resolve()
    {
        string baseDir = AppContext.BaseDirectory;
        var src = FindSourceContentDir(baseDir);
        if (src != null) return src;
        return Path.Combine(baseDir, "Content");
    }

    /// <summary>Ищет папку LostAndDivine.ClientMonoGame\Content, поднимаясь вверх от базовой папки.</summary>
    private static string? FindSourceContentDir(string startDir)
    {
        var dir = Path.GetFullPath(startDir);
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "LostAndDivine.ClientMonoGame", "Content");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Path.GetDirectoryName(dir);
            if (parent == null) break;
            dir = parent;
        }
        return null;
    }
}