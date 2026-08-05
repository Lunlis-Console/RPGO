using RPGGame.Shared.Models;

namespace RPGGame.Server.Services;

/// <summary>
/// Строит игрока из сохранённых данных аккаунта. Общая логика для входа (login)
/// и переподключения после перезапуска сервера.
/// </summary>
public static class PlayerFactory
{
    public static Player FromAccount(Account account, GameServices svc)
    {
        string savedZone = account.PlayerData.CurrentZoneId;

        // После перезапуска инстансы не существуют — возвращаем на главную карту.
        bool zoneGone = svc.Zones.GetZone(savedZone) == null
                        && !savedZone.Equals(Balance.MainZoneId, StringComparison.OrdinalIgnoreCase);
        string zoneId = zoneGone ? Balance.MainZoneId : savedZone;

        int spawnX, spawnY;
        if (!zoneGone && account.PlayerData.X >= 0 && account.PlayerData.Y >= 0)
        {
            spawnX = account.PlayerData.X;
            spawnY = account.PlayerData.Y;
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
            Name = account.PlayerName,
            X = spawnX,
            Y = spawnY,
            Level = account.PlayerData.Level,
            Experience = account.PlayerData.Experience,
            Health = account.PlayerData.Health,
            MaxHealth = account.PlayerData.MaxHealth,
            Gold = account.PlayerData.Gold,
            Strength = account.PlayerData.Strength,
            Endurance = account.PlayerData.Endurance,
            Agility = account.PlayerData.Agility,
            Cunning = account.PlayerData.Cunning,
            Intellect = account.PlayerData.Intellect,
            Wisdom = account.PlayerData.Wisdom,
            AttributePoints = account.PlayerData.AttributePoints,
            Speed = account.PlayerData.Speed,
            Inventory = account.PlayerData.Inventory,
            Equipment = account.PlayerData.Equipment,
            ActiveQuests = account.PlayerData.ActiveQuests,
            CompletedQuestIds = account.PlayerData.CompletedQuestIds,
            HotbarSlots = account.PlayerData.HotbarSlots,
            SkillPoints = account.PlayerData.SkillPoints,
            LearnedSkills = account.PlayerData.LearnedSkills,
            SkillRanks = account.PlayerData.SkillRanks,
            Mana = account.PlayerData.Mana,
            MaxMana = Balance.MaxMana(account.PlayerData.Wisdom),
            IsAdmin = account.IsAdmin,
            CurrentZoneId = zoneId
        };
        // Mana загружается из сохранённых данных (не восстанавливается при входе)

        if (player.Name.Equals("test", StringComparison.OrdinalIgnoreCase)
            || player.Name.Equals("тест", StringComparison.OrdinalIgnoreCase))
            player.Speed = 50;

        return player;
    }
}
