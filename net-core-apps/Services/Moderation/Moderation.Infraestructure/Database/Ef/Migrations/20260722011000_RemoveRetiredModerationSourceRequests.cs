using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moderation.Infra.Database.Ef;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations;

[DbContext(typeof(ModerationDbContext))]
[Migration("20260722011000_RemoveRetiredModerationSourceRequests")]
public sealed class RemoveRetiredModerationSourceRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_ModerationCaseMessages_SourceRequest",
            table: "ModerationCaseMessages");
        migrationBuilder.DropIndex(
            name: "UX_ModerationReports_SourceRequest",
            table: "ModerationReports");
        migrationBuilder.DropColumn(
            name: "SourceRequestId",
            table: "ModerationCaseMessages");
        migrationBuilder.DropColumn(
            name: "SourceRequestId",
            table: "ModerationReports");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Removed Moderation source-request state cannot be restored.");
    }
}
