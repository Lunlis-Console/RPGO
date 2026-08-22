using System.Text.Json.Serialization;
using LostAndDivine.Shared.Network;

namespace LostAndDivine.Shared.Models;

public class GameMessage
{
    [JsonConverter(typeof(GameMessageTypeJsonConverter))]
    [JsonPropertyName("t")]
    public GameMessageType Type { get; set; } = GameMessageType.Unknown;

    [JsonPropertyName("d")]
    public object? Data { get; set; }

    /// <summary>Сброс боевого состояния (выход из боя).</summary>
    public static GameMessage ResetCombat() => new()
    {
        Type = GameMessageType.CombatState,
        Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
    };

    /// <summary>Обновление HP монстра.</summary>
    public static GameMessage CombatUpdate(string name, int health, int maxHealth) => new()
    {
        Type = GameMessageType.CombatUpdate,
        Data = new { MonsterName = name, MonsterHealth = health, MonsterMaxHealth = maxHealth }
    };

    /// <summary>Сообщение в чат.</summary>
    public static GameMessage Chat(string name, string text) => new()
    {
        Type = GameMessageType.Chat,
        Data = new { Name = name, Text = text }
    };

    /// <summary>Системное сообщение в чат.</summary>
    public static GameMessage SystemChat(string text) => Chat("Система", text);

    /// <summary>Урон (монстр>игрок или игрок>монстр).</summary>
    public static GameMessage Damage(string target, string? monsterId, int x, int y, int amount, bool isCrit, string? playerName = null, string? result = null) => new()
    {
        Type = GameMessageType.Damage,
        Data = new { Target = target, PlayerName = playerName, MonsterId = monsterId, X = x, Y = y, Amount = amount, IsCrit = isCrit, Result = result }
    };

    /// <summary>Обновление дебаффов цели (монстра) для HUD.</summary>
    public static GameMessage TargetDebuffUpdate(object debuffs) => new()
    {
        Type = GameMessageType.TargetDebuffUpdate,
        Data = new { Debuffs = debuffs }
    };

    /// <summary>Уведомление клиенту о смерти игрока (death screen + задержка).</summary>
    public static GameMessage PlayerDeath(int lostGold) => new()
    {
        Type = GameMessageType.PlayerDeath,
        Data = new { LostGold = lostGold }
    };

    /// <summary>Сообщение об ошибке.</summary>
    public static GameMessage Error(string code, string message) => new()
    {
        Type = GameMessageType.Error,
        Data = new { Code = code, Message = message }
    };

    /// <summary>Боевое состояние (вход/выход из боя).</summary>
    public static GameMessage CombatState(bool inCombat, string? targetId = null, string? targetName = null,
        int targetHp = 0, int targetMaxHp = 0, int targetX = 0, int targetY = 0, bool isPvP = false,
        object? targetDebuffs = null) => new()
    {
        Type = GameMessageType.CombatState,
        Data = new
        {
            InCombat = inCombat,
            TargetId = targetId,
            TargetName = targetName,
            TargetHp = targetHp,
            TargetMaxHp = targetMaxHp,
            TargetX = targetX,
            TargetY = targetY,
            IsPvP = isPvP,
            TargetDebuffs = targetDebuffs
        }
    };
}