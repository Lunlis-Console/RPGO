namespace LostAndDivine.Shared.Models;

public enum CharacterClass
{
    Warrior,
    Rogue,
    Necromage,
    ElementalMage,
    Ripper,
    Warlock
}

public static class CharacterClassMeta
{
    public static string DisplayName(this CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => "Воин",
        CharacterClass.Rogue => "Разбойник",
        CharacterClass.Necromage => "Некромаг",
        CharacterClass.ElementalMage => "Маг стихий",
        CharacterClass.Ripper => "Потрошитель",
        CharacterClass.Warlock => "Колдун",
        _ => "Неизвестно"
    };

    public static (int Str, int End, int Agi, int Cun, int Int, int Wis) BaseStats(this CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior => (3, 3, 1, 1, 1, 1),
        CharacterClass.Rogue => (1, 1, 3, 3, 1, 1),
        CharacterClass.Necromage => (1, 2, 1, 1, 3, 2),
        CharacterClass.ElementalMage => (1, 1, 1, 1, 3, 3),
        CharacterClass.Ripper => (3, 1, 2, 2, 1, 1),
        CharacterClass.Warlock => (1, 1, 1, 3, 2, 2),
        _ => (1, 1, 1, 1, 1, 1)
    };
}
