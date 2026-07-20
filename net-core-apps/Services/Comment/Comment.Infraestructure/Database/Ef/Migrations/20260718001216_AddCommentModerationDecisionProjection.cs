using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentModerationDecisionProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentModerationDecisionProjection",
                columns: table => new
                {
                    CaseId = table.Column<System.Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestSeenVersion = table.Column<long>(type: "bigint", nullable: false),
                    HasGap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSuppressed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DecisionOccurredAtUtc = table.Column<System.DateTimeOffset>(type: "datetime(6)", nullable: true),
                    GapDetectedAtUtc = table.Column<System.DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<System.DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentModerationDecisionProjection", x => new { x.CaseId, x.CommentId });
                    table.CheckConstraint("CK_CmtModDecision_Action", "`Action` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_CmtModDecision_Gap", "(`HasGap` = 1 AND `IsSuppressed` = 1 AND `GapDetectedAtUtc` IS NOT NULL) OR (`HasGap` = 0 AND `GapDetectedAtUtc` IS NULL)");
                    table.CheckConstraint("CK_CmtModDecision_Suppression", "`IsSuppressed` = (`HasGap` = 1 OR `Action` <> 1)");
                    table.CheckConstraint("CK_CmtModDecision_Versions", "`AppliedVersion` >= 0 AND `HighestSeenVersion` >= `AppliedVersion`");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentModerationDecisionGap",
                columns: table => new
                {
                    CaseId = table.Column<System.Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    OccurredAtUtc = table.Column<System.DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ObservedAtUtc = table.Column<System.DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentModerationDecisionGap", x => new { x.CaseId, x.CommentId, x.DecisionVersion });
                    table.CheckConstraint("CK_CmtModGap_Action", "`Action` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_CmtModGap_Version", "`DecisionVersion` > 0");
                    table.ForeignKey(
                        name: "FK_CommentModerationDecisionGap_CommentModerationDecisionProjection_CaseId_CommentId",
                        columns: x => new { x.CaseId, x.CommentId },
                        principalTable: "CommentModerationDecisionProjection",
                        principalColumns: new[] { "CaseId", "CommentId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CmtModGap_Drain",
                table: "CommentModerationDecisionGap",
                columns: new[] { "CaseId", "CommentId", "DecisionVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtModDecision_Comment_Suppressed",
                table: "CommentModerationDecisionProjection",
                columns: new[] { "CommentId", "IsSuppressed" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtModDecision_Reconciliation",
                table: "CommentModerationDecisionProjection",
                columns: new[] { "HasGap", "GapDetectedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CommentModerationDecisionGap");
            migrationBuilder.DropTable(name: "CommentModerationDecisionProjection");
        }
    }
}
