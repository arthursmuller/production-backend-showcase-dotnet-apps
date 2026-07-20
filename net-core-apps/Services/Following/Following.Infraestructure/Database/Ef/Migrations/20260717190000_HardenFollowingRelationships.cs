using Following.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Following.Infra.Migrations
{
    [DbContext(typeof(FollowingContext))]
    [Migration("20260717190000_HardenFollowingRelationships")]
    public partial class HardenFollowingRelationships : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddVersionColumns(migrationBuilder);

            migrationBuilder.AddColumn<byte>(
                name: "Status",
                table: "FriendRequest",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            CreateRepairLog(migrationBuilder);
            RepairLegacyData(migrationBuilder);
            HardenColumns(migrationBuilder);
            AddCanonicalPairColumns(migrationBuilder);
            AddConstraintsAndIndexes(migrationBuilder);
            AddVersionTriggers(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropVersionTriggers(migrationBuilder);

            migrationBuilder.DropCheckConstraint("CK_CategoryFollow_PositiveIds", "CategoryFollow");
            migrationBuilder.DropCheckConstraint("CK_Follow_PositiveUsers", "Follow");
            migrationBuilder.DropCheckConstraint("CK_Follow_NoSelf", "Follow");
            migrationBuilder.DropCheckConstraint("CK_FriendRequest_PositiveUsers", "FriendRequest");
            migrationBuilder.DropCheckConstraint("CK_FriendRequest_NoSelf", "FriendRequest");
            migrationBuilder.DropCheckConstraint("CK_FriendRequest_Status", "FriendRequest");
            migrationBuilder.DropCheckConstraint("CK_Friendship_PositiveUsers", "Friendship");
            migrationBuilder.DropCheckConstraint("CK_Friendship_NoSelf", "Friendship");
            migrationBuilder.DropCheckConstraint("CK_SubscriptionFeature_PositiveUsers", "FriendshipSubscriptionFeature");
            migrationBuilder.DropCheckConstraint("CK_SubscriptionFeature_NoSelf", "FriendshipSubscriptionFeature");
            migrationBuilder.DropCheckConstraint("CK_UserRelationAnalytics_PositiveId", "UserRelationAnalytics");
            migrationBuilder.DropCheckConstraint("CK_UserRelationAnalytics_NonNegative", "UserRelationAnalytics");

            migrationBuilder.DropIndex("UX_CategoryFollow_User_Category", "CategoryFollow");
            migrationBuilder.DropIndex("IX_CategoryFollow_User_Cursor", "CategoryFollow");
            migrationBuilder.DropIndex("IX_CategoryFollow_Category_User", "CategoryFollow");
            migrationBuilder.DropIndex("UX_Follow_Direction", "Follow");
            migrationBuilder.DropIndex("IX_Follow_Follower_Cursor", "Follow");
            migrationBuilder.DropIndex("IX_Follow_Followed_Cursor", "Follow");
            migrationBuilder.DropIndex("UX_FriendRequest_ActivePair", "FriendRequest");
            migrationBuilder.DropIndex("IX_FriendRequest_Pair_Status", "FriendRequest");
            migrationBuilder.DropIndex("IX_FriendRequest_Sender_Status_Cursor", "FriendRequest");
            migrationBuilder.DropIndex("IX_FriendRequest_Recipient_Status_Cursor", "FriendRequest");
            migrationBuilder.DropIndex("UX_Friendship_Direction", "Friendship");
            migrationBuilder.DropIndex("IX_Friendship_NormalizedPair", "Friendship");
            migrationBuilder.DropIndex("IX_Friendship_User_Cursor", "Friendship");
            migrationBuilder.DropIndex("IX_Friendship_Reverse_Cursor", "Friendship");
            migrationBuilder.DropIndex(
                "UX_SubscriptionFeature_Owner_Requester_Subscription",
                "FriendshipSubscriptionFeature");
            migrationBuilder.DropIndex("IX_SubscriptionFeature_Expiry", "FriendshipSubscriptionFeature");

            migrationBuilder.DropColumn("PairMinUserId", "FriendRequest");
            migrationBuilder.DropColumn("PairMaxUserId", "FriendRequest");
            migrationBuilder.DropColumn("ActivePairMinUserId", "FriendRequest");
            migrationBuilder.DropColumn("ActivePairMaxUserId", "FriendRequest");
            migrationBuilder.DropColumn("PairMinUserId", "Friendship");
            migrationBuilder.DropColumn("PairMaxUserId", "Friendship");

            RelaxColumns(migrationBuilder);

            migrationBuilder.DropColumn("Status", "FriendRequest");
            migrationBuilder.DropColumn("Version", "CategoryFollow");
            migrationBuilder.DropColumn("Version", "Follow");
            migrationBuilder.DropColumn("Version", "FriendRequest");
            migrationBuilder.DropColumn("Version", "Friendship");
            migrationBuilder.DropColumn("Version", "FriendshipSubscriptionFeature");
            migrationBuilder.DropColumn("Version", "UserRelationAnalytics");

            // The repair log is intentionally retained across a rollback. It contains
            // the exact legacy rows removed by this migration and is the operator's
            // recovery/audit source if a forward repair is required.
        }

        private static void AddVersionColumns(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
            {
                "CategoryFollow",
                "Follow",
                "FriendRequest",
                "Friendship",
                "FriendshipSubscriptionFeature",
                "UserRelationAnalytics"
            })
            {
                migrationBuilder.AddColumn<long>(
                    name: "Version",
                    table: table,
                    type: "bigint",
                    nullable: false,
                    defaultValue: 1L);
            }
        }

        private static void CreateRepairLog(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS `FollowingRelationshipRepairLog` (
                    `Id` bigint NOT NULL AUTO_INCREMENT,
                    `EntityType` varchar(48) CHARACTER SET utf8mb4 NOT NULL,
                    `LegacyEntityId` int NOT NULL,
                    `RepairAction` varchar(48) CHARACTER SET utf8mb4 NOT NULL,
                    `OriginalData` longtext CHARACTER SET utf8mb4 NOT NULL,
                    `RecordedAtUtc` datetime(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
                    CONSTRAINT `PK_FollowingRelationshipRepairLog` PRIMARY KEY (`Id`),
                    INDEX `IX_FollowingRelationshipRepairLog_Entity_Id`
                        (`EntityType`, `LegacyEntityId`)
                ) CHARACTER SET=utf8mb4;
                """);

        private static void RepairLegacyData(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `FriendRequest`
                SET `Status` = CASE
                    WHEN `Approved` IS NULL THEN 0
                    WHEN `Approved` = 1 THEN 1
                    ELSE 2
                END;

                UPDATE `CategoryFollow` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                UPDATE `Follow` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                UPDATE `FriendRequest` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                UPDATE `Friendship` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                UPDATE `FriendshipSubscriptionFeature` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                UPDATE `UserRelationAnalytics` SET `UserUpdate` = 'SISTEMA' WHERE `UserUpdate` IS NULL;
                """);

            ArchiveAndDeleteInvalidRows(migrationBuilder);
            NormalizeBoundedProjections(migrationBuilder);
            ArchiveAndRemoveDuplicates(migrationBuilder);
            RepairDirectionalFriendships(migrationBuilder);
            ReconcileAnalytics(migrationBuilder);
        }

        private static void ArchiveAndDeleteInvalidRows(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Follow', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'UserId', `User_Id`,
                        'UserFullName', `User_FullName`, 'FollowedUserId', `Followed_Id`,
                        'FollowedFullName', `Followed_FullName`,
                        'IsFollowedBack', `IsFollowedBack`, 'CreatedDate', `CreatedDate`,
                        'UpdateDate', `UpdateDate`, 'UserUpdate', `UserUpdate`,
                        'Version', `Version`)
                FROM `Follow`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Followed_Id` IS NULL OR `Followed_Id` <= 0
                   OR `User_Id` = `Followed_Id`;
                DELETE FROM `Follow`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Followed_Id` IS NULL OR `Followed_Id` <= 0
                   OR `User_Id` = `Followed_Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendRequest', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'SenderUserId', `User_Id`,
                        'SenderFullName', `User_FullName`, 'RecipientUserId', `Friend_Id`,
                        'RecipientFullName', `Friend_FullName`, 'Approved', `Approved`,
                        'Status', `Status`, 'CreatedDate', `CreatedDate`,
                        'UpdateDate', `UpdateDate`, 'UserUpdate', `UserUpdate`,
                        'Version', `Version`)
                FROM `FriendRequest`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Friend_Id` IS NULL OR `Friend_Id` <= 0
                   OR `User_Id` = `Friend_Id`;
                DELETE FROM `FriendRequest`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Friend_Id` IS NULL OR `Friend_Id` <= 0
                   OR `User_Id` = `Friend_Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Friendship', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'UserId', `User_Id`,
                        'UserFullName', `User_FullName`, 'FriendUserId', `Friend_Id`,
                        'FriendFullName', `Friend_FullName`, 'CreatedDate', `CreatedDate`,
                        'UpdateDate', `UpdateDate`, 'UserUpdate', `UserUpdate`,
                        'Version', `Version`)
                FROM `Friendship`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Friend_Id` IS NULL OR `Friend_Id` <= 0
                   OR `User_Id` = `Friend_Id`;
                DELETE FROM `Friendship`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Friend_Id` IS NULL OR `Friend_Id` <= 0
                   OR `User_Id` = `Friend_Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'CategoryFollow', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'UserId', `User_Id`,
                        'UserFullName', `User_FullName`, 'CategoryId', `Followed_Id`,
                        'CategoryName', `Followed_Name`, 'CreatedDate', `CreatedDate`,
                        'UpdateDate', `UpdateDate`, 'UserUpdate', `UserUpdate`,
                        'Version', `Version`)
                FROM `CategoryFollow`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Followed_Id` IS NULL OR `Followed_Id` <= 0;
                DELETE FROM `CategoryFollow`
                WHERE `User_Id` IS NULL OR `User_Id` <= 0
                   OR `Followed_Id` IS NULL OR `Followed_Id` <= 0;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendshipSubscriptionFeature', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'OwnerId', `OwnerId`,
                        'RequesterId', `RequesterId`, 'SubscriptionId', `SubscriptionId`,
                        'EndDate', `EndDate`, 'CreatedDate', `CreatedDate`,
                        'UpdateDate', `UpdateDate`, 'UserUpdate', `UserUpdate`,
                        'Version', `Version`)
                FROM `FriendshipSubscriptionFeature`
                WHERE `OwnerId` <= 0 OR `RequesterId` <= 0 OR `OwnerId` = `RequesterId`
                   OR `SubscriptionId` IS NULL OR TRIM(`SubscriptionId`) = ''
                   OR CHAR_LENGTH(TRIM(`SubscriptionId`)) > 191;
                DELETE FROM `FriendshipSubscriptionFeature`
                WHERE `OwnerId` <= 0 OR `RequesterId` <= 0 OR `OwnerId` = `RequesterId`
                   OR `SubscriptionId` IS NULL OR TRIM(`SubscriptionId`) = ''
                   OR CHAR_LENGTH(TRIM(`SubscriptionId`)) > 191;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'UserRelationAnalytics', `Id`, 'DeletedInvalidIdentity',
                    JSON_OBJECT('Id', `Id`, 'TotalFriends', `TotalFriends`,
                        'TotalFollowers', `TotalFollowers`, 'TotalFollowing', `TotalFollowing`,
                        'TotalCategoriesFollowed', `TotalCategoriesFollowed`,
                        'TotalFriendsRequests', `TotalFriendsRequests`,
                        'CreatedDate', `CreatedDate`, 'UpdateDate', `UpdateDate`,
                        'UserUpdate', `UserUpdate`, 'Version', `Version`)
                FROM `UserRelationAnalytics`
                WHERE `Id` <= 0;
                DELETE FROM `UserRelationAnalytics` WHERE `Id` <= 0;
                """);
        }

        private static void NormalizeBoundedProjections(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Follow', `Id`, 'NormalizedProjection',
                    JSON_OBJECT('UserFullName', `User_FullName`,
                        'FollowedFullName', `Followed_FullName`)
                FROM `Follow`
                WHERE NOT (`User_FullName` <=> NULLIF(LEFT(TRIM(`User_FullName`), 200), ''))
                   OR NOT (`Followed_FullName` <=> NULLIF(LEFT(TRIM(`Followed_FullName`), 200), ''));
                UPDATE `Follow`
                SET `User_FullName` = NULLIF(LEFT(TRIM(`User_FullName`), 200), ''),
                    `Followed_FullName` = NULLIF(LEFT(TRIM(`Followed_FullName`), 200), '');

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendRequest', `Id`, 'NormalizedProjection',
                    JSON_OBJECT('UserFullName', `User_FullName`,
                        'FriendFullName', `Friend_FullName`)
                FROM `FriendRequest`
                WHERE NOT (`User_FullName` <=> NULLIF(LEFT(TRIM(`User_FullName`), 200), ''))
                   OR NOT (`Friend_FullName` <=> NULLIF(LEFT(TRIM(`Friend_FullName`), 200), ''));
                UPDATE `FriendRequest`
                SET `User_FullName` = NULLIF(LEFT(TRIM(`User_FullName`), 200), ''),
                    `Friend_FullName` = NULLIF(LEFT(TRIM(`Friend_FullName`), 200), '');

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Friendship', `Id`, 'NormalizedProjection',
                    JSON_OBJECT('UserFullName', `User_FullName`,
                        'FriendFullName', `Friend_FullName`)
                FROM `Friendship`
                WHERE NOT (`User_FullName` <=> NULLIF(LEFT(TRIM(`User_FullName`), 200), ''))
                   OR NOT (`Friend_FullName` <=> NULLIF(LEFT(TRIM(`Friend_FullName`), 200), ''));
                UPDATE `Friendship`
                SET `User_FullName` = NULLIF(LEFT(TRIM(`User_FullName`), 200), ''),
                    `Friend_FullName` = NULLIF(LEFT(TRIM(`Friend_FullName`), 200), '');

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'CategoryFollow', `Id`, 'NormalizedProjection',
                    JSON_OBJECT('UserFullName', `User_FullName`,
                        'CategoryName', `Followed_Name`)
                FROM `CategoryFollow`
                WHERE NOT (`User_FullName` <=> NULLIF(LEFT(TRIM(`User_FullName`), 200), ''))
                   OR NOT (`Followed_Name` <=> NULLIF(LEFT(TRIM(`Followed_Name`), 128), ''));
                UPDATE `CategoryFollow`
                SET `User_FullName` = NULLIF(LEFT(TRIM(`User_FullName`), 200), ''),
                    `Followed_Name` = NULLIF(LEFT(TRIM(`Followed_Name`), 128), '');

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendshipSubscriptionFeature', `Id`, 'NormalizedExternalId',
                    JSON_OBJECT('SubscriptionId', `SubscriptionId`)
                FROM `FriendshipSubscriptionFeature`
                WHERE NOT (`SubscriptionId` <=> TRIM(`SubscriptionId`));
                UPDATE `FriendshipSubscriptionFeature`
                SET `SubscriptionId` = TRIM(`SubscriptionId`);
                """);

        private static void ArchiveAndRemoveDuplicates(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Follow', dupe.`Id`, 'DeletedDuplicate',
                    JSON_OBJECT('Id', dupe.`Id`, 'UserId', dupe.`User_Id`,
                        'UserFullName', dupe.`User_FullName`,
                        'FollowedUserId', dupe.`Followed_Id`,
                        'FollowedFullName', dupe.`Followed_FullName`,
                        'IsFollowedBack', dupe.`IsFollowedBack`,
                        'CreatedDate', dupe.`CreatedDate`, 'UpdateDate', dupe.`UpdateDate`,
                        'UserUpdate', dupe.`UserUpdate`, 'Version', dupe.`Version`)
                FROM `Follow` dupe
                WHERE EXISTS (
                    SELECT 1 FROM `Follow` keeper
                    WHERE keeper.`User_Id` = dupe.`User_Id`
                      AND keeper.`Followed_Id` = dupe.`Followed_Id`
                      AND keeper.`Id` < dupe.`Id`);
                DELETE dupe FROM `Follow` dupe
                JOIN `Follow` keeper
                  ON keeper.`User_Id` = dupe.`User_Id`
                 AND keeper.`Followed_Id` = dupe.`Followed_Id`
                 AND keeper.`Id` < dupe.`Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'CategoryFollow', dupe.`Id`, 'DeletedDuplicate',
                    JSON_OBJECT('Id', dupe.`Id`, 'UserId', dupe.`User_Id`,
                        'UserFullName', dupe.`User_FullName`,
                        'CategoryId', dupe.`Followed_Id`,
                        'CategoryName', dupe.`Followed_Name`,
                        'CreatedDate', dupe.`CreatedDate`, 'UpdateDate', dupe.`UpdateDate`,
                        'UserUpdate', dupe.`UserUpdate`, 'Version', dupe.`Version`)
                FROM `CategoryFollow` dupe
                WHERE EXISTS (
                    SELECT 1 FROM `CategoryFollow` keeper
                    WHERE keeper.`User_Id` = dupe.`User_Id`
                      AND keeper.`Followed_Id` = dupe.`Followed_Id`
                      AND keeper.`Id` < dupe.`Id`);
                DELETE dupe FROM `CategoryFollow` dupe
                JOIN `CategoryFollow` keeper
                  ON keeper.`User_Id` = dupe.`User_Id`
                 AND keeper.`Followed_Id` = dupe.`Followed_Id`
                 AND keeper.`Id` < dupe.`Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Friendship', dupe.`Id`, 'DeletedDuplicateDirection',
                    JSON_OBJECT('Id', dupe.`Id`, 'UserId', dupe.`User_Id`,
                        'UserFullName', dupe.`User_FullName`,
                        'FriendUserId', dupe.`Friend_Id`,
                        'FriendFullName', dupe.`Friend_FullName`,
                        'CreatedDate', dupe.`CreatedDate`, 'UpdateDate', dupe.`UpdateDate`,
                        'UserUpdate', dupe.`UserUpdate`, 'Version', dupe.`Version`)
                FROM `Friendship` dupe
                WHERE EXISTS (
                    SELECT 1 FROM `Friendship` keeper
                    WHERE keeper.`User_Id` = dupe.`User_Id`
                      AND keeper.`Friend_Id` = dupe.`Friend_Id`
                      AND keeper.`Id` < dupe.`Id`);
                DELETE dupe FROM `Friendship` dupe
                JOIN `Friendship` keeper
                  ON keeper.`User_Id` = dupe.`User_Id`
                 AND keeper.`Friend_Id` = dupe.`Friend_Id`
                 AND keeper.`Id` < dupe.`Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendRequest', dupe.`Id`, 'DeletedDuplicateActivePair',
                    JSON_OBJECT('Id', dupe.`Id`, 'SenderUserId', dupe.`User_Id`,
                        'SenderFullName', dupe.`User_FullName`,
                        'RecipientUserId', dupe.`Friend_Id`,
                        'RecipientFullName', dupe.`Friend_FullName`,
                        'Approved', dupe.`Approved`, 'Status', dupe.`Status`,
                        'CreatedDate', dupe.`CreatedDate`, 'UpdateDate', dupe.`UpdateDate`,
                        'UserUpdate', dupe.`UserUpdate`, 'Version', dupe.`Version`)
                FROM `FriendRequest` dupe
                WHERE dupe.`Status` = 0
                  AND EXISTS (
                    SELECT 1 FROM `FriendRequest` keeper
                    WHERE keeper.`Status` = 0
                      AND LEAST(keeper.`User_Id`, keeper.`Friend_Id`)
                          = LEAST(dupe.`User_Id`, dupe.`Friend_Id`)
                      AND GREATEST(keeper.`User_Id`, keeper.`Friend_Id`)
                          = GREATEST(dupe.`User_Id`, dupe.`Friend_Id`)
                      AND keeper.`Id` < dupe.`Id`);
                DELETE dupe FROM `FriendRequest` dupe
                JOIN `FriendRequest` keeper
                  ON keeper.`Status` = 0
                 AND dupe.`Status` = 0
                 AND LEAST(keeper.`User_Id`, keeper.`Friend_Id`)
                     = LEAST(dupe.`User_Id`, dupe.`Friend_Id`)
                 AND GREATEST(keeper.`User_Id`, keeper.`Friend_Id`)
                     = GREATEST(dupe.`User_Id`, dupe.`Friend_Id`)
                 AND keeper.`Id` < dupe.`Id`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'FriendshipSubscriptionFeature', dupe.`Id`, 'DeletedDuplicate',
                    JSON_OBJECT('Id', dupe.`Id`, 'OwnerId', dupe.`OwnerId`,
                        'RequesterId', dupe.`RequesterId`,
                        'SubscriptionId', dupe.`SubscriptionId`,
                        'EndDate', dupe.`EndDate`, 'CreatedDate', dupe.`CreatedDate`,
                        'UpdateDate', dupe.`UpdateDate`, 'UserUpdate', dupe.`UserUpdate`,
                        'Version', dupe.`Version`)
                FROM `FriendshipSubscriptionFeature` dupe
                WHERE EXISTS (
                    SELECT 1 FROM `FriendshipSubscriptionFeature` keeper
                    WHERE keeper.`OwnerId` = dupe.`OwnerId`
                      AND keeper.`RequesterId` = dupe.`RequesterId`
                      AND keeper.`SubscriptionId` = dupe.`SubscriptionId`
                      AND (keeper.`EndDate` > dupe.`EndDate`
                           OR (keeper.`EndDate` = dupe.`EndDate`
                               AND keeper.`Id` < dupe.`Id`)));
                DELETE dupe FROM `FriendshipSubscriptionFeature` dupe
                JOIN `FriendshipSubscriptionFeature` keeper
                  ON keeper.`OwnerId` = dupe.`OwnerId`
                 AND keeper.`RequesterId` = dupe.`RequesterId`
                 AND keeper.`SubscriptionId` = dupe.`SubscriptionId`
                 AND (keeper.`EndDate` > dupe.`EndDate`
                      OR (keeper.`EndDate` = dupe.`EndDate`
                          AND keeper.`Id` < dupe.`Id`));
                """);
        }

        private static void RepairDirectionalFriendships(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Friendship', edge.`Id`, 'InsertedMissingReverse',
                    JSON_OBJECT('Id', edge.`Id`, 'UserId', edge.`User_Id`,
                        'UserFullName', edge.`User_FullName`,
                        'FriendUserId', edge.`Friend_Id`,
                        'FriendFullName', edge.`Friend_FullName`,
                        'CreatedDate', edge.`CreatedDate`,
                        'UpdateDate', edge.`UpdateDate`,
                        'UserUpdate', edge.`UserUpdate`, 'Version', edge.`Version`)
                FROM `Friendship` edge
                WHERE NOT EXISTS (
                    SELECT 1 FROM `Friendship` reverse
                    WHERE reverse.`User_Id` = edge.`Friend_Id`
                      AND reverse.`Friend_Id` = edge.`User_Id`);

                INSERT INTO `Friendship`
                    (`User_Id`, `User_FullName`, `Friend_Id`, `Friend_FullName`,
                     `CreatedDate`, `UpdateDate`, `UserUpdate`, `Version`)
                SELECT edge.`Friend_Id`, edge.`Friend_FullName`,
                       edge.`User_Id`, edge.`User_FullName`,
                       edge.`CreatedDate`, edge.`UpdateDate`, 'MIGRATION', 1
                FROM `Friendship` edge
                WHERE NOT EXISTS (
                    SELECT 1 FROM `Friendship` reverse
                    WHERE reverse.`User_Id` = edge.`Friend_Id`
                      AND reverse.`Friend_Id` = edge.`User_Id`);

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'Follow', edge.`Id`, 'ReconciledReciprocalFlag',
                    JSON_OBJECT('IsFollowedBack', edge.`IsFollowedBack`)
                FROM `Follow` edge
                LEFT JOIN `Follow` reverse
                  ON reverse.`User_Id` = edge.`Followed_Id`
                 AND reverse.`Followed_Id` = edge.`User_Id`
                WHERE edge.`IsFollowedBack`
                    <> CASE WHEN reverse.`Id` IS NULL THEN 0 ELSE 1 END;

                UPDATE `Follow` edge
                LEFT JOIN `Follow` reverse
                  ON reverse.`User_Id` = edge.`Followed_Id`
                 AND reverse.`Followed_Id` = edge.`User_Id`
                SET edge.`IsFollowedBack` = CASE WHEN reverse.`Id` IS NULL THEN 0 ELSE 1 END;
                """);

        private static void ReconcileAnalytics(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DROP TEMPORARY TABLE IF EXISTS `FollowingAnalyticsRepair`;
                CREATE TEMPORARY TABLE `FollowingAnalyticsRepair` (
                    `UserId` int NOT NULL,
                    `TotalFriends` int NOT NULL DEFAULT 0,
                    `TotalFollowers` int NOT NULL DEFAULT 0,
                    `TotalFollowing` int NOT NULL DEFAULT 0,
                    `TotalCategoriesFollowed` int NOT NULL DEFAULT 0,
                    `TotalFriendsRequests` int NOT NULL DEFAULT 0,
                    PRIMARY KEY (`UserId`)
                );

                INSERT IGNORE INTO `FollowingAnalyticsRepair` (`UserId`)
                SELECT `Id` FROM `UserRelationAnalytics`
                UNION SELECT `User_Id` FROM `Friendship`
                UNION SELECT `Friend_Id` FROM `Friendship`
                UNION SELECT `User_Id` FROM `Follow`
                UNION SELECT `Followed_Id` FROM `Follow`
                UNION SELECT `User_Id` FROM `CategoryFollow`
                UNION SELECT `User_Id` FROM `FriendRequest`
                UNION SELECT `Friend_Id` FROM `FriendRequest`
                UNION SELECT `OwnerId` FROM `FriendshipSubscriptionFeature`
                UNION SELECT `RequesterId` FROM `FriendshipSubscriptionFeature`;

                UPDATE `FollowingAnalyticsRepair` repair
                JOIN (SELECT `User_Id` AS `UserId`, COUNT(*) AS `Value`
                      FROM `Friendship` GROUP BY `User_Id`) counts
                  ON counts.`UserId` = repair.`UserId`
                SET repair.`TotalFriends` = counts.`Value`;
                UPDATE `FollowingAnalyticsRepair` repair
                JOIN (SELECT `Followed_Id` AS `UserId`, COUNT(*) AS `Value`
                      FROM `Follow` GROUP BY `Followed_Id`) counts
                  ON counts.`UserId` = repair.`UserId`
                SET repair.`TotalFollowers` = counts.`Value`;
                UPDATE `FollowingAnalyticsRepair` repair
                JOIN (SELECT `User_Id` AS `UserId`, COUNT(*) AS `Value`
                      FROM `Follow` GROUP BY `User_Id`) counts
                  ON counts.`UserId` = repair.`UserId`
                SET repair.`TotalFollowing` = counts.`Value`;
                UPDATE `FollowingAnalyticsRepair` repair
                JOIN (SELECT `User_Id` AS `UserId`, COUNT(*) AS `Value`
                      FROM `CategoryFollow` GROUP BY `User_Id`) counts
                  ON counts.`UserId` = repair.`UserId`
                SET repair.`TotalCategoriesFollowed` = counts.`Value`;
                UPDATE `FollowingAnalyticsRepair` repair
                JOIN (SELECT `Friend_Id` AS `UserId`, COUNT(*) AS `Value`
                      FROM `FriendRequest` WHERE `Status` = 0 GROUP BY `Friend_Id`) counts
                  ON counts.`UserId` = repair.`UserId`
                SET repair.`TotalFriendsRequests` = counts.`Value`;

                INSERT INTO `FollowingRelationshipRepairLog`
                    (`EntityType`, `LegacyEntityId`, `RepairAction`, `OriginalData`)
                SELECT 'UserRelationAnalytics', analytics.`Id`, 'ReconciledCounters',
                    JSON_OBJECT('TotalFriends', analytics.`TotalFriends`,
                        'TotalFollowers', analytics.`TotalFollowers`,
                        'TotalFollowing', analytics.`TotalFollowing`,
                        'TotalCategoriesFollowed', analytics.`TotalCategoriesFollowed`,
                        'TotalFriendsRequests', analytics.`TotalFriendsRequests`)
                FROM `UserRelationAnalytics` analytics
                JOIN `FollowingAnalyticsRepair` repair ON repair.`UserId` = analytics.`Id`
                WHERE analytics.`TotalFriends` <> repair.`TotalFriends`
                   OR analytics.`TotalFollowers` <> repair.`TotalFollowers`
                   OR analytics.`TotalFollowing` <> repair.`TotalFollowing`
                   OR analytics.`TotalCategoriesFollowed` <> repair.`TotalCategoriesFollowed`
                   OR analytics.`TotalFriendsRequests` <> repair.`TotalFriendsRequests`;

                INSERT INTO `UserRelationAnalytics`
                    (`Id`, `TotalFriends`, `TotalFollowers`, `TotalFollowing`,
                     `TotalCategoriesFollowed`, `TotalFriendsRequests`,
                     `CreatedDate`, `UpdateDate`, `UserUpdate`, `Version`)
                SELECT repair.`UserId`, repair.`TotalFriends`, repair.`TotalFollowers`,
                       repair.`TotalFollowing`, repair.`TotalCategoriesFollowed`,
                       repair.`TotalFriendsRequests`, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6),
                       'MIGRATION', 1
                FROM `FollowingAnalyticsRepair` repair
                WHERE NOT EXISTS (
                    SELECT 1 FROM `UserRelationAnalytics` analytics
                    WHERE analytics.`Id` = repair.`UserId`);

                UPDATE `UserRelationAnalytics` analytics
                JOIN `FollowingAnalyticsRepair` repair ON repair.`UserId` = analytics.`Id`
                SET analytics.`TotalFriends` = repair.`TotalFriends`,
                    analytics.`TotalFollowers` = repair.`TotalFollowers`,
                    analytics.`TotalFollowing` = repair.`TotalFollowing`,
                    analytics.`TotalCategoriesFollowed` = repair.`TotalCategoriesFollowed`,
                    analytics.`TotalFriendsRequests` = repair.`TotalFriendsRequests`;

                DROP TEMPORARY TABLE `FollowingAnalyticsRepair`;
                """);

        private static void HardenColumns(MigrationBuilder migrationBuilder)
        {
            HardenRelationshipColumns(migrationBuilder, "Follow", "Followed_Id", "Followed_FullName");
            HardenRelationshipColumns(migrationBuilder, "FriendRequest", "Friend_Id", "Friend_FullName");
            HardenRelationshipColumns(migrationBuilder, "Friendship", "Friend_Id", "Friend_FullName");
            HardenRelationshipColumns(
                migrationBuilder,
                "CategoryFollow",
                "Followed_Id",
                "Followed_Name",
                relatedNameLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "SubscriptionId",
                table: "FriendshipSubscriptionFeature",
                type: "varchar(191)",
                maxLength: 191,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "UserRelationAnalytics",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation(
                    "MySql:ValueGenerationStrategy",
                    Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn);

            foreach (var table in new[]
            {
                "CategoryFollow",
                "Follow",
                "FriendRequest",
                "Friendship",
                "FriendshipSubscriptionFeature",
                "UserRelationAnalytics"
            })
            {
                migrationBuilder.AlterColumn<string>(
                    name: "UserUpdate",
                    table: table,
                    type: "varchar(10)",
                    unicode: false,
                    maxLength: 10,
                    nullable: false,
                    defaultValue: "SISTEMA",
                    oldClrType: typeof(string),
                    oldType: "varchar(10)",
                    oldUnicode: false,
                    oldMaxLength: 10,
                    oldNullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4")
                    .OldAnnotation("MySql:CharSet", "utf8mb4");
            }
        }

        private static void HardenRelationshipColumns(
            MigrationBuilder migrationBuilder,
            string table,
            string relatedIdColumn,
            string relatedNameColumn,
            int relatedNameLength = 200)
        {
            migrationBuilder.AlterColumn<int>(
                name: "User_Id",
                table: table,
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.AlterColumn<int>(
                name: relatedIdColumn,
                table: table,
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "User_FullName",
                table: table,
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<string>(
                name: relatedNameColumn,
                table: table,
                type: $"varchar({relatedNameLength})",
                maxLength: relatedNameLength,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        private static void AddCanonicalPairColumns(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PairMinUserId",
                table: "FriendRequest",
                type: "int",
                nullable: false,
                computedColumnSql: "LEAST(`User_Id`, `Friend_Id`)",
                stored: true);
            migrationBuilder.AddColumn<int>(
                name: "PairMaxUserId",
                table: "FriendRequest",
                type: "int",
                nullable: false,
                computedColumnSql: "GREATEST(`User_Id`, `Friend_Id`)",
                stored: true);
            migrationBuilder.AddColumn<int>(
                name: "ActivePairMinUserId",
                table: "FriendRequest",
                type: "int",
                nullable: true,
                computedColumnSql:
                    "CASE WHEN `Status` = 0 THEN LEAST(`User_Id`, `Friend_Id`) ELSE NULL END",
                stored: true);
            migrationBuilder.AddColumn<int>(
                name: "ActivePairMaxUserId",
                table: "FriendRequest",
                type: "int",
                nullable: true,
                computedColumnSql:
                    "CASE WHEN `Status` = 0 THEN GREATEST(`User_Id`, `Friend_Id`) ELSE NULL END",
                stored: true);
            migrationBuilder.AddColumn<int>(
                name: "PairMinUserId",
                table: "Friendship",
                type: "int",
                nullable: false,
                computedColumnSql: "LEAST(`User_Id`, `Friend_Id`)",
                stored: true);
            migrationBuilder.AddColumn<int>(
                name: "PairMaxUserId",
                table: "Friendship",
                type: "int",
                nullable: false,
                computedColumnSql: "GREATEST(`User_Id`, `Friend_Id`)",
                stored: true);
        }

        private static void AddConstraintsAndIndexes(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                "CK_CategoryFollow_PositiveIds",
                "CategoryFollow",
                "`User_Id` > 0 AND `Followed_Id` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_Follow_PositiveUsers",
                "Follow",
                "`User_Id` > 0 AND `Followed_Id` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_Follow_NoSelf",
                "Follow",
                "`User_Id` <> `Followed_Id`");
            migrationBuilder.AddCheckConstraint(
                "CK_FriendRequest_PositiveUsers",
                "FriendRequest",
                "`User_Id` > 0 AND `Friend_Id` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_FriendRequest_NoSelf",
                "FriendRequest",
                "`User_Id` <> `Friend_Id`");
            migrationBuilder.AddCheckConstraint(
                "CK_FriendRequest_Status",
                "FriendRequest",
                "`Status` BETWEEN 0 AND 3");
            migrationBuilder.AddCheckConstraint(
                "CK_Friendship_PositiveUsers",
                "Friendship",
                "`User_Id` > 0 AND `Friend_Id` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_Friendship_NoSelf",
                "Friendship",
                "`User_Id` <> `Friend_Id`");
            migrationBuilder.AddCheckConstraint(
                "CK_SubscriptionFeature_PositiveUsers",
                "FriendshipSubscriptionFeature",
                "`OwnerId` > 0 AND `RequesterId` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_SubscriptionFeature_NoSelf",
                "FriendshipSubscriptionFeature",
                "`OwnerId` <> `RequesterId`");
            migrationBuilder.AddCheckConstraint(
                "CK_UserRelationAnalytics_PositiveId",
                "UserRelationAnalytics",
                "`Id` > 0");
            migrationBuilder.AddCheckConstraint(
                "CK_UserRelationAnalytics_NonNegative",
                "UserRelationAnalytics",
                "`TotalFriends` >= 0 AND `TotalFollowers` >= 0 " +
                "AND `TotalFollowing` >= 0 AND `TotalCategoriesFollowed` >= 0 " +
                "AND `TotalFriendsRequests` >= 0");

            migrationBuilder.CreateIndex(
                "UX_CategoryFollow_User_Category",
                "CategoryFollow",
                new[] { "User_Id", "Followed_Id" },
                unique: true);
            migrationBuilder.CreateIndex(
                "IX_CategoryFollow_User_Cursor",
                "CategoryFollow",
                new[] { "User_Id", "CreatedDate", "Id" },
                descending: new[] { false, true, true });
            migrationBuilder.CreateIndex(
                "IX_CategoryFollow_Category_User",
                "CategoryFollow",
                new[] { "Followed_Id", "User_Id" });

            migrationBuilder.CreateIndex(
                "UX_Follow_Direction",
                "Follow",
                new[] { "User_Id", "Followed_Id" },
                unique: true);
            migrationBuilder.CreateIndex(
                "IX_Follow_Follower_Cursor",
                "Follow",
                new[] { "User_Id", "CreatedDate", "Id" },
                descending: new[] { false, true, true });
            migrationBuilder.CreateIndex(
                "IX_Follow_Followed_Cursor",
                "Follow",
                new[] { "Followed_Id", "CreatedDate", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                "UX_FriendRequest_ActivePair",
                "FriendRequest",
                new[] { "ActivePairMinUserId", "ActivePairMaxUserId" },
                unique: true);
            migrationBuilder.CreateIndex(
                "IX_FriendRequest_Pair_Status",
                "FriendRequest",
                new[] { "PairMinUserId", "PairMaxUserId", "Status" });
            migrationBuilder.CreateIndex(
                "IX_FriendRequest_Sender_Status_Cursor",
                "FriendRequest",
                new[] { "User_Id", "Status", "CreatedDate", "Id" },
                descending: new[] { false, false, true, true });
            migrationBuilder.CreateIndex(
                "IX_FriendRequest_Recipient_Status_Cursor",
                "FriendRequest",
                new[] { "Friend_Id", "Status", "CreatedDate", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                "UX_Friendship_Direction",
                "Friendship",
                new[] { "User_Id", "Friend_Id" },
                unique: true);
            migrationBuilder.CreateIndex(
                "IX_Friendship_NormalizedPair",
                "Friendship",
                new[] { "PairMinUserId", "PairMaxUserId" });
            migrationBuilder.CreateIndex(
                "IX_Friendship_User_Cursor",
                "Friendship",
                new[] { "User_Id", "CreatedDate", "Id" },
                descending: new[] { false, true, true });
            migrationBuilder.CreateIndex(
                "IX_Friendship_Reverse_Cursor",
                "Friendship",
                new[] { "Friend_Id", "CreatedDate", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                "UX_SubscriptionFeature_Owner_Requester_Subscription",
                "FriendshipSubscriptionFeature",
                new[] { "OwnerId", "RequesterId", "SubscriptionId" },
                unique: true);
            migrationBuilder.CreateIndex(
                "IX_SubscriptionFeature_Expiry",
                "FriendshipSubscriptionFeature",
                new[] { "EndDate", "Id" });
        }

        private static void AddVersionTriggers(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
            {
                "CategoryFollow",
                "Follow",
                "FriendRequest",
                "Friendship",
                "FriendshipSubscriptionFeature",
                "UserRelationAnalytics"
            })
            {
                migrationBuilder.Sql(
                    $"CREATE TRIGGER `TR_{table}_Version` BEFORE UPDATE ON `{table}` " +
                    "FOR EACH ROW SET NEW.`Version` = OLD.`Version` + 1, " +
                    "NEW.`UpdateDate` = UTC_TIMESTAMP(6);");
            }
        }

        private static void DropVersionTriggers(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[]
            {
                "CategoryFollow",
                "Follow",
                "FriendRequest",
                "Friendship",
                "FriendshipSubscriptionFeature",
                "UserRelationAnalytics"
            })
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_Version`;");
            }
        }

        private static void RelaxColumns(MigrationBuilder migrationBuilder)
        {
            RelaxRelationshipColumns(migrationBuilder, "Follow", "Followed_Id", "Followed_FullName");
            RelaxRelationshipColumns(migrationBuilder, "FriendRequest", "Friend_Id", "Friend_FullName");
            RelaxRelationshipColumns(migrationBuilder, "Friendship", "Friend_Id", "Friend_FullName");
            RelaxRelationshipColumns(migrationBuilder, "CategoryFollow", "Followed_Id", "Followed_Name", 128);

            migrationBuilder.AlterColumn<string>(
                name: "SubscriptionId",
                table: "FriendshipSubscriptionFeature",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(191)",
                oldMaxLength: 191)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "UserRelationAnalytics",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation(
                    "MySql:ValueGenerationStrategy",
                    Microsoft.EntityFrameworkCore.Metadata.MySqlValueGenerationStrategy.IdentityColumn);

            foreach (var table in new[]
            {
                "CategoryFollow",
                "Follow",
                "FriendRequest",
                "Friendship",
                "FriendshipSubscriptionFeature",
                "UserRelationAnalytics"
            })
            {
                migrationBuilder.AlterColumn<string>(
                    name: "UserUpdate",
                    table: table,
                    type: "varchar(10)",
                    unicode: false,
                    maxLength: 10,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "varchar(10)",
                    oldUnicode: false,
                    oldMaxLength: 10,
                    oldDefaultValue: "SISTEMA")
                    .Annotation("MySql:CharSet", "utf8mb4")
                    .OldAnnotation("MySql:CharSet", "utf8mb4");
            }
        }

        private static void RelaxRelationshipColumns(
            MigrationBuilder migrationBuilder,
            string table,
            string relatedIdColumn,
            string relatedNameColumn,
            int relatedNameLength = 200)
        {
            migrationBuilder.AlterColumn<int>(
                name: "User_Id",
                table: table,
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
            migrationBuilder.AlterColumn<int>(
                name: relatedIdColumn,
                table: table,
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
            migrationBuilder.AlterColumn<string>(
                name: "User_FullName",
                table: table,
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<string>(
                name: relatedNameColumn,
                table: table,
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: $"varchar({relatedNameLength})",
                oldMaxLength: relatedNameLength,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
