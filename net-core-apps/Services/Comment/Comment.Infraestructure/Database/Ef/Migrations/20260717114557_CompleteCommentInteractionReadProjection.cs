using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCommentInteractionReadProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ContributionVersion",
                table: "CommentSocialRating",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "CommentInteractionActorProjections",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    Reaction = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    Rating = table.Column<byte>(type: "tinyint unsigned", nullable: true),
                    ReactionVersion = table.Column<long>(type: "bigint", nullable: false),
                    RatingVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionActorProjections", x => new { x.CommentId, x.ActorId });
                    table.CheckConstraint("CK_CommentInteractionActorProjection_Rating", "`Rating` IS NULL OR `Rating` BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_CommentInteractionActorProjection_Reaction", "`Reaction` IS NULL OR `Reaction` IN (1,2)");
                    table.CheckConstraint("CK_CommentInteractionActorProjection_Versions", "`ReactionVersion` >= 0 AND `RatingVersion` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentInteractionSummaryGaps",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    SummaryVersion = table.Column<long>(type: "bigint", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    DislikeCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingTotal = table.Column<long>(type: "bigint", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false),
                    ShareCount = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionSummaryGaps", x => new { x.CommentId, x.SummaryVersion });
                    table.ForeignKey(
                        name: "FK_CommentInteractionSummaryGaps_CommentInteractionSummaryProje~",
                        column: x => x.CommentId,
                        principalTable: "CommentInteractionSummaryProjections",
                        principalColumn: "CommentId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentInteractionActor_Actor_Updated",
                table: "CommentInteractionActorProjections",
                columns: new[] { "ActorId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommentInteractionSummaryGap_Occurred",
                table: "CommentInteractionSummaryGaps",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentInteractionActorProjections");

            migrationBuilder.DropTable(
                name: "CommentInteractionSummaryGaps");

            migrationBuilder.DropColumn(
                name: "ContributionVersion",
                table: "CommentSocialRating");
        }
    }
}
