using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractionAuthorityExportCoordinate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractionAuthorityCoordinates",
                columns: table => new
                {
                    Cursor = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ResourceType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionAuthorityCoordinates", x => x.Cursor);
                    table.ForeignKey(
                        name: "FK_InteractionAuthorityCoordinates_ResourceStates_ResourceType_~",
                        columns: x => new { x.ResourceType, x.ResourceId },
                        principalTable: "ResourceStates",
                        principalColumns: new[] { "ResourceType", "ResourceId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "UX_InteractionAuthorityCoordinate_Resource",
                table: "InteractionAuthorityCoordinates",
                columns: new[] { "ResourceType", "ResourceId" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO `InteractionAuthorityCoordinates`
                    (`ResourceType`, `ResourceId`, `CreatedAtUtc`)
                SELECT `ResourceType`, `ResourceId`, `UpdatedAtUtc`
                FROM `ResourceStates`
                ORDER BY `ResourceType`, `ResourceId`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractionAuthorityCoordinates");
        }
    }
}
