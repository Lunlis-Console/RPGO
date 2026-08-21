using LostAndDivine.Server.Services;
using LostAndDivine.Shared.Models;

namespace LostAndDivine.Tests;

public class ItemRollerTests
{
    private static Item Template(ItemQuality quality, int requiredLevel, ItemRollConfig config) => new()
    {
        TemplateId = "sword_t1",
        Name = "Меч",
        TypeEnum = ItemType.Weapon,
        RequiredLevel = requiredLevel,
        DamageMin = 10,
        DamageMax = 15,
        BonusStrength = 99,
        BonusEndurance = 99,
        Quality = quality,
        RollConfig = config
    };

    private static ItemRollConfig Config(
        int weightUncommon = 0,
        int weightRare = 0,
        int weightEpic = 0,
        int countMin = 1,
        int countMax = 1,
        int? weightCommon = null,
        params (string Stat, double Min, double Max)[] stats) => new()
    {
        Enabled = true,
        WeightCommon = weightCommon,
        WeightUncommon = weightUncommon,
        WeightRare = weightRare,
        WeightEpic = weightEpic,
        Uncommon = Tier(countMin, countMax, stats),
        Rare = Tier(countMin, countMax, stats),
        Epic = Tier(countMin, countMax, stats)
    };

    private static RollTierConfig Tier(int countMin, int countMax, (string Stat, double Min, double Max)[] stats) => new()
    {
        CountMin = countMin,
        CountMax = countMax,
        Stats = stats.Select(s => new RollStatConfig { Stat = s.Stat, Min = s.Min, Max = s.Max }).ToList()
    };

    [Fact]
    public void Roll_NoConfig_ReturnsCloneWithoutBonuses()
    {
        var tpl = Template(ItemQuality.Common, 10, null!);
        var result = ItemRoller.Roll(tpl, new Random(42));

        Assert.NotNull(result);
        Assert.NotSame(tpl, result);
        Assert.Equal(99, result!.BonusStrength); // без ролла статичные бонусы сохраняются
        Assert.Equal(ItemQuality.Common, result.Quality);
    }

    [Fact]
    public void Roll_DisabledConfig_KeepsStaticBonuses()
    {
        var tpl = Template(ItemQuality.Epic, 10, new ItemRollConfig { Enabled = false });
        var result = ItemRoller.Roll(tpl, new Random(42));

        Assert.NotNull(result);
        Assert.Equal(99, result!.BonusStrength);
        Assert.Equal(ItemQuality.Epic, result.Quality);
    }

    [Fact]
    public void Roll_ZeroWeights_DropsCommonAndKeepsBase()
    {
        // Легаси: вес Обычного не задан → Обычный = остаток до 100, предмет выпадает всегда
        var tpl = Template(ItemQuality.Common, 10, Config(0, 0, 0, 1, 1, null, ("Strength", 2, 2)));
        var result = ItemRoller.Roll(tpl, new Random(42));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Common, result!.Quality);
        Assert.Equal(99, result.BonusStrength); // Обычный — предмет как в шаблоне (база)
        Assert.Equal(99, result.BonusEndurance);
    }

    [Fact]
    public void Roll_OnlyRareWeight_AlwaysDropsRareWithBonuses()
    {
        // Абсолютные веса: 0/0/100 → всегда Редкий, шанс дропа 100%
        var tpl = Template(ItemQuality.Common, 5, Config(0, 100, 0, 1, 1, 0, ("Strength", 2, 2)));
        var result = ItemRoller.Roll(tpl, new Random(1));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Rare, result!.Quality);
        Assert.Equal(10, result.BonusStrength); // 2 * уровень 5
        Assert.Equal(0, result.BonusEndurance); // статичные бонусы стёрты
    }

    [Fact]
    public void Roll_OnlyEpicWeight_AlwaysDropsEpic()
    {
        var tpl = Template(ItemQuality.Common, 1, Config(0, 0, 100, 1, 1, 0, ("Strength", 3, 3)));
        var result = ItemRoller.Roll(tpl, new Random(1));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Epic, result!.Quality);
        Assert.Equal(3, result.BonusStrength);
    }

    [Fact]
    public void Roll_WeightedMix_NeverDropsNonConfiguredQuality()
    {
        // Абсолютные веса 50/30/10/7 (сумма 97) → только перечисленные качества или ничего
        var tpl = Template(ItemQuality.Common, 1, Config(30, 10, 7, 1, 1, 50, ("Strength", 1, 1)));

        for (int seed = 0; seed < 200; seed++)
        {
            var result = ItemRoller.Roll(tpl, new Random(seed));
            if (result == null) continue; // остаток до 100% — ничего не выпадает
            Assert.Contains(result.Quality, new[]
            {
                ItemQuality.Common, ItemQuality.Uncommon, ItemQuality.Rare, ItemQuality.Epic
            });
        }
    }

    [Fact]
    public void Roll_AbsoluteWeights_CanDropNothing()
    {
        // Сумма весов 47 (0/30/10/7) → шанс дропа 47%, 53% — ничего
        var tpl = Template(ItemQuality.Common, 1, Config(30, 10, 7, 1, 1, 0, ("Strength", 1, 1)));

        bool sawItem = false, sawNothing = false;
        for (int seed = 0; seed < 500; seed++)
        {
            var result = ItemRoller.Roll(tpl, new Random(seed));
            if (result == null) sawNothing = true;
            else sawItem = true;
        }

        Assert.True(sawItem);
        Assert.True(sawNothing);
    }

    [Fact]
    public void Roll_ZeroTotalWeights_AlwaysNothing()
    {
        var tpl = Template(ItemQuality.Common, 1, Config(0, 0, 0, 1, 1, 0, ("Strength", 1, 1)));

        for (int seed = 0; seed < 100; seed++)
            Assert.Null(ItemRoller.Roll(tpl, new Random(seed)));
    }

    [Fact]
    public void RollForQuality_NoConfig_ReturnsCloneUnchanged()
    {
        var tpl = Template(ItemQuality.Epic, 10, null!);
        var result = ItemRoller.RollForQuality(tpl, ItemQuality.Common, new Random(42));

        Assert.Equal(ItemQuality.Epic, result.Quality); // качество шаблона сохраняется
        Assert.Equal(99, result.BonusStrength);
    }

    [Fact]
    public void RollForQuality_Common_KeepsBase()
    {
        var tpl = Template(ItemQuality.Common, 5, Config(30, 15, 5, 1, 1, null, ("Strength", 2, 2)));
        var result = ItemRoller.RollForQuality(tpl, ItemQuality.Common, new Random(1));

        Assert.Equal(ItemQuality.Common, result.Quality);
        Assert.Equal(99, result.BonusStrength); // Обычный — предмет как в шаблоне (база)
    }

    [Fact]
    public void RollForQuality_Epic_AppliesTierAndSetsQuality()
    {
        var tpl = Template(ItemQuality.Common, 5, Config(30, 15, 5, 1, 1, null, ("Strength", 2, 2)));
        var result = ItemRoller.RollForQuality(tpl, ItemQuality.Epic, new Random(1));

        Assert.Equal(ItemQuality.Epic, result.Quality);
        Assert.Equal(10, result.BonusStrength);
    }

    [Fact]
    public void Roll_CountZero_NoBonuses()
    {
        var tpl = Template(ItemQuality.Common, 5, Config(0, 0, 100, 0, 0, 0, ("Strength", 2, 4)));
        var result = ItemRoller.Roll(tpl, new Random(1));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Epic, result!.Quality);
        Assert.Equal(0, result.BonusStrength);
    }

    [Fact]
    public void Roll_RandomSelection_UsesOnlyConfiguredStats()
    {
        var tpl = Template(ItemQuality.Common, 3, Config(0, 0, 100, 1, 3, 0,
            ("Strength", 1, 5), ("Agility", 1, 5), ("CritChance", 1, 2)));

        var rng = new Random(7);
        var result = ItemRoller.Roll(tpl, rng);

        Assert.NotNull(result);
        Assert.True(result!.BonusStrength > 0 || result.BonusAgility > 0 || result.BonusCritChance > 0);
        Assert.Equal(0, result.BonusEndurance);
        Assert.Equal(0, result.BonusIntellect);
    }

    [Fact]
    public void Roll_DoesNotMutateTemplate()
    {
        var tpl = Template(ItemQuality.Common, 10, Config(0, 0, 100, 2, 3, 0,
            ("Strength", 1, 5), ("Endurance", 1, 5), ("Agility", 1, 5)));
        var beforeStr = tpl.BonusStrength;
        var beforeEnd = tpl.BonusEndurance;

        ItemRoller.Roll(tpl, new Random(123));
        ItemRoller.Roll(tpl, new Random(124));

        Assert.Equal(beforeStr, tpl.BonusStrength);
        Assert.Equal(beforeEnd, tpl.BonusEndurance);
        Assert.Equal(ItemQuality.Common, tpl.Quality);
    }

    [Fact]
    public void Roll_ScaleLevelOverridesRequiredLevel()
    {
        var tpl = Template(ItemQuality.Common, 1, Config(0, 0, 100, 1, 1, 0, ("Strength", 3, 3)));
        var result = ItemRoller.Roll(tpl, new Random(5), scaleLevel: 10);

        Assert.NotNull(result);
        Assert.Equal(30, result!.BonusStrength);
    }

    [Fact]
    public void Roll_DecimalPerLevel_RoundsToInt()
    {
        var tpl = Template(ItemQuality.Common, 5, Config(0, 0, 100, 1, 1, 0, ("Strength", 1.5, 1.5)));
        var result = ItemRoller.Roll(tpl, new Random(1));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Epic, result!.Quality);
        Assert.Equal(8, result.BonusStrength); // 1.5 × 5 = 7.5 → 8 (округление от нуля)
    }

    [Fact]
    public void Roll_PercentStat_RoundsToOneDecimal()
    {
        var tpl = Template(ItemQuality.Common, 3, Config(0, 0, 100, 1, 1, 0, ("CritChance", 1.35, 1.35)));
        var result = ItemRoller.Roll(tpl, new Random(1));

        Assert.NotNull(result);
        Assert.Equal(ItemQuality.Epic, result!.Quality);
        Assert.Equal(4.1, result.BonusCritChance); // 1.35 × 3 = 4.05 → 4.1 (1 знак, от нуля)
    }

    [Fact]
    public void ConfigJson_RoundTrips()
    {
        var cfg = Config(30, 15, 5, 2, 3, 50,
            ("Strength", 1, 5), ("CritChance", 0.5, 1.5));

        var json = System.Text.Json.JsonSerializer.Serialize(cfg, ItemRollConfig.JsonOpts);
        var back = System.Text.Json.JsonSerializer.Deserialize<ItemRollConfig>(json, ItemRollConfig.JsonOpts);

        Assert.NotNull(back);
        Assert.True(back!.Enabled);
        Assert.Equal(50, back.WeightCommon);
        Assert.Equal(30, back.WeightUncommon);
        Assert.Equal(15, back.WeightRare);
        Assert.Equal(5, back.WeightEpic);
        Assert.Equal(2, back.Epic.CountMin);
        Assert.Equal(3, back.Epic.CountMax);
        Assert.Equal(2, back.Epic.Stats.Count);
        Assert.Equal("Strength", back.Epic.Stats[0].Stat);
        Assert.Equal(1.5, back.Epic.Stats[1].Max);
    }

    [Fact]
    public void ConfigJson_LegacyWithoutWeightCommon_RoundTripsAsNull()
    {
        var cfg = Config(30, 15, 5, 1, 1, null, ("Strength", 1, 1));

        var json = System.Text.Json.JsonSerializer.Serialize(cfg, ItemRollConfig.JsonOpts);
        var back = System.Text.Json.JsonSerializer.Deserialize<ItemRollConfig>(json, ItemRollConfig.JsonOpts);

        Assert.NotNull(back);
        Assert.Null(back!.WeightCommon); // легаси: Обычный = остаток до 100
    }
}

