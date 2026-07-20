using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationDecisionRevisionRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAtUtc",
                table: "ModerationDecisionStateRevisions",
                type: "datetime(6)",
                precision: 6,
                nullable: false,
                defaultValueSql: "UTC_TIMESTAMP(6)");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationDecisionRevision_Retention",
                table: "ModerationDecisionStateRevisions",
                columns: new[] { "RecordedAtUtc", "RevisionCursor" });

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStateRevisions_RejectDelete`;");
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationDecisionStateRevisions_RejectDelete`
                BEFORE DELETE ON `ModerationDecisionStateRevisions`
                FOR EACH ROW
                BEGIN
                    IF COALESCE(@moderation_decision_revision_prune, 0) <> 1 THEN
                        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Moderation decision revisions are append-only';
                    END IF;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStateRevisions_RejectDelete`;");
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationDecisionStateRevisions_RejectDelete`
                BEFORE DELETE ON `ModerationDecisionStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Moderation decision revisions are append-only';
                """);

            migrationBuilder.DropIndex(
                name: "IX_ModerationDecisionRevision_Retention",
                table: "ModerationDecisionStateRevisions");

            migrationBuilder.DropColumn(
                name: "RecordedAtUtc",
                table: "ModerationDecisionStateRevisions");
        }
    }
}
