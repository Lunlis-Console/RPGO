using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(3)]
public class LootTableSchema : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("loot_tables")
            .WithColumn("id").AsInt32().Identity().PrimaryKey()
            .WithColumn("monster_id").AsString().NotNullable()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("description").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("value").AsInt32().WithDefaultValue(1)
            .WithColumn("drop_chance").AsInt32().WithDefaultValue(30);

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0001", name = "Крысиный хвост", description = "Сухой обрубок хвоста. Кто-то коллекционирует такие.", value = 3, drop_chance = 50 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0001", name = "Крысиные клыки", description = "Маленькие, но острые. Годятся как поделка.", value = 5, drop_chance = 25 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0002", name = "Паучья лапа", description = "Покрыта мелкими щетинками. Вызывает мурашки.", value = 6, drop_chance = 40 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0002", name = "Паучий яд", description = "Маленький флакон с ядом. Осторожно!", value = 10, drop_chance = 20 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0003", name = "Гнилая плоть", description = "Кусок мертвечины. Пахнет ужасно.", value = 4, drop_chance = 45 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0003", name = "Костяная булава", description = "Ржавая, но ещё может размозжить череп.", value = 12, drop_chance = 30 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0004", name = "Гоблинский нож", description = "Кривой нож, вырезанный из железа.", value = 8, drop_chance = 40 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0004", name = "Гоблинское ухо", description = "Трофей охотника. Не для слабонервных.", value = 6, drop_chance = 25 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0005", name = "Кость скелета", description = "Прочная кость. Пригодится алхимику.", value = 7, drop_chance = 45 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0005", name = "Череп скелета", description = "Пустые глазницы смотрят в душу.", value = 10, drop_chance = 30 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0006", name = "Волчий клык", description = "Острый и крепкий. Из него делают подвески.", value = 15, drop_chance = 35 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0006", name = "Волчья шкура", description = "Густая и тёплая. Ценный мех.", value = 18, drop_chance = 15 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0007", name = "Медвежий коготь", description = "Массивный коготь. Украшение для воина.", value = 25, drop_chance = 30 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0007", name = "Медвежья шкура", description = "Толстая шкура, выдержит удар меча.", value = 30, drop_chance = 12 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0008", name = "Орочий клык", description = "Жёлтый и потрескавшийся. Не самый приятный трофей.", value = 20, drop_chance = 35 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0008", name = "Орочий браслет", description = "Грубый железный браслет. Красиво смотрится.", value = 25, drop_chance = 15 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0009", name = "Тёмный артефакт", description = "Мерцающий камень. Шепчет что-то непонятное.", value = 40, drop_chance = 25 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0009", name = "Магическая пыль", description = "Светящиеся частицы. Используются в ритуалах.", value = 35, drop_chance = 18 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0010", name = "Чешуя дракончика", description = "Маленькая, но прочная. Блестит на свету.", value = 50, drop_chance = 20 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0010", name = "Коготь дракончика", description = "Острый как бритва. Трогать в перчатках.", value = 55, drop_chance = 10 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0011", name = "Драконья чешуя", description = "Большая и невероятно прочная. Легендарный материал.", value = 100, drop_chance = 15 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0011", name = "Драконье сердце", description = "Ещё тёплое. Источник древней силы.", value = 150, drop_chance = 5 });

        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0012", name = "Артефакт Лича", description = "Посох с мёртвой душой на конце.", value = 120, drop_chance = 10 });
        Insert.IntoTable("loot_tables").Row(new { monster_id = "M0012", name = "Кристалл души", description = "Заключённая в камень душа. Мерцает холодным светом.", value = 130, drop_chance = 8 });
    }
}
