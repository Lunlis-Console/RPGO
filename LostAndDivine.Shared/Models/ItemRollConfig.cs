using System.Text.Json;
using System.Text.Json.Serialization;

namespace LostAndDivine.Shared.Models;

/// <summary>
/// Конфигурация случайных бонусов предмета (items.roll_config).
/// Хранится в шаблоне; бонусы сворачиваются в момент дропа (см. ItemRoller).
/// </summary>
public class ItemRollConfig
{
    /// <summary>Включён ли ролл бонусов для этого шаблона.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Вес качества «Обычный» — абсолютный шанс (процент) при дропе.
    /// null (легаси) — обычный = остаток до 100% от Необ/Ред/Эпик (всегда что-то выпадает).
    /// Сумма всех четырёх весов — шанс дропа предмета; остаток до 100% — ничего не выпадает.
    /// Пример: 30/10/7/3 → 30% обычный, 10% необычный, 7% редкий, 3% эпический, 50% ничего.
    /// </summary>
    public int? WeightCommon { get; set; }

    /// <summary>Вес качества «Необычный» (абсолютный шанс, %).</summary>
    public int WeightUncommon { get; set; }

    /// <summary>Вес качества «Редкий» (абсолютный шанс, %).</summary>
    public int WeightRare { get; set; }

    /// <summary>Вес качества «Эпический» (абсолютный шанс, %).</summary>
    public int WeightEpic { get; set; }

    /// <summary>Настройки ролла для качества «Необычный».</summary>
    public RollTierConfig Uncommon { get; set; } = new();

    /// <summary>Настройки ролла для качества «Редкий».</summary>
    public RollTierConfig Rare { get; set; } = new();

    /// <summary>Настройки ролла для качества «Эпический».</summary>
    public RollTierConfig Epic { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>Настройки ролла бонусов для одного качества.</summary>
public class RollTierConfig
{
    /// <summary>Минимальное количество бонусов.</summary>
    public int CountMin { get; set; }

    /// <summary>Максимальное количество бонусов.</summary>
    public int CountMax { get; set; }

    /// <summary>Параметры, участвующие в ролле, и их диапазоны значений.</summary>
    public List<RollStatConfig> Stats { get; set; } = new();
}

/// <summary>Один параметр в пуле ролла: ключ стата и диапазон значения (за уровень предмета).</summary>
public class RollStatConfig
{
    /// <summary>Ключ стата из <see cref="RollStatCatalog"/> (напр. "Strength", "CritChance").</summary>
    public string Stat { get; set; } = "";

    /// <summary>Минимальное значение бонуса за уровень предмета.</summary>
    public double Min { get; set; }

    /// <summary>Максимальное значение бонуса за уровень предмета.</summary>
    public double Max { get; set; }
}

/// <summary>
/// Каталог статов, доступных для случайных бонусов: единый источник ключей
/// для редактора (UI) и сервера (применение ролла).
/// </summary>
public static class RollStatCatalog
{
    public static readonly (string Key, string Label)[] All =
    {
        ("Strength", "Сила"),
        ("Endurance", "Выносливость"),
        ("Agility", "Ловкость"),
        ("Cunning", "Хитрость"),
        ("Intellect", "Интеллект"),
        ("Wisdom", "Мудрость"),
        ("MaxHealth", "Макс. HP"),
        ("MaxMana", "Макс. MP"),
        ("PhysAttack", "+Физ. атака"),
        ("MagAttack", "+Маг. атака"),
        ("Defense", "+Физ. защита"),
        ("Resistance", "+Маг. защита"),
        ("CritChance", "+Крит %"),
        ("CritDamage", "+Крит урон %"),
        ("EvadeChance", "+Уклонение %"),
        ("AttackSpeed", "+Скор. атаки %"),
        ("BlockChance", "+Блок %"),
        ("ParryChance", "+Парирование %"),
        ("Accuracy", "+Точность %"),
        ("Tenacity", "+Стойкость %"),
        ("ArmorPenetration", "+Пробивание %"),
        ("CooldownReduction", "+Снижение отката %"),
        ("HpRegen", "+Реген ХП %"),
        ("MpRegen", "+Реген МП %"),
    };

    /// <summary>Является ли стат процентным (хранится в double, а не int).</summary>
    public static bool IsPercentStat(string key) => key is
        "CritChance" or "CritDamage" or "EvadeChance" or "AttackSpeed" or
        "BlockChance" or "ParryChance" or "Accuracy" or "Tenacity" or
        "ArmorPenetration" or "CooldownReduction" or "HpRegen" or "MpRegen";
}
