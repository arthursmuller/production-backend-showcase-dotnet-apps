using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Following.Infra.Database.Ef.Migrations;

[DbContext(typeof(FollowingContext))]
[Migration("20260722000000_DropRetiredAnalyticsTable")]
public sealed class DropRetiredAnalyticsTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS `UserRelationAnalyticsLegacy`;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException(
            "The retired analytics table cannot be restored after its data is deleted.");
}
