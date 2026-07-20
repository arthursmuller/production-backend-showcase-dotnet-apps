using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommentOutboxMessages_ProcessedOnUtc_NextAttemptOnUtc_Locked~",
                table: "CommentOutboxMessages");

            migrationBuilder.AddColumn<bool>(
                name: "CategoryMentionsPublished",
                table: "CommentPost",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedOnUtc",
                table: "CommentOutboxMessages",
                type: "datetime(6)",
                nullable: true);

            // Before the outbox refactor, category mentions remained counted when a
            // published comment was hidden. PublishedOnFeed is therefore the only
            // durable legacy signal that those counts still need a later decrement.
            migrationBuilder.Sql(
                "UPDATE `CommentPost` SET `CategoryMentionsPublished` = `PublishedOnFeed`;");

            migrationBuilder.CreateIndex(
                name: "IX_CommentOutboxMessages_ProcessedOnUtc_FailedOnUtc_NextAttempt~",
                table: "CommentOutboxMessages",
                columns: new[] { "ProcessedOnUtc", "FailedOnUtc", "NextAttemptOnUtc", "LockedUntilUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommentOutboxMessages_ProcessedOnUtc_FailedOnUtc_NextAttempt~",
                table: "CommentOutboxMessages");

            migrationBuilder.DropColumn(
                name: "CategoryMentionsPublished",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "FailedOnUtc",
                table: "CommentOutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_CommentOutboxMessages_ProcessedOnUtc_NextAttemptOnUtc_Locked~",
                table: "CommentOutboxMessages",
                columns: new[] { "ProcessedOnUtc", "NextAttemptOnUtc", "LockedUntilUtc" });
        }
    }
}
