using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddInteractionAuthorityRevisionRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAtUtc",
                table: "InteractionAuthoritativeStateRevisions",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "UTC_TIMESTAMP(6)");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionAuthorityRevision_Retention",
                table: "InteractionAuthoritativeStateRevisions",
                columns: new[] { "RecordedAtUtc", "RevisionCursor" });

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_InteractionAuthorityRevisions_RejectDelete`;");
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_InteractionAuthorityRevisions_RejectDelete`
                BEFORE DELETE ON `InteractionAuthoritativeStateRevisions`
                FOR EACH ROW
                BEGIN
                    IF COALESCE(@interaction_authority_revision_prune, 0) <> 1 THEN
                        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Interaction authority revisions are append-only';
                    END IF;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_InteractionAuthorityRevisions_RejectDelete`;");
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_InteractionAuthorityRevisions_RejectDelete`
                BEFORE DELETE ON `InteractionAuthoritativeStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Interaction authority revisions are append-only';
                """);

            migrationBuilder.DropIndex(
                name: "IX_InteractionAuthorityRevision_Retention",
                table: "InteractionAuthoritativeStateRevisions");

            migrationBuilder.DropColumn(
                name: "RecordedAtUtc",
                table: "InteractionAuthoritativeStateRevisions");
        }
    }
}
