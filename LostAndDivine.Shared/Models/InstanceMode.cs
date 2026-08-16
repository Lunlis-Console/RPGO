namespace LostAndDivine.Shared.Models;

/// <summary>Режим инстанса: соло (меньше мобов, слабее босс, обычный дроп)
/// или групповой (больше мобов, сильнее босс, выше шанс лучшей экипировки).</summary>
public enum InstanceMode
{
    Solo,
    Group
}