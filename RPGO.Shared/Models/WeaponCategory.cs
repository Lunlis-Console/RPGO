namespace RPGGame.Shared.Models;

public enum WeaponCategory
{
    None = 0,
    Sword,
    Axe,
    Mace,
    Hammer,
    Dagger,
    Greatsword,
    Greataxe,
    Halberd,
    Spear,
    Greathammer,
    Bow,
    Staff,
    Grimoire,
    Sphere,
    Shield
}

public enum ItemClass
{
    Melee = 0,
    Ranged,
    Magic
}

public static class WeaponCategoryExtensions
{
    public static WeaponCategory Parse(string? subtype)
    {
        if (string.IsNullOrWhiteSpace(subtype))
            return WeaponCategory.None;

        return subtype.Trim().ToLowerInvariant() switch
        {
            "sword" => WeaponCategory.Sword,
            "axe" => WeaponCategory.Axe,
            "mace" => WeaponCategory.Mace,
            "hammer" => WeaponCategory.Hammer,
            "dagger" => WeaponCategory.Dagger,
            "greatsword" => WeaponCategory.Greatsword,
            "greataxe" => WeaponCategory.Greataxe,
            "halberd" => WeaponCategory.Halberd,
            "spear" => WeaponCategory.Spear,
            "greathammer" => WeaponCategory.Greathammer,
            "bow" => WeaponCategory.Bow,
            "staff" => WeaponCategory.Staff,
            "grimoire" => WeaponCategory.Grimoire,
            "sphere" => WeaponCategory.Sphere,
            "shield" => WeaponCategory.Shield,
            _ => WeaponCategory.None
        };
    }

    public static ItemClass GetItemClass(this WeaponCategory category) => category switch
    {
        WeaponCategory.Bow => ItemClass.Ranged,
        WeaponCategory.Staff or WeaponCategory.Grimoire or WeaponCategory.Sphere => ItemClass.Magic,
        _ => ItemClass.Melee
    };
}
