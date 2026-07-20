using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableModerationUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ErasureClaimToken",
                table: "ModerationCases",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ErasureSubjectUserId",
                table: "ModerationCases",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ModerationUserErasures",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ErasureVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    LeaseOwner = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    LastProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: true),
                    HeldCaseCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationUserErasures", x => x.UserId);
                    table.CheckConstraint("CK_ModerationUserErasures_HeldCases", "`HeldCaseCount` >= 0");
                    table.CheckConstraint("CK_ModerationUserErasures_Lease", "(`LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL) OR (`LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL)");
                    table.CheckConstraint("CK_ModerationUserErasures_Stage", "`Stage` IN (1,2)");
                    table.CheckConstraint("CK_ModerationUserErasures_User", "`UserId` > 0");
                    table.CheckConstraint("CK_ModerationUserErasures_Version", "`ErasureVersion` > 0 AND `FencingToken` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_ErasureClaim",
                table: "ModerationCases",
                columns: new[] { "ErasureSubjectUserId", "ErasureClaimToken", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationCases_ReporterErasure",
                table: "ModerationCases",
                columns: new[] { "ReporterId", "ReceivedAtUtc", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ModerationCases_ErasureClaim",
                table: "ModerationCases",
                sql: "`ErasureClaimToken` >= 0 AND (`ErasureSubjectUserId` IS NULL OR `ErasureSubjectUserId` > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationUserErasures_Work",
                table: "ModerationUserErasures",
                columns: new[] { "Stage", "NextAttemptAtUtc", "LeaseUntilUtc", "LastProcessedAtUtc", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_ModerationUserErasures_Request",
                table: "ModerationUserErasures",
                column: "RequestId",
                unique: true);

            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectDelete`;");
            migrationBuilder.Sql(CombinedGuardedDeleteTrigger(
                "TR_ModerationAuditEntries_RejectDelete",
                "ModerationAuditEntries",
                "Moderation audit entries are append-only"));
            migrationBuilder.Sql(CombinedGuardedDeleteTrigger(
                "TR_ModerationCaseMessages_RejectDelete",
                "ModerationCaseMessages",
                "Moderation case messages are append-only"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationAuditEntries_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationCaseMessages_RejectDelete`;");
            migrationBuilder.Sql(RetentionGuardedDeleteTrigger(
                "TR_ModerationAuditEntries_RejectDelete",
                "ModerationAuditEntries",
                "Moderation audit entries are append-only"));
            migrationBuilder.Sql(RetentionGuardedDeleteTrigger(
                "TR_ModerationCaseMessages_RejectDelete",
                "ModerationCaseMessages",
                "Moderation case messages are append-only"));

            migrationBuilder.DropTable(
                name: "ModerationUserErasures");

            migrationBuilder.DropIndex(
                name: "IX_ModerationCases_ErasureClaim",
                table: "ModerationCases");

            migrationBuilder.DropIndex(
                name: "IX_ModerationCases_ReporterErasure",
                table: "ModerationCases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ModerationCases_ErasureClaim",
                table: "ModerationCases");

            migrationBuilder.DropColumn(
                name: "ErasureClaimToken",
                table: "ModerationCases");

            migrationBuilder.DropColumn(
                name: "ErasureSubjectUserId",
                table: "ModerationCases");
        }

        private static string CombinedGuardedDeleteTrigger(
            string triggerName,
            string tableName,
            string rejectionMessage) =>
            $"""
            CREATE TRIGGER `{triggerName}`
            BEFORE DELETE ON `{tableName}`
            FOR EACH ROW
            BEGIN
                IF (
                    COALESCE(@moderation_retention_fencing_token, 0) = 0
                    OR NOT EXISTS (
                        SELECT 1
                        FROM `ModerationRetentionCheckpoints`
                        WHERE `Id` = 1
                          AND `FencingToken` = @moderation_retention_fencing_token
                          AND `LeaseOwner` IS NOT NULL
                          AND `LeaseUntilUtc` >= UTC_TIMESTAMP(6)
                    )
                ) AND (
                    COALESCE(@moderation_erasure_user_id, 0) = 0
                    OR COALESCE(@moderation_erasure_fencing_token, 0) = 0
                    OR NOT EXISTS (
                        SELECT 1
                        FROM `ModerationUserErasures`
                        WHERE `UserId` = @moderation_erasure_user_id
                          AND `FencingToken` = @moderation_erasure_fencing_token
                          AND `LeaseOwner` IS NOT NULL
                          AND `LeaseUntilUtc` >= UTC_TIMESTAMP(6)
                    )
                ) THEN
                    SIGNAL SQLSTATE '45000'
                        SET MESSAGE_TEXT = '{rejectionMessage}';
                END IF;
            END
            """;

        private static string RetentionGuardedDeleteTrigger(
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
