using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentInteractionSourceSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentInteractionSourceSequences",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<long>(type: "bigint", nullable: false),
                    Channel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CurrentValue = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentInteractionSourceSequences", x => new { x.CommentId, x.ActorId, x.Channel });
                    table.CheckConstraint("CK_CommentInteractionSourceSequence_Channel", "`Channel` BETWEEN 1 AND 2");
                    table.CheckConstraint("CK_CommentInteractionSourceSequence_Identity", "`CommentId` > 0 AND `ActorId` > 0");
                    table.CheckConstraint("CK_CommentInteractionSourceSequence_Value", "`CurrentValue` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentInteractionSourceSequences");
        }
    }
}
