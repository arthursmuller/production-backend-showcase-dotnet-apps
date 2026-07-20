using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    public partial class RemoveRetiredFeedProjection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `FeedUserErasure`
                SET `Stage` = 9, `Cursor` = 0, `SecondaryCursor` = 0
                WHERE `Stage` IN (6, 7, 8);

                DROP TABLE IF EXISTS `FeedLegacyEntryDedupArchive`;
                DROP TABLE IF EXISTS `FeedLegacyPostDedupArchive`;
                """);

            migrationBuilder.DropTable(name: "UserConnectionsFeed");
            migrationBuilder.DropTable(name: "VisbilityLevels");
            migrationBuilder.DropTable(name: "Post");
            migrationBuilder.DropIndex(
                name: "IX_FeedActivity_LegacyPostId",
                table: "FeedActivity");
            migrationBuilder.DropColumn(
                name: "LegacyPostId",
                table: "FeedActivity");
        }

        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException(
                "The retired Feed projection schema contained deleted data and cannot be restored.");
    }
}
