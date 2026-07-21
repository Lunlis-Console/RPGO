using FluentMigrator;

namespace RPGGame.Shared.Migrations;

[Migration(19)]
public class RenameAttributes : ForwardOnlyMigration
{
    public override void Up()
    {
        // accounts: stamina -> endurance, wisdom -> intellect, will_val -> wisdom
        // (will хранится как will_val из-за зарезервированного слова)
        Execute.Sql("ALTER TABLE accounts RENAME COLUMN stamina TO endurance");
        Execute.Sql("ALTER TABLE accounts RENAME COLUMN wisdom TO intellect");
        Execute.Sql("ALTER TABLE accounts RENAME COLUMN will_val TO wisdom");
        Execute.Sql("ALTER TABLE accounts RENAME COLUMN attack TO phys_attack");
        Execute.Sql("ALTER TABLE accounts RENAME COLUMN defense TO phys_defense");

        // items: bonus_stamina -> bonus_endurance, bonus_wisdom -> bonus_intellect, bonus_will -> bonus_wisdom
        Execute.Sql("ALTER TABLE items RENAME COLUMN bonus_stamina TO bonus_endurance");
        Execute.Sql("ALTER TABLE items RENAME COLUMN bonus_wisdom TO bonus_intellect");
        Execute.Sql("ALTER TABLE items RENAME COLUMN bonus_will TO bonus_wisdom");

        // inventory: same as items
        Execute.Sql("ALTER TABLE inventory RENAME COLUMN bonus_stamina TO bonus_endurance");
        Execute.Sql("ALTER TABLE inventory RENAME COLUMN bonus_wisdom TO bonus_intellect");
        Execute.Sql("ALTER TABLE inventory RENAME COLUMN bonus_will TO bonus_wisdom");

        // monsters: stamina -> endurance, wisdom -> intellect, will -> wisdom
        Execute.Sql("ALTER TABLE monsters RENAME COLUMN stamina TO endurance");
        Execute.Sql("ALTER TABLE monsters RENAME COLUMN wisdom TO intellect");
        Execute.Sql("ALTER TABLE monsters RENAME COLUMN will TO wisdom");
        Execute.Sql("ALTER TABLE monsters RENAME COLUMN attack TO phys_attack");
        Execute.Sql("ALTER TABLE monsters RENAME COLUMN defense TO phys_defense");

        // Добавляем колонки для новых атрибутов в items/inventory
        Execute.Sql("ALTER TABLE items ADD COLUMN bonus_phys_attack INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE items ADD COLUMN bonus_mag_attack INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE items ADD COLUMN bonus_defense INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE items ADD COLUMN bonus_resistance INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE items ADD COLUMN bonus_attack_speed REAL NOT NULL DEFAULT 0.0");

        Execute.Sql("ALTER TABLE inventory ADD COLUMN bonus_phys_attack INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN bonus_mag_attack INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN bonus_defense INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN bonus_resistance INTEGER NOT NULL DEFAULT 0");
        Execute.Sql("ALTER TABLE inventory ADD COLUMN bonus_attack_speed REAL NOT NULL DEFAULT 0.0");
    }
}
