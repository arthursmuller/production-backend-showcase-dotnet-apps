using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentModerationBackfillCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentModerationBackfillCheckpoints",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    LastSourceId = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_CommentModerationBackfillCheckpoints", x => x.Name);
                    table.CheckConstraint("CK_CommentModerationBackfill_Counts", "`LastSourceId` >= 0 AND `CycleExported` >= 0 AND `TotalExported` >= `CycleExported` AND `CompletedCycles` >= 0 AND `Version` >= 0");
                    table.CheckConstraint("CK_CommentModerationBackfill_Stage", "`Stage` BETWEEN 1 AND 3");
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentModerationBackfillCheckpoints");
        }
    }
}
