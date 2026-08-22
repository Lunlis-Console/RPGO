using System.Text.RegularExpressions;

namespace LostAndDivine.Shared.Tiled;

/// <summary>
/// Точечное редактирование объектов NPC в файлах Tiled (.tmj) с сохранением исходного
/// форматирования (CRLF, по одному свойству на строку, ключи по алфавиту — формат Tiled).
/// Связь с сервером: name объекта = id записи npcs, type = npc/merchant/board/instance_portal.
/// </summary>
public static class TiledNpcWriter
{
    public const string NpcLayerName = "NPC";

    private const string ObjectTypes = "npc|merchant|board|instance_portal|dummy|storage";

    public sealed class UpsertResult
    {
        public bool Added { get; init; }
        public bool Moved { get; init; }
    }

    /// <summary>
    /// Добавляет объект NPC в слой «NPC» карты (создаёт слой, если его нет) либо перемещает
    /// существующий (по name). Позиция — клетка тайловой сетки: px = tile * tileSize.
    /// Старые объекты с тем же name (включая дубликаты) удаляются.
    /// </summary>
    public static UpsertResult Upsert(string mapFile, string npcId, string npcType, int tileX, int tileY, int tileW, int tileH)
    {
        var text = File.ReadAllText(mapFile);
        bool existed = FindObjectBlock(text, npcId) != null;
        var cleaned = RemoveAllBlocks(text, npcId);
        var result = InsertObject(cleaned, npcId, npcType, tileX, tileY, tileW, tileH);
        File.WriteAllText(mapFile, result);
        return new UpsertResult { Added = !existed, Moved = existed };
    }

    /// <summary>Удаляет объект NPC (по name) из одной карты. Вернёт true, если объект был.</summary>
    public static bool Remove(string mapFile, string npcId)
    {
        var text = File.ReadAllText(mapFile);
        var cleaned = RemoveAllBlocks(text, npcId);
        if (cleaned == text) return false;
        File.WriteAllText(mapFile, cleaned);
        return true;
    }

    /// <summary>
    /// Удаляет объект NPC из всех карт контента (zone_*.tmj и Sectors\*.tmj).
    /// Используется при перемещении NPC между картами, чтобы не оставалось дублей.
    /// </summary>
    public static int RemoveFromAllMaps(string contentDir, string npcId)
    {
        int removed = 0;
        foreach (var file in EnumerateMapFiles(contentDir))
            if (Remove(file, npcId)) removed++;
        return removed;
    }

    /// <summary>Все карты, на которых сервер размещает NPC: zone_*.tmj и секторы открытого мира.</summary>
    public static IEnumerable<string> EnumerateMapFiles(string contentDir)
    {
        if (!Directory.Exists(contentDir)) yield break;
        foreach (var f in Directory.GetFiles(contentDir, "*.tmj", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (!name.StartsWith("zone_", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("zone_main", StringComparison.OrdinalIgnoreCase)) continue;
            yield return f;
        }
        var sectors = Path.Combine(contentDir, "Sectors");
        if (Directory.Exists(sectors))
            foreach (var f in Directory.GetFiles(sectors, "*.tmj", SearchOption.TopDirectoryOnly))
                yield return f;
    }

    // ── поиск и удаление блоков ──────────────────────────────────────────────

    private static (int Start, int End)? FindObjectBlock(string text, string npcId)
    {
        var rx = new Regex("\"name\":\"" + Regex.Escape(npcId) + "\",");
        var m = rx.Match(text);
        if (!m.Success) return null;
        int start = text.LastIndexOf('{', m.Index);
        if (start < 0) return null;
        int end = MatchCloseBrace(text, start);
        if (end < 0) return null;
        // Формат-независимая проверка: это объект NPC (есть name, координаты и type),
        // а не слой/тайлсет. Так ручное редактирование в Tiled не ломает удаление.
        if (!IsNpcObjectBlock(text.Substring(start, end - start + 1), npcId)) return null;
        return (start, end);
    }

    /// <summary>Объект NPC — содержит нужный name и координаты/тип (отличает от слоёв/тайлсетов).</summary>
    private static bool IsNpcObjectBlock(string block, string npcId)
    {
        if (!Regex.IsMatch(block, "\"name\":\"" + Regex.Escape(npcId) + "\","))
            return false;
        return block.Contains("\"x\":") && block.Contains("\"y\":") && block.Contains("\"type\":");
    }

    private static string RemoveAllBlocks(string text, string npcId)
    {
        for (int guard = 0; guard < 1000; guard++)
        {
            var found = FindObjectBlock(text, npcId);
            if (found == null) return text;
            text = DeleteBlock(text, found.Value.Start, found.Value.End);
        }
        return text;
    }

    /// <summary>
    /// Удаляет блок объекта и нормализует запятые массива: объект не последний —
    /// вместе со своей запятой (перевод строки сохраняется); последний — блок целиком,
    /// а запятую предыдущего объекта убирает нормализация "},\r\n ]" → "}]".
    /// </summary>
    private static string DeleteBlock(string text, int start, int end)
    {
        int after = end + 1;
        if (after < text.Length && text[after] == ',')
        {
            // не последний: блок + своя запятая (и пробел после запятой в формате Tiled)
            int cut = after + 1;
            if (cut < text.Length && text[cut] == ' ') cut++;
            text = text.Remove(start, cut - start);
        }
        else
        {
            text = text.Remove(start, end - start + 1);
            text = Regex.Replace(text, @"\},\r?\n\s*\]", "}]");
            text = Regex.Replace(text, @"\[\r?\n\s*\]", "[]");
        }
        // схлопываем пустые строки, оставшиеся после удаления
        return Regex.Replace(text, @"\r?\n\s+\r?\n", "\r\n");
    }

    // ── вставка объекта ──────────────────────────────────────────────────────

    private static string InsertObject(string text, string npcId, string npcType, int tileX, int tileY, int tileW, int tileH)
    {
        int pxX = tileX * tileW;
        int pxY = tileY * tileH;
        int objId = GetCounter(text, "nextobjectid");
        int layerId = GetCounter(text, "nextlayerid");

        var layer = FindNpcLayer(text);
        if (layer != null)
        {
            string objBlock = BuildObjectBlock(layer.ObjectIndent, objId, npcId, npcType, pxX, pxY);
            int close = layer.ObjectsClose; // позиция ']' массива objects
            int open = text.LastIndexOf('[', close - 1);
            bool empty = open > layer.Start && string.IsNullOrWhiteSpace(text.Substring(open + 1, close - open - 1));
            if (empty)
            {
                // "objects":[], → "objects":[\r\n<block>],
                text = text.Remove(open + 1, close - open - 1)
                           .Insert(open + 1, "\r\n" + objBlock);
            }
            else
            {
                text = text.Insert(close, ",\r\n" + objBlock);
            }
            text = BumpCounter(text, "nextobjectid", objId + 1);
            return text;
        }

        // Слоя «NPC» нет — создаём новый objectgroup перед "nextlayerid".
        var m = Regex.Match(text, "\r\n(\\s*)\\}\\],\r\n(\\s*)\"nextlayerid\"");
        if (!m.Success)
            throw new InvalidOperationException("Не найден конец массива слоёв карты (\"nextlayerid\")");
        string layerIndent = m.Groups[1].Value;
        string propIndent = layerIndent + " ";
        int objIndentCount = layerIndent.Length + 8;
        // Замена "}]," на "}," + новый слой + "]," — закрывающая '}' последнего слоя сохраняется,
        // а закрытие нового слоя сразу примыкает к закрытию массива слоёв (без лишней запятой).
        string replacement =
            "\r\n" + layerIndent + "}," +
            "\r\n" + layerIndent + "{" +
            "\r\n" + propIndent + "\"draworder\":\"topdown\"," +
            "\r\n" + propIndent + "\"id\":" + layerId + "," +
            "\r\n" + propIndent + "\"name\":\"" + NpcLayerName + "\"," +
            "\r\n" + propIndent + "\"objects\":[" +
            "\r\n" + BuildObjectBlock(objIndentCount, objId, npcId, npcType, pxX, pxY) + "]," +
            "\r\n" + propIndent + "\"opacity\":1," +
            "\r\n" + propIndent + "\"type\":\"objectgroup\"," +
            "\r\n" + propIndent + "\"visible\":true," +
            "\r\n" + propIndent + "\"x\":0," +
            "\r\n" + propIndent + "\"y\":0" +
            "\r\n" + layerIndent + "}]," +
            "\r\n" + m.Groups[2].Value + "\"nextlayerid\"";

        string result = text.Substring(0, m.Index) + replacement + text.Substring(m.Index + m.Length);
        result = BumpCounter(result, "nextobjectid", objId + 1);
        result = BumpCounter(result, "nextlayerid", layerId + 1);
        return result;
    }

    private sealed class NpcLayerInfo
    {
        public int Start { get; init; }
        public int End { get; init; }
        public int ObjectsClose { get; init; }
        public int ObjectIndent { get; init; }
    }

    private static NpcLayerInfo? FindNpcLayer(string text)
    {
        var m = Regex.Match(text, "\\{\r?\\n\\s*\"draworder\":\"[^\"]*\",\r?\\n\\s*\"id\":\\d+,\r?\\n\\s*\"name\":\"" + NpcLayerName + "\",");
        if (!m.Success) return null;
        int start = m.Index;
        int end = MatchCloseBrace(text, start);
        if (end < 0) return null;

        int len = end - start + 1;
        var sub = text.Substring(start, len);
        int close = sub.LastIndexOf(']');
        if (close < 0) return null;

        var objIndent = Regex.Match(sub, "\r\n(\\s*)\\{\r?\n\\s*\"height\":\\d+,");
        int indent = objIndent.Success ? objIndent.Groups[1].Length : 16;
        return new NpcLayerInfo { Start = start, End = end, ObjectsClose = start + close, ObjectIndent = indent };
    }

    private static string BuildObjectBlock(int indent, int objId, string npcId, string npcType, int pxX, int pxY)
    {
        string pad = new string(' ', indent);
        string p = pad + " ";
        return pad + "{" +
            "\r\n" + p + "\"height\":0," +
            "\r\n" + p + "\"id\":" + objId + "," +
            "\r\n" + p + "\"name\":\"" + npcId + "\"," +
            "\r\n" + p + "\"opacity\":1," +
            "\r\n" + p + "\"point\":true," +
            "\r\n" + p + "\"rotation\":0," +
            "\r\n" + p + "\"type\":\"" + npcType + "\"," +
            "\r\n" + p + "\"visible\":true," +
            "\r\n" + p + "\"width\":0," +
            "\r\n" + p + "\"x\":" + pxX + "," +
            "\r\n" + p + "\"y\":" + pxY +
            "\r\n" + pad + "}";
    }

    /// <summary>Закрывающая скобка блока (сбалансированный счётчик, без учёта строк — в блоках карт скобок в строках нет).</summary>
    private static int MatchCloseBrace(string text, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int GetCounter(string text, string key)
    {
        var m = Regex.Match(text, "\"" + key + "\":(\\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 1;
    }

    private static string BumpCounter(string text, string key, int min)
    {
        return Regex.Replace(text, "(\"" + key + "\":)(\\d+)", m =>
        {
            int cur = int.Parse(m.Groups[2].Value);
            return m.Groups[1].Value + Math.Max(cur, min);
        });
    }
}