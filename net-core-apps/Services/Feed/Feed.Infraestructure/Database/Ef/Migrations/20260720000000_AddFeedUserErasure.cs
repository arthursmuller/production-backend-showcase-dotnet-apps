using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedUserErasure",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequestEventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ErasureVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    SecondaryCursor = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseToken = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CacheSafeAfterUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletionEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RowVersion = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedUserErasure", x => x.UserId);
                    table.CheckConstraint("CK_FeedUserErasure_Cursors", "`Cursor` >= 0 AND `SecondaryCursor` >= 0");
                    table.CheckConstraint("CK_FeedUserErasure_ErasureVersion", "`ErasureVersion` > 0");
                    table.CheckConstraint("CK_FeedUserErasure_LeaseToken", "`LeaseToken` >= 0");
                    table.CheckConstraint("CK_FeedUserErasure_Stage", "`Stage` BETWEEN 0 AND 20");
                    table.CheckConstraint("CK_FeedUserErasure_Status", "`Status` BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_FeedUserErasure_User", "`UserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedUserMutationGuard",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedUserMutationGuard", x => x.UserId);
                    table.CheckConstraint("CK_FeedUserMutationGuard_User", "`UserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                "CREATE TRIGGER `TR_FeedUserErasure_RowVersion` " +
                "BEFORE UPDATE ON `FeedUserErasure` FOR EACH ROW " +
                "SET NEW.`RowVersion` = OLD.`RowVersion` + 1;");

            // Older activity revocation rows predate the publisher key. Backfilling it makes
            // publisher cleanup a bounded (OwnerUserId, Id) keyset scan with no activity join.
            migrationBuilder.Sql("""
                UPDATE `FeedRevocationWorkItem` AS work
                INNER JOIN `FeedActivity` AS activity ON activity.`Id` = work.`ActivityId`
                SET work.`OwnerUserId` = activity.`ActorUserId`
                WHERE work.`RevocationKind` = 'activity'
                  AND work.`OwnerUserId` IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionsFeed_Post_Id",
                table: "UserConnectionsFeed",
                columns: new[] { "PostId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionsFeed_User_Id",
                table: "UserConnectionsFeed",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Post_User_Id",
                table: "Post",
                columns: new[] { "UserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRevocationWorkItem_Owner_Id",
                table: "FeedRevocationWorkItem",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRevocationWorkItem_Viewer_Id",
                table: "FeedRevocationWorkItem",
                columns: new[] { "ViewerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipEventQuarantine_Owner_Id",
                table: "FeedRelationshipEventQuarantine",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipEventQuarantine_Viewer_Id",
                table: "FeedRelationshipEventQuarantine",
                columns: new[] { "ViewerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipCheckpoint_Owner_Stream",
                table: "FeedRelationshipCheckpoint",
                columns: new[] { "OwnerUserId", "StreamId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipCheckpoint_Viewer_Stream",
                table: "FeedRelationshipCheckpoint",
                columns: new[] { "ViewerUserId", "StreamId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedEntry_Activity_Id",
                table: "FeedEntry",
                columns: new[] { "ActivityId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedEntry_Recipient_Id",
                table: "FeedEntry",
                columns: new[] { "RecipientUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedAudienceEdge_Owner_Id",
                table: "FeedAudienceEdge",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedAudienceEdge_Viewer_Id",
                table: "FeedAudienceEdge",
                columns: new[] { "ViewerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedActivityEventQuarantine_Actor_Id",
                table: "FeedActivityEventQuarantine",
                columns: new[] { "ActorUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedActivity_Actor_Id",
                table: "FeedActivity",
                columns: new[] { "ActorUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FanoutWorkItem_Publisher_Id",
                table: "FanoutWorkItem",
                columns: new[] { "PublisherUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedUserErasure_Claim",
                table: "FeedUserErasure",
                columns: new[] { "Status", "LeaseExpiresAtUtc", "RequestedAtUtc", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_FeedUserErasure_RowVersion`;");

            migrationBuilder.DropTable(
                name: "FeedUserErasure");

            migrationBuilder.DropTable(
                name: "FeedUserMutationGuard");

            migrationBuilder.DropIndex(
                name: "IX_UserConnectionsFeed_Post_Id",
                table: "UserConnectionsFeed");

            migrationBuilder.DropIndex(
                name: "IX_UserConnectionsFeed_User_Id",
                table: "UserConnectionsFeed");

            migrationBuilder.DropIndex(
                name: "IX_Post_User_Id",
                table: "Post");

            migrationBuilder.DropIndex(
                name: "IX_FeedRevocationWorkItem_Owner_Id",
                table: "FeedRevocationWorkItem");

            migrationBuilder.DropIndex(
                name: "IX_FeedRevocationWorkItem_Viewer_Id",
                table: "FeedRevocationWorkItem");

            migrationBuilder.DropIndex(
                name: "IX_FeedRelationshipEventQuarantine_Owner_Id",
                table: "FeedRelationshipEventQuarantine");

            migrationBuilder.DropIndex(
                name: "IX_FeedRelationshipEventQuarantine_Viewer_Id",
                table: "FeedRelationshipEventQuarantine");

            migrationBuilder.DropIndex(
                name: "IX_FeedRelationshipCheckpoint_Owner_Stream",
                table: "FeedRelationshipCheckpoint");

            migrationBuilder.DropIndex(
                name: "IX_FeedRelationshipCheckpoint_Viewer_Stream",
                table: "FeedRelationshipCheckpoint");

            migrationBuilder.DropIndex(
                name: "IX_FeedEntry_Activity_Id",
                table: "FeedEntry");

            migrationBuilder.DropIndex(
                name: "IX_FeedEntry_Recipient_Id",
                table: "FeedEntry");

            migrationBuilder.DropIndex(
                name: "IX_FeedAudienceEdge_Owner_Id",
                table: "FeedAudienceEdge");

            migrationBuilder.DropIndex(
                name: "IX_FeedAudienceEdge_Viewer_Id",
                table: "FeedAudienceEdge");

            migrationBuilder.DropIndex(
                name: "IX_FeedActivityEventQuarantine_Actor_Id",
                table: "FeedActivityEventQuarantine");

            migrationBuilder.DropIndex(
                name: "IX_FeedActivity_Actor_Id",
                table: "FeedActivity");

            migrationBuilder.DropIndex(
                name: "IX_FanoutWorkItem_Publisher_Id",
                table: "FanoutWorkItem");
        }
    }
}
