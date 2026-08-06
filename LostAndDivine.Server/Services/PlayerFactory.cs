using LostAndDivine.Shared.Models;

namespace LostAndDivine.Server.Services;

public static class PlayerFactory
{
    public static Player FromCharacter(CharacterModel ch, GameServices svc)
    {
        string savedZone = ch.CurrentZoneId;

        bool zoneGone = svc.Zones.GetZone(savedZone) == null
                        && !savedZone.Equals(Balance.MainZoneId, StringComparison.OrdinalIgnoreCase);
        string zoneId = zoneGone ? Balance.MainZoneId : savedZone;

        int spawnX, spawnY;
        if (!zoneGone && ch.X >= 0 && ch.Y >= 0)
        {
            spawnX = ch.X;
            spawnY = ch.Y;
        }
        else
        {
            var zone = svc.Zones.GetZone(zoneId);
            int baseX = zone?.SpawnX ?? svc.Merchant.MerchantX;
            int baseY = zone?.SpawnY ?? svc.Merchant.MerchantY;
            int mapW = zone?.Width ?? svc.World.Map.Width;
            int mapH = zone?.Height ?? svc.World.Map.Height;
            spawnX = baseX + svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
            spawnY = baseY + svc.World.NextRandom(Balance.RespawnJitterMin, Balance.RespawnJitterMax);
            spawnX = Math.Clamp(spawnX, 0, mapW - 1);
            spawnY = Math.Clamp(spawnY, 0, mapH - 1);
        }

        var player = new Player
        {
            Name = ch.Name,
            X = spawnX,
            Y = spawnY,
            Level = ch.Level,
            Experience = ch.Experience,
            Health = ch.Health,
            MaxHealth = ch.MaxHealth,
            Gold = ch.Gold,
            Strength = ch.Strength,
            Endurance = ch.Endurance,
            Agility = ch.Agility,
            Cunning = ch.Cunning,
            Intellect = ch.Intellect,
            Wisdom = ch.Wisdom,
            AttributePoints = ch.AttributePoints,
            Speed = ch.Speed,
            Inventory = ch.Inventory,
            Equipment = ch.Equipment,
            ActiveQuests = ch.ActiveQuests,
            CompletedQuestIds = ch.CompletedQuestIds,
            HotbarSlots = ch.HotbarSlots,
            SkillPoints = ch.SkillPoints,
            LearnedSkills = ch.LearnedSkills,
            SkillRanks = ch.SkillRanks,
            Mana = ch.Mana,
            MaxMana = Balance.MaxMana(ch.Wisdom),
            CurrentZoneId = zoneId
        };

        if (player.Name.Equals("test", StringComparison.OrdinalIgnoreCase)
            || player.Name.Equals("тест", StringComparison.OrdinalIgnoreCase))
            player.Speed = 50;

        return player;
    }
}
