using System.Text.Json.Serialization;

namespace RPGGame.Shared.Models;

public class GameMessage
{
    [JsonPropertyName("t")]
    public string Type { get; set; } = "";

    [JsonPropertyName("d")]
    public object? Data { get; set; }

    /// <summary>Сброс боевого состояния (выход из боя).</summary>
    public static GameMessage ResetCombat() => new()
    {
        Type = "combat_state",
        Data = new { InCombat = false, TargetId = (string?)null, TargetName = (string?)null, TargetHp = 0, TargetMaxHp = 0 }
    };

    /// <summary>Обновление HP монстра.</summary>
    public static GameMessage CombatUpdate(string name, int health, int maxHealth) => new()
    {
        Type = "combat_update",
        Data = new { MonsterName = name, MonsterHealth = health, MonsterMaxHealth = maxHealth }
    };

    /// <summary>Сообщение в чат.</summary>
    public static GameMessage Chat(string name, string text) => new()
    {
        Type = "chat",
        Data = new { Name = name, Text = text }
    };

    /// <summary>Системное сообщение в чат.</summary>
    public static GameMessage SystemChat(string text) => Chat("Система", text);

    /// <summary>Урон (монстр→игрок или игрок→монстр).</summary>
    public static GameMessage Damage(string target, string? monsterId, int x, int y, int amount, bool isCrit, string? playerName = null) => new()
    {
        Type = "damage",
        Data = new { Target = target, PlayerName = playerName, MonsterId = monsterId, X = x, Y = y, Amount = amount, IsCrit = isCrit }
    };

    /// <summary>Обновление дебаффов цели (монстра) для HUD.</summary>
    public static GameMessage TargetDebuffUpdate(object debuffs) => new()
    {
        Type = "target_debuff_update",
        Data = new { Debuffs = debuffs }
    };
}