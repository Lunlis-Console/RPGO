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
}
