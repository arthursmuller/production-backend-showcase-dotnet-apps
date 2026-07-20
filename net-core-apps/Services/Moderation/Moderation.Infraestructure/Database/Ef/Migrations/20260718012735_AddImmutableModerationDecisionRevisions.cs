using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableModerationDecisionRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationDecisionStateRevisions",
                columns: table => new
                {
                    RevisionCursor = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceKind = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    CaseVersion = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false),
                    PartitionKey = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RowSha256 = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "ascii_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationDecisionStateRevisions", x => x.RevisionCursor);
                    table.CheckConstraint("CK_ModerationDecisionRevision_Action", "`Action` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationDecisionRevision_Hash", "CHAR_LENGTH(`RowSha256`) = 64");
                    table.CheckConstraint("CK_ModerationDecisionRevision_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationDecisionRevision_Versions", "`DecisionVersion` >= 1 AND `CaseVersion` >= `DecisionVersion`");
                    table.ForeignKey(
                        name: "FK_ModerationDecisionStateRevisions_ModerationCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ModerationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationDecisionRevision_Case_Cursor",
                table: "ModerationDecisionStateRevisions",
                columns: new[] { "CaseId", "RevisionCursor" });

            migrationBuilder.CreateIndex(
                name: "IX_ModerationDecisionRevision_Partition_Cursor",
                table: "ModerationDecisionStateRevisions",
                columns: new[] { "PartitionKey", "RevisionCursor" });

            migrationBuilder.Sql(AppendRevision("`ModerationDecisionStates` AS `s`", "s"));
            migrationBuilder.Sql(RevisionTrigger(
                "TR_ModerationDecisionStates_AuthorityRevision_Insert",
                "INSERT"));
            migrationBuilder.Sql(RevisionTrigger(
                "TR_ModerationDecisionStates_AuthorityRevision_Update",
                "UPDATE"));
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationDecisionStateRevisions_RejectUpdate`
                BEFORE UPDATE ON `ModerationDecisionStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation decision authority revisions are immutable';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationDecisionStateRevisions_RejectDelete`
                BEFORE DELETE ON `ModerationDecisionStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Moderation decision authority revisions are immutable';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStateRevisions_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStateRevisions_RejectUpdate`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStates_AuthorityRevision_Update`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStates_AuthorityRevision_Insert`;");
            migrationBuilder.DropTable(
                name: "ModerationDecisionStateRevisions");
        }

        private static string RevisionTrigger(string name, string operation) =>
            $"""
            CREATE TRIGGER `{name}`
            AFTER {operation} ON `ModerationDecisionStates`
            FOR EACH ROW
            {AppendRevision("(SELECT 1 AS `Seed`) AS `seed`", "NEW")}
            """;

        private static string AppendRevision(string from, string state) =>
            $"""
            INSERT INTO `ModerationDecisionStateRevisions`
                (`CaseId`, `ResourceKind`, `ResourceId`, `Action`, `DecisionVersion`,
                 `CaseVersion`, `DecidedAtUtc`, `PartitionKey`, `RowSha256`)
            SELECT {state}.`CaseId`, {state}.`ResourceKind`, {state}.`ResourceId`, {state}.`Action`,
                   {state}.`DecisionVersion`, {state}.`CaseVersion`, {state}.`DecidedAtUtc`,
                   CAST(CONV(SUBSTRING(SHA2(LOWER(CAST({state}.`CaseId` AS CHAR)), 256), 1, 2), 16, 10) AS UNSIGNED),
                   LOWER(SHA2(CONCAT_WS(CHAR(31),
                       'moderation-decision-authority-revision',
                       LOWER(CAST({state}.`CaseId` AS CHAR)), CAST({state}.`ResourceKind` AS CHAR),
                       CONCAT(CHAR_LENGTH({state}.`ResourceId`), ':', {state}.`ResourceId`),
                       CAST({state}.`Action` AS CHAR), CAST({state}.`DecisionVersion` AS CHAR),
                       CAST({state}.`CaseVersion` AS CHAR),
                       DATE_FORMAT({state}.`DecidedAtUtc`, '%Y-%m-%dT%H:%i:%s.%fZ')), 256))
            FROM {from};
            """;
    }
}
