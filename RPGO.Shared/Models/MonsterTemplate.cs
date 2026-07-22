namespace RPGGame.Shared.Models;

public class MonsterTemplate
{
    public string Id = "";
    public string Name = "";
    public int Tier;
    public int Health;
    public int XpReward;
    public int GoldReward;
    public char Symbol = 'M';
    public int Strength = 1;
    public int Endurance = 1;
    public int Agility = 1;
    public int Cunning = 1;
    public int Intellect = 1;
    public int Wisdom = 1;
    public double CritChance = 1.0;
    public double CritDamage = 1.5;
    public double EvadeChance = 1.0;
}
