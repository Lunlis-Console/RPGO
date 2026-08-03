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

public static class WeaponCategoryExtensions
{
    public static WeaponCategory Parse(string subtype)
    {
        if (string.IsNullOrWhiteSpace(subtype))
            return WeaponCategory.None;

        return subtype switch
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
}
