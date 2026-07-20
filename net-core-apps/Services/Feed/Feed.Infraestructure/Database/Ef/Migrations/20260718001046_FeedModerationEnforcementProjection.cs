using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class FeedModerationEnforcementProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedModerationDecisionGap",
                columns: table => new
                {
                    CaseId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"),
                    ResourceKind = table.Column<string>(
                        type: "varchar(16)",
                        maxLength: 16,
                        nullable: false,
                        collation: "ascii_bin"),
                    ResourceId = table.Column<string>(
                        type: "varchar(128)",
                        maxLength: 128,
                        nullable: false,
                        collation: "ascii_bin"),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(
                        type: "varchar(32)",
                        maxLength: 32,
                        nullable: false,
                        collation: "ascii_bin"),
                    SourceOccurredAtUtc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_FeedModerationDecisionGap",
                        value => new
                        {
                            value.CaseId,
                            value.ResourceKind,
                            value.ResourceId,
                            value.DecisionVersion,
                        });
                    table.CheckConstraint(
                        "CK_FeedModerationDecisionGap_Version_Positive",
                        "`DecisionVersion` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedModerationDecisionState",
                columns: table => new
                {
                    CaseId = table.Column<Guid>(
                        type: "char(36)",
                        nullable: false,
                        collation: "ascii_general_ci"),
                    ResourceKind = table.Column<string>(
                        type: "varchar(16)",
                        maxLength: 16,
                        nullable: false,
                        collation: "ascii_bin"),
                    ResourceId = table.Column<string>(
                        type: "varchar(128)",
                        maxLength: 128,
                        nullable: false,
                        collation: "ascii_bin"),
                    LastAppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestObservedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(
                        type: "varchar(32)",
                        maxLength: 32,
                        nullable: false,
                        collation: "ascii_bin"),
                    Status = table.Column<string>(
                        type: "varchar(16)",
                        maxLength: 16,
                        nullable: false,
                        collation: "ascii_bin"),
                    IsSuppressed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NeedsReconciliation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastSourceOccurredAtUtc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(
                        type: "datetime(6)",
                        nullable: false),
                    Version = table.Column<long>(
                        type: "bigint",
                        nullable: false,
                        defaultValue: 1L),
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_FeedModerationDecisionState",
                        value => new
                        {
                            value.CaseId,
                            value.ResourceKind,
                            value.ResourceId,
                        });
                    table.CheckConstraint(
                        "CK_FeedModerationDecisionState_Applied_NonNegative",
                        "`LastAppliedVersion` >= 0");
                    table.CheckConstraint(
                        "CK_FeedModerationDecisionState_Highest_Positive",
                        "`HighestObservedVersion` > 0");
                    table.CheckConstraint(
                        "CK_FeedModerationDecisionState_Version_Positive",
                        "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FeedModerationDecisionState_Resource_Suppressed",
                table: "FeedModerationDecisionState",
                columns: new[] { "ResourceKind", "ResourceId", "IsSuppressed" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedModerationDecisionState_Status_Updated_Case",
                table: "FeedModerationDecisionState",
                columns: new[] { "Status", "UpdatedAtUtc", "CaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FeedModerationDecisionGap");
            migrationBuilder.DropTable(name: "FeedModerationDecisionState");
        }
    }
}
