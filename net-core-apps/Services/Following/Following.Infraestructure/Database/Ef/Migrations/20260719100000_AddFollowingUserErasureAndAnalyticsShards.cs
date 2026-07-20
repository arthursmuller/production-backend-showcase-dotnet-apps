using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Following.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowingUserErasureAndAnalyticsShards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Side-by-side cutover avoids rebuilding and exclusively locking the legacy hot
            // table. The retained legacy copy is read-only rollback material and is erased by
            // the privacy processor together with the live shards.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_UserRelationAnalytics_Version`;");
            migrationBuilder.RenameTable(
                name: "UserRelationAnalytics",
                newName: "UserRelationAnalyticsLegacy");

            migrationBuilder.CreateTable(
                name: "UserRelationAnalytics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ShardId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TotalFriends = table.Column<int>(type: "int", nullable: false),
                    TotalFollowers = table.Column<int>(type: "int", nullable: false),
                    TotalFollowing = table.Column<int>(type: "int", nullable: false),
                    TotalCategoriesFollowed = table.Column<int>(type: "int", nullable: false),
                    TotalFriendsRequests = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "SISTEMA")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 1L),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRelationAnalytics", x => new { x.Id, x.ShardId });
                    table.CheckConstraint("CK_UserRelationAnalytics_PositiveId", "`Id` > 0");
                    table.CheckConstraint("CK_UserRelationAnalytics_Shard", "`ShardId` BETWEEN 0 AND 63");
                    table.CheckConstraint(
                        "CK_UserRelationAnalytics_NonNegative",
                        "`TotalFriends` >= 0 AND `TotalFollowers` >= 0 AND `TotalFollowing` >= 0 " +
                        "AND `TotalCategoriesFollowed` >= 0 AND `TotalFriendsRequests` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql(
                """
                INSERT INTO `UserRelationAnalytics`
                    (`Id`, `ShardId`, `TotalFriends`, `TotalFollowers`, `TotalFollowing`,
                     `TotalCategoriesFollowed`, `TotalFriendsRequests`, `CreatedDate`,
                     `UpdateDate`, `UserUpdate`, `Version`)
                SELECT `Id`, 0, `TotalFriends`, `TotalFollowers`, `TotalFollowing`,
                       `TotalCategoriesFollowed`, `TotalFriendsRequests`, `CreatedDate`,
                       `UpdateDate`, `UserUpdate`, `Version`
                FROM `UserRelationAnalyticsLegacy`;
                """);

            migrationBuilder.CreateTable(
                name: "FollowingUserErasure",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ErasureVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseToken = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletionEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RowVersion = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowingUserErasure", x => x.UserId);
                    table.CheckConstraint("CK_FollowingUserErasure_Cursor", "`Cursor` >= 0");
                    table.CheckConstraint("CK_FollowingUserErasure_LeaseToken", "`LeaseToken` >= 0");
                    table.CheckConstraint("CK_FollowingUserErasure_Stage", "`Stage` BETWEEN 0 AND 14");
                    table.CheckConstraint("CK_FollowingUserErasure_Status", "`Status` BETWEEN 0 AND 2");
                    table.CheckConstraint("CK_FollowingUserErasure_User", "`UserId` > 0");
                    table.CheckConstraint("CK_FollowingUserErasure_Version", "`ErasureVersion` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserMutationGuard",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMutationGuard", x => x.UserId);
                    table.CheckConstraint("CK_UserMutationGuard_User", "`UserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FollowingUserErasure_Claim",
                table: "FollowingUserErasure",
                columns: new[] { "Status", "LeaseExpiresAtUtc", "UpdatedAtUtc", "UserId" });

            AddAuthorityTriggers(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropAuthorityTriggers(migrationBuilder);
            migrationBuilder.DropTable(name: "FollowingUserErasure");
            migrationBuilder.DropTable(name: "UserMutationGuard");

            // Preserve every post-cutover counter change if an emergency rollback is required.
            migrationBuilder.Sql(
                """
                INSERT INTO `UserRelationAnalyticsLegacy`
                    (`Id`, `TotalFriends`, `TotalFollowers`, `TotalFollowing`,
                     `TotalCategoriesFollowed`, `TotalFriendsRequests`, `CreatedDate`,
                     `UpdateDate`, `UserUpdate`, `Version`)
                SELECT `Id`, SUM(`TotalFriends`), SUM(`TotalFollowers`), SUM(`TotalFollowing`),
                       SUM(`TotalCategoriesFollowed`), SUM(`TotalFriendsRequests`),
                       MIN(`CreatedDate`), MAX(`UpdateDate`), 'SISTEMA', MAX(`Version`)
                FROM `UserRelationAnalytics`
                GROUP BY `Id`
                ON DUPLICATE KEY UPDATE
                    `TotalFriends` = VALUES(`TotalFriends`),
                    `TotalFollowers` = VALUES(`TotalFollowers`),
                    `TotalFollowing` = VALUES(`TotalFollowing`),
                    `TotalCategoriesFollowed` = VALUES(`TotalCategoriesFollowed`),
                    `TotalFriendsRequests` = VALUES(`TotalFriendsRequests`),
                    `UpdateDate` = VALUES(`UpdateDate`),
                    `Version` = VALUES(`Version`);
                """);
            migrationBuilder.DropTable(name: "UserRelationAnalytics");
            migrationBuilder.RenameTable(
                name: "UserRelationAnalyticsLegacy",
                newName: "UserRelationAnalytics");
            migrationBuilder.Sql(
                "CREATE TRIGGER `TR_UserRelationAnalytics_Version` " +
                "BEFORE UPDATE ON `UserRelationAnalytics` FOR EACH ROW " +
                "SET NEW.`Version` = OLD.`Version` + 1, NEW.`UpdateDate` = UTC_TIMESTAMP(6);");
        }

        private static void AddAuthorityTriggers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE TRIGGER `TR_FollowingUserErasure_RowVersion` " +
                "BEFORE UPDATE ON `FollowingUserErasure` FOR EACH ROW " +
                "SET NEW.`RowVersion` = OLD.`RowVersion` + 1;");
            migrationBuilder.Sql(
                "CREATE TRIGGER `TR_UserRelationAnalytics_Version` " +
                "BEFORE UPDATE ON `UserRelationAnalytics` FOR EACH ROW " +
                "SET NEW.`Version` = OLD.`Version` + 1, NEW.`UpdateDate` = UTC_TIMESTAMP(6);");

            AddParticipantTriggers(migrationBuilder, "Follow", "User_Id", "Followed_Id");
            AddParticipantTriggers(migrationBuilder, "Friendship", "User_Id", "Friend_Id");
            AddParticipantTriggers(migrationBuilder, "FriendRequest", "User_Id", "Friend_Id");
            AddParticipantTriggers(migrationBuilder, "UserBlock", "BlockerUserId", "BlockedUserId");
            AddParticipantTriggers(migrationBuilder, "FriendshipSubscriptionFeature", "OwnerId", "RequesterId");
            AddParticipantTriggers(migrationBuilder, "CategoryFollow", "User_Id", null);
            AddParticipantTriggers(migrationBuilder, "UserRelationAnalytics", "Id", null);
            AddStreamTriggers(migrationBuilder);
        }

        private static void DropAuthorityTriggers(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_FollowingUserErasure_RowVersion`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_UserRelationAnalytics_Version`;");
            foreach (var table in new[]
            {
                "Follow", "Friendship", "FriendRequest", "UserBlock",
                "FriendshipSubscriptionFeature", "CategoryFollow", "UserRelationAnalytics",
                "RelationshipEventStream"
            })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_RejectErasedInsert`;");
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_RejectErasedUpdate`;");
            }
        }

        private static void AddParticipantTriggers(
            MigrationBuilder migrationBuilder,
            string table,
            string firstColumn,
            string secondColumn)
        {
            var predicate = secondColumn is null
                ? $"t.`UserId` = NEW.`{firstColumn}`"
                : $"t.`UserId` IN (NEW.`{firstColumn}`, NEW.`{secondColumn}`)";
            foreach (var operation in new[] { "INSERT", "UPDATE" })
            {
                migrationBuilder.Sql(
                    $"""
                    CREATE TRIGGER `TR_{table}_RejectErased{ToTitle(operation)}`
                    BEFORE {operation} ON `{table}`
                    FOR EACH ROW
                    BEGIN
                        IF EXISTS (
                            SELECT 1 FROM `FollowingUserErasure` t
                            WHERE {predicate}
                            LIMIT 1
                        ) THEN
                            SIGNAL SQLSTATE '45000'
                                SET MYSQL_ERRNO = 1644,
                                    MESSAGE_TEXT = 'Relationship participant is erased';
                        END IF;
                    END;
                    """);
            }
        }

        private static void AddStreamTriggers(MigrationBuilder migrationBuilder)
        {
            foreach (var operation in new[] { "INSERT", "UPDATE" })
            {
                migrationBuilder.Sql(
                    $"""
                    CREATE TRIGGER `TR_RelationshipEventStream_RejectErased{ToTitle(operation)}`
                    BEFORE {operation} ON `RelationshipEventStream`
                    FOR EACH ROW
                    BEGIN
                        IF NEW.`CurrentState` <> 'revoked' AND EXISTS (
                            SELECT 1 FROM `FollowingUserErasure` t
                            WHERE t.`UserId` IN (NEW.`OwnerUserId`, NEW.`ViewerUserId`)
                            LIMIT 1
                        ) THEN
                            SIGNAL SQLSTATE '45000'
                                SET MYSQL_ERRNO = 1644,
                                    MESSAGE_TEXT = 'Relationship participant is erased';
                        END IF;
                    END;
                    """);
            }
        }

        private static string ToTitle(string operation) =>
            operation == "INSERT" ? "Insert" : "Update";
    }
}
