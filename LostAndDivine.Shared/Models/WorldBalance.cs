namespace LostAndDivine.Shared.Models;

/// <summary>
/// Мировой баланс: зоны, секторы, размеры мира.
/// Выделено из BalanceStatic для SRP.
/// </summary>
public static class WorldBalance
{
    public const string MainZoneId = "main";
    public const string StartZoneId = "airship_basement";

    public const int SectorSize = 100;
    public const int SectorCols = 30;
    public const int SectorRows = 17;
    public const int WorldWidth = SectorSize * SectorCols;   // 3000
    public const int WorldHeight = SectorSize * SectorRows;  // 1700

    public const int EntrySectorCol = 3;
    public const int EntrySectorRow = 7;
    public const int EntrySectorOffsetX = EntrySectorCol * SectorSize; // 300
    public const int EntrySectorOffsetY = EntrySectorRow * SectorSize; // 700

    public const int MinDamage = 1;
    public const int ChanceRollMax = 100;
}
