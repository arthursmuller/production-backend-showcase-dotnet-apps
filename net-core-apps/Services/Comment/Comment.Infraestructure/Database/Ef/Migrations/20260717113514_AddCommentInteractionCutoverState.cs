using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentInteractionCutoverState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentInteractionActorBackfillCheckpoints",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    LastSourceId = table.Column<long>(type: "bigint", nullable: false),
                    TotalExported = table.Column<long>(type: "bigint", nullable: false),
                    CompletedCycles = table.Column<long>(type: "bigint", nullable: false),
                    RollingHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "ascii_bin"),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionActorBackfillCheckpoints", x => x.Name);
                    table.CheckConstraint("CK_CommentInteractionActorBackfill_Counts", "`LastSourceId` >= 0 AND `TotalExported` >= 0 AND `CompletedCycles` >= 0 AND `Version` >= 0");
                    table.CheckConstraint("CK_CommentInteractionActorBackfill_Stage", "`Stage` BETWEEN 1 AND 4");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentInteractionBackfillCheckpoints",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastCommentId = table.Column<int>(type: "int", nullable: false),
                    CycleExported = table.Column<long>(type: "bigint", nullable: false),
                    TotalExported = table.Column<long>(type: "bigint", nullable: false),
                    CompletedCycles = table.Column<long>(type: "bigint", nullable: false),
                    RollingHash = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "ascii_bin"),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionBackfillCheckpoints", x => x.Name);
                    table.CheckConstraint("CK_CommentInteractionBackfill_Counts", "`LastCommentId` >= 0 AND `CycleExported` >= 0 AND `TotalExported` >= `CycleExported` AND `CompletedCycles` >= 0");
                    table.CheckConstraint("CK_CommentInteractionBackfill_Version", "`Version` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentInteractionSummaryProjections",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    DislikeCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingTotal = table.Column<long>(type: "bigint", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false),
                    ShareCount = table.Column<long>(type: "bigint", nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestSeenVersion = table.Column<long>(type: "bigint", nullable: false),
                    HasGap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionSummaryProjections", x => x.CommentId);
                    table.CheckConstraint("CK_CommentInteractionSummary_Nonnegative", "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
                    table.CheckConstraint("CK_CommentInteractionSummary_Versions", "`AppliedVersion` > 0 AND `HighestSeenVersion` >= `AppliedVersion`");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentInteractionSummary_Gap_Updated",
                table: "CommentInteractionSummaryProjections",
                columns: new[] { "HasGap", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentInteractionActorBackfillCheckpoints");

            migrationBuilder.DropTable(
                name: "CommentInteractionBackfillCheckpoints");

            migrationBuilder.DropTable(
                name: "CommentInteractionSummaryProjections");
        }
    }
}
