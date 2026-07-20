using Comment.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <summary>
    /// Removes the retired Comment target/visibility copies and the superseded
    /// engagement table. Interaction summary projections are the only current
    /// source for engagement values after this migration.
    /// </summary>
    [DbContext(typeof(CommentContext))]
    [Migration("20260718120000_RemoveCommentCompatibilityArtifacts")]
    public partial class RemoveCommentCompatibilityArtifacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CmtPost_Analytics",
                table: "CommentPost");

            migrationBuilder.DropIndex(
                name: "UX_CommentPost_SocialId",
                table: "CommentPost");

            // The historical schema computed this compatibility value from
            // IdOfRelatedEntity and TypeOfRelatedEntity. MariaDB requires the
            // dependent generated column to be removed before its sources.
            migrationBuilder.DropColumn(
                name: "RelatedEntity",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "SocialId",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "IdOfRelatedEntity",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "TypeOfRelatedEntity",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "VisibilityLevel_Id",
                table: "CommentPost");

            migrationBuilder.DropColumn(
                name: "VisibilityLevel_Name",
                table: "CommentPost");

            migrationBuilder.DropTable(
                name: "CommentSocialAnalytics");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CmtPost_VisibilityLevel",
                table: "CommentPost",
                sql: "`VisibilityLevelId` IN (1, 2, 3, 4)");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Retired Comment compatibility columns and engagement data cannot be restored.");
        }
    }
}
