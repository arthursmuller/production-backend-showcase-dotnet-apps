using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRetiredCommentArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CmtUserErasure_Stage",
                table: "CommentUserErasure");

            migrationBuilder.Sql(
                "UPDATE `CommentUserErasure` SET `Cursor` = 0 WHERE `Stage` BETWEEN 3 AND 9;");
            migrationBuilder.Sql(
                "UPDATE `CommentUserErasure` SET `Stage` = CASE WHEN `Stage` BETWEEN 3 AND 9 THEN 3 WHEN `Stage` BETWEEN 10 AND 17 THEN `Stage` - 7 ELSE `Stage` END WHERE `Stage` BETWEEN 3 AND 17;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CmtUserErasure_Stage",
                table: "CommentUserErasure",
                sql: "`Stage` BETWEEN 1 AND 10");

            migrationBuilder.DropTable(name: "CommentBookmark");
            migrationBuilder.DropTable(name: "CommentDislike");
            migrationBuilder.DropTable(name: "CommentInteractionActorBackfillCheckpoints");
            migrationBuilder.DropTable(name: "CommentInteractionBackfillCheckpoints");
            migrationBuilder.DropTable(name: "CommentInteractionSourceSequences");
            migrationBuilder.DropTable(name: "CommentLike");
            migrationBuilder.DropTable(name: "CommentModerationBackfillCheckpoints");
            migrationBuilder.DropTable(name: "CommentOutboxMessages");
            migrationBuilder.DropTable(name: "CommentPostComment");
            migrationBuilder.DropTable(name: "CommentRatingLevel");
            migrationBuilder.DropTable(name: "CommentReport");
            migrationBuilder.DropTable(name: "CommentShare");
            migrationBuilder.DropTable(name: "RatingLevel");
            migrationBuilder.DropTable(name: "CommentSocialRating");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Removed Comment-owned interaction, moderation, and outbox data cannot be restored.");
        }
    }
}
