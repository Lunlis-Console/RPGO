using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(9)]
public class AddEquipmentSlots : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("equipment_slots")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name_ru").AsString().NotNullable()
            .WithColumn("is_paperdoll").AsInt32().WithDefaultValue(0)
            .WithColumn("z_order").AsInt32().WithDefaultValue(0)
            .WithColumn("accepts_two_handed").AsInt32().WithDefaultValue(0)
            .WithColumn("blocked_by_two_handed").AsInt32().WithDefaultValue(0);

        // Слоты синхронизированы с RPGGame.Shared.Models.EquipmentSlots
        Insert.IntoTable("equipment_slots").Row(new { id = "legs",       name_ru = "Ноги",            is_paperdoll = 1, z_order = 1, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "feet",       name_ru = "Обувь",           is_paperdoll = 1, z_order = 2, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "torso",      name_ru = "Торс",            is_paperdoll = 1, z_order = 3, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "head",       name_ru = "Голова",          is_paperdoll = 1, z_order = 4, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "glove_l",    name_ru = "Левая перчатка",  is_paperdoll = 1, z_order = 5, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "lhand",      name_ru = "Левая рука",      is_paperdoll = 1, z_order = 6, accepts_two_handed = 0, blocked_by_two_handed = 1 });
        Insert.IntoTable("equipment_slots").Row(new { id = "glove_r",    name_ru = "Правая перчатка", is_paperdoll = 1, z_order = 7, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "rhand",      name_ru = "Правая рука",     is_paperdoll = 1, z_order = 8, accepts_two_handed = 1, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "neck",       name_ru = "Ожерелье",        is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "ring_r",     name_ru = "Кольцо (правая рука)", is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });
        Insert.IntoTable("equipment_slots").Row(new { id = "ring_l",     name_ru = "Кольцо (левая рука)",  is_paperdoll = 0, z_order = 0, accepts_two_handed = 0, blocked_by_two_handed = 0 });

        // Признак двуручного оружия у предметов (по умолчанию — нет)
        Alter.Table("items").AddColumn("two_handed").AsInt32().WithDefaultValue(0);
    }
}
