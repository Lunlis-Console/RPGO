using FluentMigrator;

namespace RPGGame.Shared.Migrations;

/// <summary>
/// Приводим предметы к новым слотам экипировки:
///  - старые типы конвертируем в новые (accessory→ring, armor→chest; weapon оставляем);
///  - старые слоты в player_equipment (weapon/armor/accessory) переименовываем в новые id;
///  - добавляем образцы предметов для слотов, у которых раньше не было вещей
///    (helmet, cloak, legs, boots, gloves, shield, necklace, twohand), чтобы все 12 слотов были рабочими.
/// </summary>
[Migration(12)]
public class ConvertItemsToNewSlots : ForwardOnlyMigration
{
    public override void Up()
    {
        // 1) Конвертация типов существующих предметов
        Execute.Sql("UPDATE items SET type='ring'  WHERE type='accessory'");
        Execute.Sql("UPDATE items SET type='chest' WHERE type='armor'");

        // 2) Переименование старых слотов экипировки у игроков (на случай, если есть данные)
        Execute.Sql("UPDATE player_equipment SET slot='rhand'  WHERE slot='weapon'");
        Execute.Sql("UPDATE player_equipment SET slot='torso'  WHERE slot='armor'");
        Execute.Sql("UPDATE player_equipment SET slot='ring_r' WHERE slot='accessory'");

        // 3) Образцы предметов для новых слотов (id I02xx — не пересекаются с I00xx)
        const string cols = @"id, name, type, value, attack, defense, max_health_bonus, heal_amount, stock, description,
            bonus_strength, bonus_stamina, bonus_agility, bonus_cunning, bonus_wisdom, bonus_will,
            bonus_crit_chance, bonus_crit_damage, bonus_evade_chance, two_handed";

        void Ins(string sql) => Execute.Sql($"INSERT INTO items ({cols}) {sql}");

        // --- Шлем (head) ---
        Ins("SELECT 'I0201','Железный шлем','helmet',15,0,2,5,0,1,'Простой защитный шлем.',0,0,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0202','Стальной шлем','helmet',40,0,5,12,0,1,'Крепкий шлем из стали.',0,1,0,0,0,0,0,0,0,0");

        // --- Плащ (cloak) ---
        Ins("SELECT 'I0203','Поношенный плащ','cloak',10,0,1,0,0,1,'Лёгкий плащ, чуть укрывает от ударов.',0,0,0,0,0,0,0,0,2,0");
        Ins("SELECT 'I0204','Шёлковый плащ','cloak',35,0,2,0,0,1,'Тонкий плащ, ускользает от врагов.',0,0,1,0,1,0,0,0,5,0");

        // --- Поножи (legs) ---
        Ins("SELECT 'I0205','Железные поножи','legs',15,0,2,5,0,1,'Защита для ног.',0,0,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0206','Стальные поножи','legs',45,0,5,15,0,1,'Тяжёлые поножи.',0,2,0,0,0,0,0,0,0,0");

        // --- Сапоги (feet) ---
        Ins("SELECT 'I0207','Кожаные сапоги','boots',10,0,1,0,0,1,'Удобная обувь.',0,0,1,0,0,0,0,0,3,0");
        Ins("SELECT 'I0208','Стальные сапоги','boots',35,0,3,0,0,1,'Прочные сапоги.',0,0,2,0,0,0,0,0,6,0");

        // --- Перчатки (gloves — надеваются сразу на обе руки) ---
        Ins("SELECT 'I0209','Кожаные перчатки','gloves',10,0,1,0,0,1,'Лёгкие перчатки.',0,0,1,0,0,0,0,0,0,0");
        Ins("SELECT 'I0210','Стальные перчатки','gloves',30,1,2,0,0,1,'Боевые перчатки.',0,0,2,0,0,0,0,0,0,0");

        // --- Щит (lhand) ---
        Ins("SELECT 'I0211','Деревянный щит','shield',15,0,3,0,0,1,'Простой щит.',0,0,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0212','Стальной щит','shield',40,0,7,0,0,1,'Надёжный щит.',0,1,0,0,0,0,0,0,0,0");

        // --- Ожерелье (neck) ---
        Ins("SELECT 'I0213','Серебряное ожерелье','necklace',25,1,1,5,0,1,'Украшение с магией.',0,0,0,0,0,0,0,0,0,0");
        Ins("SELECT 'I0214','Ожерелье мудреца','necklace',60,0,0,10,0,1,'Хранит знание.',0,0,0,0,2,3,0,0,0,0");

        // --- Двуручное оружие (rhand, two_handed) ---
        Ins("SELECT 'I0215','Двуручный топор','twohand',50,9,0,0,0,1,'Тяжёлый топор, требует обеих рук.',2,0,0,0,0,0,0,0,0,1");
        Ins("SELECT 'I0216','Двуручный меч','twohand',70,14,0,0,0,1,'Огромный клинок.',3,0,1,0,0,0,0,0,0,1");
    }
}
