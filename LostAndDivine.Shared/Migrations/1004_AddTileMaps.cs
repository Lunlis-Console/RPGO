using FluentMigrator;

namespace LostAndDivine.Shared.Migrations;

[Migration(1004)]
public class AddTileMaps : ForwardOnlyMigration
{
    public override void Up()
    {
        Create.Table("tile_maps")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("zone_id").AsString().NotNullable()
            .WithColumn("width").AsInt32().NotNullable()
            .WithColumn("height").AsInt32().NotNullable()
            .WithColumn("tile_size").AsInt32().WithDefaultValue(32)
            .WithColumn("tileset_id").AsString().WithDefaultValue("")
            .WithColumn("tiles").AsBinary().NotNullable();

        Create.Table("tilesets")
            .WithColumn("id").AsString().NotNullable().PrimaryKey()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("file_name").AsString().NotNullable()
            .WithColumn("tile_width").AsInt32().NotNullable()
            .WithColumn("tile_height").AsInt32().NotNullable()
            .WithColumn("columns").AsInt32().NotNullable()
            .WithColumn("rows").AsInt32().NotNullable();
    }
}