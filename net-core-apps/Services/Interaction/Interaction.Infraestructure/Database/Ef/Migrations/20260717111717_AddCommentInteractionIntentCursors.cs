using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentInteractionIntentCursors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Rating_Score",
                table: "Ratings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EngagementSummary_RatingTotal",
                table: "EngagementSummaries");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Ratings",
                type: "varchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InteractionIntentCursors",
                columns: table => new
                {
                    ResourceType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AppliedSourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastIntentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionIntentCursors", x => new { x.ResourceType, x.ResourceId, x.ActorId, x.Channel });
                    table.CheckConstraint("CK_InteractionIntentCursor_Channel", "`Channel` IN (1,2)");
                    table.CheckConstraint("CK_InteractionIntentCursor_Version", "`AppliedSourceVersion` > 0");
                    table.ForeignKey(
                        name: "FK_InteractionIntentCursors_ResourceStates_ResourceType_Resourc~",
                        columns: x => new { x.ResourceType, x.ResourceId },
                        principalTable: "ResourceStates",
                        principalColumns: new[] { "ResourceType", "ResourceId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rating_Score",
                table: "Ratings",
                sql: "`Score` BETWEEN 1 AND 6");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EngagementSummary_RatingTotal",
                table: "EngagementSummaries",
                sql: "`RatingTotal` <= `RatingCount` * 6");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionIntentCursor_Updated",
                table: "InteractionIntentCursors",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractionIntentCursors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rating_Score",
                table: "Ratings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EngagementSummary_RatingTotal",
                table: "EngagementSummaries");

            migrationBuilder.AlterColumn<string>(
                name: "Comment",
                table: "Ratings",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rating_Score",
                table: "Ratings",
                sql: "`Score` BETWEEN 1 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EngagementSummary_RatingTotal",
                table: "EngagementSummaries",
                sql: "`RatingTotal` <= `RatingCount` * 5");
        }
    }
}
