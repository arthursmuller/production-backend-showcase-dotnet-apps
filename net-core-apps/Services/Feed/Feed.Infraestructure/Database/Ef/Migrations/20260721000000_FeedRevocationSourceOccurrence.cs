using System;
using Feed.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations;

[DbContext(typeof(FeedContext))]
[Migration("20260721000000_FeedRevocationSourceOccurrence")]
public partial class FeedRevocationSourceOccurrence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Expand first so the previous application version can continue writing rows
        // while the migration is deployed.
        migrationBuilder.AddColumn<DateTime>(
            name: "SourceOccurredAtUtc",
            table: "FeedRevocationWorkItem",
            type: "datetime(6)",
            nullable: true);

        // Historical rows cannot recover their original source timestamp. CreatedAtUtc
        // is used only for this one-time compatibility backfill; all new writes carry the
        // authoritative source occurrence supplied by the integration contract.
        migrationBuilder.Sql(
            """
            UPDATE `FeedRevocationWorkItem`
            SET `SourceOccurredAtUtc` = `CreatedAtUtc`
            WHERE `SourceOccurredAtUtc` IS NULL;
            """);

        // Contract only after every legacy row has a value.
        migrationBuilder.AlterColumn<DateTime>(
            name: "SourceOccurredAtUtc",
            table: "FeedRevocationWorkItem",
            type: "datetime(6)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime(6)",
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(
            name: "SourceOccurredAtUtc",
            table: "FeedRevocationWorkItem");
}
