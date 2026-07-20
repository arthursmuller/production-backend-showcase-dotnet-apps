using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictedDecisionAuthorityState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModerationDecisionStates",
                columns: table => new
                {
                    ExportCursor = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CaseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceKind = table.Column<int>(type: "int", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<int>(type: "int", nullable: false),
                    DecisionVersion = table.Column<long>(type: "bigint", nullable: false),
                    CaseVersion = table.Column<long>(type: "bigint", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationDecisionStates", x => x.ExportCursor);
                    table.CheckConstraint("CK_ModerationDecisionState_Action", "`Action` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationDecisionState_ResourceKind", "`ResourceKind` BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_ModerationDecisionState_Versions", "`DecisionVersion` >= 1 AND `CaseVersion` >= `DecisionVersion`");
                    table.ForeignKey(
                        name: "FK_ModerationDecisionStates_ModerationCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ModerationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "UX_ModerationDecisionState_Case",
                table: "ModerationDecisionStates",
                column: "CaseId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO `ModerationDecisionStates`
                    (`CaseId`, `ResourceKind`, `ResourceId`, `Action`,
                     `DecisionVersion`, `CaseVersion`, `DecidedAtUtc`)
                SELECT `Id`, `ResourceKind`, `ResourceId`, `DecisionAction`,
                       `DecisionVersion`, `Version`, `DecidedAtUtc`
                FROM `ModerationCases`
                WHERE `DecisionVersion` > 0
                  AND `DecisionAction` IS NOT NULL
                  AND `DecidedAtUtc` IS NOT NULL
                ORDER BY `ReceivedAtUtc`, `Id`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModerationDecisionStates");
        }
    }
}
