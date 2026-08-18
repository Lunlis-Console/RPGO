using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

/// <summary>
/// Пассивные ветки теперь не зависят от первого активного навыка: корневые
/// пассивы (SK0003 «Амбидекстр», SK0017 «Вам подарочек») можно изучать сразу,
/// не тратя лишнее очко на первый активный навык пути.
/// </summary>
[Migration(1056)]
public class FreePassiveSkillRoots : ForwardOnlyMigration
{
    public override void Up()
    {
        Update.Table("skills").Set(new { parent_id = (string?)null }).Where(new { id = "SK0003" });
        Update.Table("skills").Set(new { parent_id = (string?)null }).Where(new { id = "SK0017" });
    }
}