using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Объединяем перчатки в один слот "gloves", заменяем "glove_r" на "belt" (пояс).
/// Удаляем старые предметы glove_r/glove_l, создаём новые glove и belt.
/// </summary>
[Migration(26)]
public class UnifyGlovesAddBelt : ForwardOnlyMigration
{
    public override void Up()
    {
        // 1) Переименовываем слот glove_l → gloves (единый слот перчаток)
        Execute.Sql("UPDATE equipment_slots SET id='gloves', name_ru='Перчатки' WHERE id='glove_l'");

        // 2) Переименовываем слот glove_r → belt (пояс)
        Execute.Sql("UPDATE equipment_slots SET id='belt', name_ru='Пояс' WHERE id='glove_r'");

        // 3) Конвертируем экипировку игроков
        Execute.Sql("UPDATE player_equipment SET slot='gloves' WHERE slot='glove_l'");
        Execute.Sql("UPDATE player_equipment SET slot='belt' WHERE slot='glove_r'");

        // 4) Удаляем старые предметы перчаток
        Execute.Sql("DELETE FROM items WHERE type IN ('glove_r', 'glove_l', 'gloves')");

        // 5) Создаём новые предметы
        const string cols = @"id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description,
            bonus_strength, bonus_endurance, bonus_agility, bonus_cunning, bonus_intellect, bonus_wisdom,
            bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed";

        void Ins(string sql) => Execute.Sql($"INSERT INTO items ({cols}) {sql}");

        // --- Перчатки (glove) ---
        Ins("SELECT 'I0601','Кожаные перчатки','glove',10,0,1,0,0,1,'Лёгкие перчатки.',0,0,1,0,0,0,0,0,0,0");
        Ins("SELECT 'I0602','Стальные перчатки','glove',30,1,2,0,0,1,'Боевые перчатки.',0,0,2,0,0,0,0,0,0,0");
        Ins("SELECT 'I0603','Кольчужные перчатки','glove',50,0,4,10,0,1,'Прочные кольчужные перчатки.',0,0,0,0,0,0,0,0,0,0");

        // --- Пояса (belt) ---
        Ins("SELECT 'I0604','Кожаный пояс','belt',8,0,0,0,0,1,'Простой пояс.',0,0,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0605','Ремень воина','belt',25,0,1,5,0,1,'Крепкий ремень с пряжкой.',0,1,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0606','Пояс силы','belt',60,0,2,10,0,1,'Магический пояс, усиливает хватку.',2,0,0,0,0,0,0,0,0,0");
    }
}
