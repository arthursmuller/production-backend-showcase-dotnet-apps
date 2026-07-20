using System;
using Interaction.Infraestructure.Database.Ef;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations;

[DbContext(typeof(InteractionDbContext))]
[Migration("20260722010000_RemoveRetiredCommentImportState")]
public sealed class RemoveRetiredCommentImportState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "InteractionIntentCursors");

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(
            "Removed Interaction import state cannot be restored.");
    }
}
