using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentOutboxCutoverState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TransferredToMassTransitOnUtc",
                table: "CommentOutboxMessages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentOutboxMessages_TransferredToMassTransitOnUtc",
                table: "CommentOutboxMessages",
                column: "TransferredToMassTransitOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommentOutboxMessages_TransferredToMassTransitOnUtc",
                table: "CommentOutboxMessages");

            migrationBuilder.DropColumn(
                name: "TransferredToMassTransitOnUtc",
                table: "CommentOutboxMessages");
        }
    }
}
