using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalHoldSafeModerationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RetentionClaimToken",
                table: "ModerationCases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionRedactedAtUtc",
                table: "ModerationCases",
                type: "datetime(6)",
                precision: 6,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModerationRetentionCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    LastStartedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    LastCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    TotalCasesRedacted = table.Column<long>(type: "bigint", nullable: false),
                    TotalCasesPurged = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationRetentionCheckpoints", x => x.Id);
                    table.CheckConstraint("CK_ModerationRetentionCheckpoints_FencingToken", "`FencingToken` >= 0");
                    table.CheckConstraint("CK_ModerationRetentionCheckpoints_Singleton", "`Id` = 1");
                    table.CheckConstraint("CK_ModerationRetentionCheckpoints_Totals", "`TotalCasesRedacted` >= 0 AND `TotalCasesPurged` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_Retention",
                table: "ModerationCases",
                columns: new[] { "Status", "RetentionRedactedAtUtc", "LastChangedAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModerationCases_RetentionClaim",
                table: "ModerationCases",
                sql: "`RetentionClaimToken` >= 0");

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectDelete`;");
            migrationBuilder.Sql(GuardedDeleteTrigger(
                "TR_ModerationAuditEntries_RejectDelete",
                "ModerationAuditEntries",
                "Moderation audit entries are append-only"));
            migrationBuilder.Sql(GuardedDeleteTrigger(
                "TR_ModerationCaseMessages_RejectDelete",
                "ModerationCaseMessages",
                "Moderation case messages are append-only"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectDelete`;");
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationAuditEntries_RejectDelete`
                BEFORE DELETE ON `ModerationAuditEntries`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation audit entries are append-only';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationCaseMessages_RejectDelete`
                BEFORE DELETE ON `ModerationCaseMessages`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation case messages are append-only';
                """);

            migrationBuilder.DropTable(
                name: "ModerationRetentionCheckpoints");

            migrationBuilder.DropIndex(
                name: "IX_ModerationCases_Retention",
                table: "ModerationCases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModerationCases_RetentionClaim",
                table: "ModerationCases");

            migrationBuilder.DropColumn(
                name: "RetentionClaimToken",
                table: "ModerationCases");

            migrationBuilder.DropColumn(
                name: "RetentionRedactedAtUtc",
                table: "ModerationCases");
        }

        private static string GuardedDeleteTrigger(
            string triggerName,
            string tableName,
            string rejectionMessage) =>
            $"""
            CREATE TRIGGER `{triggerName}`
            BEFORE DELETE ON `{tableName}`
            FOR EACH ROW
            BEGIN
                IF COALESCE(@moderation_retention_fencing_token, 0) = 0
                    OR NOT EXISTS (
                        SELECT 1
                        FROM `ModerationRetentionCheckpoints`
                        WHERE `Id` = 1
                          AND `FencingToken` = @moderation_retention_fencing_token
                          AND `LeaseOwner` IS NOT NULL
                          AND `LeaseUntilUtc` >= UTC_TIMESTAMP(6)
                    ) THEN
                    SIGNAL SQLSTATE '45000'
                        SET MESSAGE_TEXT = '{rejectionMessage}';
                END IF;
            END
            """;
    }
}
