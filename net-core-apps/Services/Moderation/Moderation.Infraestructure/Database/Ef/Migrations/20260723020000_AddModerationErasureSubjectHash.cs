using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moderation.Infra.Database.Ef;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations;

[DbContext(typeof(ModerationDbContext))]
[Migration("20260723020000_AddModerationErasureSubjectHash")]
public sealed class AddModerationErasureSubjectHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ErasureSubjectHash",
            table: "ModerationCases",
            type: "char(64)",
            unicode: false,
            nullable: true,
            collation: "ascii_bin");

        migrationBuilder.AddCheckConstraint(
            name: "CK_ModerationCases_ErasureSubjectHash",
            table: "ModerationCases",
            sql: "`ErasureSubjectHash` IS NULL OR `ErasureSubjectHash` REGEXP '^[0-9a-f]{64}$'");

        migrationBuilder.CreateIndex(
            name: "IX_ModerationCases_ErasureSubjectHash",
            table: "ModerationCases",
            columns: new[] { "ErasureSubjectHash", "ErasureClaimToken", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ModerationCases_ErasureSubjectHash",
            table: "ModerationCases");

        migrationBuilder.DropCheckConstraint(
            name: "CK_ModerationCases_ErasureSubjectHash",
            table: "ModerationCases");

        migrationBuilder.DropColumn(
            name: "ErasureSubjectHash",
            table: "ModerationCases");
    }
}
