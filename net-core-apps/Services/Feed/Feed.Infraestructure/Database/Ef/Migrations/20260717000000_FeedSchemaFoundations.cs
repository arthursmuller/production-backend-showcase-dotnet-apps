using System;
using Feed.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Migrations
{
    [DbContext(typeof(FeedContext))]
    [Migration("20260717000000_FeedSchemaFoundations")]
    public partial class FeedSchemaFoundations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateSafetyArchives(migrationBuilder);
            NormalizeAndDeduplicate(migrationBuilder);

            migrationBuilder.DropForeignKey(
                name: "FK_UserConnectionsFeed_Post_PostId",
                table: "UserConnectionsFeed");
            migrationBuilder.DropIndex(
                name: "IX_UserConnectionsFeed_UserId",
                table: "UserConnectionsFeed");

            migrationBuilder.AlterColumn<string>(
                name: "EntityId",
                table: "Post",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Post",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Post",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<string>(
                name: "UserUpdate",
                table: "Post",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(10)",
                oldUnicode: false,
                oldMaxLength: 10,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Post",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "UserConnectionsFeed",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "UserConnectionsFeed",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<string>(
                name: "EventTrigger",
                table: "UserConnectionsFeed",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "UserConnectionsFeed",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: "UserUpdate",
                table: "UserConnectionsFeed",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(10)",
                oldUnicode: false,
                oldMaxLength: 10,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "UserConnectionsFeed",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Post_UserId_NonNegative",
                table: "Post",
                sql: "`UserId` >= 0");
            migrationBuilder.AddCheckConstraint(
                name: "CK_UserConnectionsFeed_UserId_Positive",
                table: "UserConnectionsFeed",
                sql: "`UserId` > 0");

            migrationBuilder.CreateIndex(
                name: "UX_Post_Type_EntityId",
                table: "Post",
                columns: new[] { "Type", "EntityId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_Post_User_Visibility_Cursor",
                table: "Post",
                columns: new[] { "UserId", "IsPublicViewable", "CreatedDate", "Id" },
                descending: new[] { false, false, true, true });
            migrationBuilder.CreateIndex(
                name: "UX_UserConnectionsFeed_UserId_PostId",
                table: "UserConnectionsFeed",
                columns: new[] { "UserId", "PostId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionsFeed_User_Seen_Cursor",
                table: "UserConnectionsFeed",
                columns: new[] { "UserId", "DateSeen", "CreatedDate", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.AddForeignKey(
                name: "FK_UserConnectionsFeed_Post_PostId",
                table: "UserConnectionsFeed",
                column: "PostId",
                principalTable: "Post",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            CreateTables(migrationBuilder);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FeedEntry");
            migrationBuilder.DropTable(name: "FanoutWorkItem");
            migrationBuilder.DropTable(name: "FeedAudienceEdge");
            migrationBuilder.DropTable(name: "FeedUserState");
            migrationBuilder.DropTable(name: "FeedActivity");

            migrationBuilder.DropForeignKey(
                name: "FK_UserConnectionsFeed_Post_PostId",
                table: "UserConnectionsFeed");
            migrationBuilder.DropIndex(name: "UX_Post_Type_EntityId", table: "Post");
            migrationBuilder.DropIndex(name: "IX_Post_User_Visibility_Cursor", table: "Post");
            migrationBuilder.DropIndex(
                name: "UX_UserConnectionsFeed_UserId_PostId",
                table: "UserConnectionsFeed");
            migrationBuilder.DropIndex(
                name: "IX_UserConnectionsFeed_User_Seen_Cursor",
                table: "UserConnectionsFeed");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Post_UserId_NonNegative",
                table: "Post");
            migrationBuilder.DropCheckConstraint(
                name: "CK_UserConnectionsFeed_UserId_Positive",
                table: "UserConnectionsFeed");
            migrationBuilder.DropColumn(name: "Version", table: "Post");
            migrationBuilder.DropColumn(name: "Version", table: "UserConnectionsFeed");

            migrationBuilder.AlterColumn<string>(
                name: "EntityId",
                table: "Post",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(200)",
                oldMaxLength: 200)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "Post",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Post",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<int>(
                name: "UserId",
                table: "UserConnectionsFeed",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "UserConnectionsFeed",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(500)",
                oldMaxLength: 500)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<string>(
                name: "EventTrigger",
                table: "UserConnectionsFeed",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "UserConnectionsFeed",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_UserConnectionsFeed_UserId",
                table: "UserConnectionsFeed",
                column: "UserId");
            migrationBuilder.AddForeignKey(
                name: "FK_UserConnectionsFeed_Post_PostId",
                table: "UserConnectionsFeed",
                column: "PostId",
                principalTable: "Post",
                principalColumn: "Id");

            // The two safety archives intentionally survive rollback. Reintroducing
            // duplicate active rows after the old binary has resumed would be unsafe.
        }

        private static void CreateSafetyArchives(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS `FeedLegacyPostDedupArchive` (
                    `OriginalId` int NOT NULL,
                    `SurvivorPostId` int NULL,
                    `EntityId` longtext NULL,
                    `UserId` int NULL,
                    `Type` longtext NULL,
                    `IsPublicViewable` tinyint(1) NOT NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdateDate` datetime(6) NOT NULL,
                    `UserUpdate` varchar(64) NULL,
                    `ArchivedReason` varchar(64) NOT NULL,
                    `ArchivedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (`OriginalId`),
                    KEY `IX_FeedLegacyPostArchive_Survivor` (`SurvivorPostId`)
                ) CHARACTER SET=utf8mb4;

                CREATE TABLE IF NOT EXISTS `FeedLegacyEntryDedupArchive` (
                    `OriginalId` int NOT NULL,
                    `SurvivorFeedItemId` int NULL,
                    `OriginalPostId` int NULL,
                    `ReplacementPostId` int NULL,
                    `UserId` int NULL,
                    `Description` longtext NULL,
                    `EventTrigger` longtext NULL,
                    `DateSeen` datetime(6) NULL,
                    `CreatedDate` datetime(6) NOT NULL,
                    `UpdateDate` datetime(6) NOT NULL,
                    `UserUpdate` varchar(64) NULL,
                    `ArchivedReason` varchar(64) NOT NULL,
                    `ArchivedAtUtc` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (`OriginalId`),
                    KEY `IX_FeedLegacyEntryArchive_Survivor` (`SurvivorFeedItemId`)
                ) CHARACTER SET=utf8mb4;
                """);
        }

        private static void NormalizeAndDeduplicate(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TEMPORARY TABLE `tmp_FeedPostDedupMap` (
                    `DuplicateId` int NOT NULL PRIMARY KEY,
                    `SurvivorId` int NOT NULL
                );

                INSERT INTO `tmp_FeedPostDedupMap` (`DuplicateId`, `SurvivorId`)
                SELECT p.`Id`, grouped.`SurvivorId`
                FROM `Post` p
                INNER JOIN (
                    SELECT normalized.`NormalizedType`, normalized.`NormalizedEntityId`, MIN(normalized.`Id`) AS `SurvivorId`
                    FROM (
                        SELECT
                            `Id`,
                            LOWER(LEFT(COALESCE(NULLIF(TRIM(`Type`), ''), 'legacy'), 64)) AS `NormalizedType`,
                            LEFT(
                                CASE
                                    WHEN `EntityId` IS NULL OR TRIM(`EntityId`) = '' THEN CONCAT('legacy-', `Id`)
                                    ELSE TRIM(`EntityId`)
                                END,
                                200) AS `NormalizedEntityId`
                        FROM `Post`
                    ) normalized
                    GROUP BY normalized.`NormalizedType`, normalized.`NormalizedEntityId`
                    HAVING COUNT(*) > 1
                ) grouped
                    ON grouped.`NormalizedType` = LOWER(LEFT(COALESCE(NULLIF(TRIM(p.`Type`), ''), 'legacy'), 64))
                    AND grouped.`NormalizedEntityId` = LEFT(
                        CASE
                            WHEN p.`EntityId` IS NULL OR TRIM(p.`EntityId`) = '' THEN CONCAT('legacy-', p.`Id`)
                            ELSE TRIM(p.`EntityId`)
                        END,
                        200)
                WHERE p.`Id` <> grouped.`SurvivorId`;

                INSERT INTO `FeedLegacyPostDedupArchive` (
                    `OriginalId`, `SurvivorPostId`, `EntityId`, `UserId`, `Type`,
                    `IsPublicViewable`, `CreatedDate`, `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT p.`Id`, map.`SurvivorId`, p.`EntityId`, p.`UserId`, p.`Type`,
                       p.`IsPublicViewable`, p.`CreatedDate`, p.`UpdateDate`, p.`UserUpdate`, 'duplicate-activity'
                FROM `Post` p
                INNER JOIN `tmp_FeedPostDedupMap` map ON map.`DuplicateId` = p.`Id`
                ON DUPLICATE KEY UPDATE
                    `SurvivorPostId` = VALUES(`SurvivorPostId`),
                    `ArchivedReason` = VALUES(`ArchivedReason`);

                INSERT INTO `FeedLegacyEntryDedupArchive` (
                    `OriginalId`, `SurvivorFeedItemId`, `OriginalPostId`, `ReplacementPostId`,
                    `UserId`, `Description`, `EventTrigger`, `DateSeen`, `CreatedDate`,
                    `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT f.`Id`, NULL, f.`PostId`, map.`SurvivorId`, f.`UserId`, f.`Description`,
                       f.`EventTrigger`, f.`DateSeen`, f.`CreatedDate`, f.`UpdateDate`,
                       f.`UserUpdate`, 'duplicate-activity-reference'
                FROM `UserConnectionsFeed` f
                INNER JOIN `tmp_FeedPostDedupMap` map ON map.`DuplicateId` = f.`PostId`
                ON DUPLICATE KEY UPDATE
                    `ReplacementPostId` = VALUES(`ReplacementPostId`),
                    `ArchivedReason` = VALUES(`ArchivedReason`);

                INSERT INTO `FeedLegacyPostDedupArchive` (
                    `OriginalId`, `SurvivorPostId`, `EntityId`, `UserId`, `Type`,
                    `IsPublicViewable`, `CreatedDate`, `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT p.`Id`, p.`Id`, p.`EntityId`, p.`UserId`, p.`Type`,
                       p.`IsPublicViewable`, p.`CreatedDate`, p.`UpdateDate`, p.`UserUpdate`,
                       'legacy-value-normalization'
                FROM `Post` p
                WHERE p.`EntityId` IS NULL
                   OR TRIM(p.`EntityId`) = ''
                   OR CHAR_LENGTH(p.`EntityId`) > 200
                   OR BINARY p.`EntityId` <> BINARY TRIM(p.`EntityId`)
                   OR p.`Type` IS NULL
                   OR TRIM(p.`Type`) = ''
                   OR CHAR_LENGTH(p.`Type`) > 64
                   OR BINARY p.`Type` <> BINARY LOWER(TRIM(p.`Type`))
                   OR p.`UserId` IS NULL
                   OR p.`UserUpdate` IS NULL
                   OR TRIM(p.`UserUpdate`) = ''
                   OR CHAR_LENGTH(p.`UserUpdate`) > 64
                   OR BINARY p.`UserUpdate` <> BINARY TRIM(p.`UserUpdate`)
                ON DUPLICATE KEY UPDATE `OriginalId` = VALUES(`OriginalId`);

                INSERT INTO `FeedLegacyEntryDedupArchive` (
                    `OriginalId`, `SurvivorFeedItemId`, `OriginalPostId`, `ReplacementPostId`,
                    `UserId`, `Description`, `EventTrigger`, `DateSeen`, `CreatedDate`,
                    `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT f.`Id`, f.`Id`, f.`PostId`, f.`PostId`, f.`UserId`, f.`Description`,
                       f.`EventTrigger`, f.`DateSeen`, f.`CreatedDate`, f.`UpdateDate`,
                       f.`UserUpdate`, 'legacy-value-normalization'
                FROM `UserConnectionsFeed` f
                WHERE f.`Description` IS NULL
                   OR CHAR_LENGTH(f.`Description`) > 500
                   OR BINARY f.`Description` <> BINARY TRIM(f.`Description`)
                   OR f.`EventTrigger` IS NULL
                   OR TRIM(f.`EventTrigger`) = ''
                   OR CHAR_LENGTH(f.`EventTrigger`) > 64
                   OR BINARY f.`EventTrigger` <> BINARY LOWER(TRIM(f.`EventTrigger`))
                   OR f.`UserUpdate` IS NULL
                   OR TRIM(f.`UserUpdate`) = ''
                   OR CHAR_LENGTH(f.`UserUpdate`) > 64
                   OR BINARY f.`UserUpdate` <> BINARY TRIM(f.`UserUpdate`)
                ON DUPLICATE KEY UPDATE `OriginalId` = VALUES(`OriginalId`);

                INSERT INTO `FeedLegacyEntryDedupArchive` (
                    `OriginalId`, `SurvivorFeedItemId`, `OriginalPostId`, `ReplacementPostId`,
                    `UserId`, `Description`, `EventTrigger`, `DateSeen`, `CreatedDate`,
                    `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT f.`Id`, NULL, f.`PostId`, NULL, f.`UserId`, f.`Description`,
                       f.`EventTrigger`, f.`DateSeen`, f.`CreatedDate`, f.`UpdateDate`,
                       f.`UserUpdate`, 'invalid-feed-reference'
                FROM `UserConnectionsFeed` f
                WHERE f.`PostId` IS NULL OR f.`UserId` IS NULL OR f.`UserId` <= 0
                ON DUPLICATE KEY UPDATE `ArchivedReason` = VALUES(`ArchivedReason`);

                DELETE FROM `UserConnectionsFeed`
                WHERE `PostId` IS NULL OR `UserId` IS NULL OR `UserId` <= 0;

                UPDATE `Post`
                SET
                    `EntityId` = LEFT(
                        CASE
                            WHEN `EntityId` IS NULL OR TRIM(`EntityId`) = '' THEN CONCAT('legacy-', `Id`)
                            ELSE TRIM(`EntityId`)
                        END,
                        200),
                    `Type` = LOWER(LEFT(COALESCE(NULLIF(TRIM(`Type`), ''), 'legacy'), 64)),
                    `UserId` = COALESCE(`UserId`, 0),
                    `UserUpdate` = LEFT(COALESCE(NULLIF(TRIM(`UserUpdate`), ''), 'SISTEMA'), 64);

                UPDATE `UserConnectionsFeed`
                SET
                    `Description` = LEFT(COALESCE(TRIM(`Description`), ''), 500),
                    `EventTrigger` = LOWER(LEFT(COALESCE(NULLIF(TRIM(`EventTrigger`), ''), 'legacy'), 64)),
                    `UserUpdate` = LEFT(COALESCE(NULLIF(TRIM(`UserUpdate`), ''), 'SISTEMA'), 64);

                UPDATE `UserConnectionsFeed` f
                INNER JOIN `tmp_FeedPostDedupMap` map ON map.`DuplicateId` = f.`PostId`
                SET f.`PostId` = map.`SurvivorId`;

                DELETE p
                FROM `Post` p
                INNER JOIN `tmp_FeedPostDedupMap` map ON map.`DuplicateId` = p.`Id`;

                CREATE TEMPORARY TABLE `tmp_FeedEntryDedupMap` (
                    `DuplicateId` int NOT NULL PRIMARY KEY,
                    `SurvivorId` int NOT NULL
                );

                INSERT INTO `tmp_FeedEntryDedupMap` (`DuplicateId`, `SurvivorId`)
                SELECT f.`Id`, grouped.`SurvivorId`
                FROM `UserConnectionsFeed` f
                INNER JOIN (
                    SELECT `UserId`, `PostId`, MIN(`Id`) AS `SurvivorId`
                    FROM `UserConnectionsFeed`
                    GROUP BY `UserId`, `PostId`
                    HAVING COUNT(*) > 1
                ) grouped
                    ON grouped.`UserId` = f.`UserId` AND grouped.`PostId` = f.`PostId`
                WHERE f.`Id` <> grouped.`SurvivorId`;

                INSERT INTO `FeedLegacyEntryDedupArchive` (
                    `OriginalId`, `SurvivorFeedItemId`, `OriginalPostId`, `ReplacementPostId`,
                    `UserId`, `Description`, `EventTrigger`, `DateSeen`, `CreatedDate`,
                    `UpdateDate`, `UserUpdate`, `ArchivedReason`)
                SELECT f.`Id`, map.`SurvivorId`, f.`PostId`, f.`PostId`, f.`UserId`,
                       f.`Description`, f.`EventTrigger`, f.`DateSeen`, f.`CreatedDate`,
                       f.`UpdateDate`, f.`UserUpdate`, 'duplicate-feed-entry'
                FROM `UserConnectionsFeed` f
                INNER JOIN `tmp_FeedEntryDedupMap` map ON map.`DuplicateId` = f.`Id`
                ON DUPLICATE KEY UPDATE
                    `SurvivorFeedItemId` = VALUES(`SurvivorFeedItemId`),
                    `ReplacementPostId` = VALUES(`ReplacementPostId`),
                    `ArchivedReason` = VALUES(`ArchivedReason`);

                UPDATE `UserConnectionsFeed` keeper
                INNER JOIN (
                    SELECT `UserId`, `PostId`, MIN(`Id`) AS `SurvivorId`,
                           MAX(`DateSeen`) AS `MergedDateSeen`,
                           MIN(`CreatedDate`) AS `OriginalCreatedDate`,
                           MAX(`UpdateDate`) AS `LatestUpdateDate`
                    FROM `UserConnectionsFeed`
                    GROUP BY `UserId`, `PostId`
                    HAVING COUNT(*) > 1
                ) grouped ON grouped.`SurvivorId` = keeper.`Id`
                SET keeper.`DateSeen` = grouped.`MergedDateSeen`,
                    keeper.`CreatedDate` = grouped.`OriginalCreatedDate`,
                    keeper.`UpdateDate` = grouped.`LatestUpdateDate`;

                DELETE f
                FROM `UserConnectionsFeed` f
                INNER JOIN `tmp_FeedEntryDedupMap` map ON map.`DuplicateId` = f.`Id`;

                DROP TEMPORARY TABLE `tmp_FeedEntryDedupMap`;
                DROP TEMPORARY TABLE `tmp_FeedPostDedupMap`;
                """);
        }

        private static void CreateTables(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedActivity",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TargetType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivityKind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    AudiencePolicy = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsTombstoned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TombstonedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedActivity", x => x.Id);
                    table.CheckConstraint("CK_FeedActivity_Actor_Positive", "`ActorUserId` > 0");
                    table.CheckConstraint("CK_FeedActivity_Version_Positive", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedAudienceEdge",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedAudienceEdge", x => x.Id);
                    table.CheckConstraint("CK_FeedAudienceEdge_Directional", "`OwnerUserId` <> `ViewerUserId`");
                    table.CheckConstraint("CK_FeedAudienceEdge_Owner_Positive", "`OwnerUserId` > 0");
                    table.CheckConstraint("CK_FeedAudienceEdge_SourceVersion_Positive", "`SourceVersion` > 0");
                    table.CheckConstraint("CK_FeedAudienceEdge_Version_Positive", "`Version` > 0");
                    table.CheckConstraint("CK_FeedAudienceEdge_Viewer_Positive", "`ViewerUserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedUserState",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FeedRevision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    LastAcknowledgedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompactionState = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompactionCursor = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedUserState", x => x.UserId);
                    table.CheckConstraint("CK_FeedUserState_Revision_NonNegative", "`FeedRevision` >= 0");
                    table.CheckConstraint("CK_FeedUserState_User_Positive", "`UserId` > 0");
                    table.CheckConstraint("CK_FeedUserState_Version_Positive", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedEntry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<long>(type: "bigint", nullable: false),
                    MaterializationMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RankKey = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SeenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedEntry", x => x.Id);
                    table.CheckConstraint("CK_FeedEntry_Recipient_Positive", "`RecipientUserId` > 0");
                    table.CheckConstraint("CK_FeedEntry_Version_Positive", "`Version` > 0");
                    table.ForeignKey(
                        name: "FK_FeedEntry_FeedActivity_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "FeedActivity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FanoutWorkItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceEventId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivityId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextCursor = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FanoutWorkItem", x => x.Id);
                    table.CheckConstraint("CK_FanoutWorkItem_Attempts_NonNegative", "`AttemptCount` >= 0");
                    table.CheckConstraint("CK_FanoutWorkItem_Version_Positive", "`Version` > 0");
                    table.ForeignKey(
                        name: "FK_FanoutWorkItem_FeedActivity_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "FeedActivity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "UX_FeedActivity_Target_Kind",
                table: "FeedActivity",
                columns: new[] { "TargetType", "TargetId", "ActivityKind" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FeedActivity_Target",
                table: "FeedActivity",
                columns: new[] { "TargetType", "TargetId" });
            migrationBuilder.CreateIndex(
                name: "IX_FeedActivity_Actor_Published_Id",
                table: "FeedActivity",
                columns: new[] { "ActorUserId", "PublishedAtUtc", "Id" },
                descending: new[] { false, true, true });
            migrationBuilder.CreateIndex(
                name: "UX_FeedEntry_Recipient_Activity",
                table: "FeedEntry",
                columns: new[] { "RecipientUserId", "ActivityId" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FeedEntry_ActivityId",
                table: "FeedEntry",
                column: "ActivityId");
            migrationBuilder.CreateIndex(
                name: "IX_FeedEntry_Recipient_State_Cursor",
                table: "FeedEntry",
                columns: new[]
                {
                    "RecipientUserId", "RevokedAtUtc", "SeenAtUtc", "RankKey", "PublishedAtUtc", "Id",
                },
                descending: new[] { false, false, false, true, true, true });
            migrationBuilder.CreateIndex(
                name: "UX_FeedAudienceEdge_Direction_Kind",
                table: "FeedAudienceEdge",
                columns: new[] { "OwnerUserId", "ViewerUserId", "RelationshipKind" },
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FeedAudienceEdge_Reverse_State",
                table: "FeedAudienceEdge",
                columns: new[] { "ViewerUserId", "OwnerUserId", "State", "RelationshipKind" });
            migrationBuilder.CreateIndex(
                name: "UX_FanoutWorkItem_SourceEventId",
                table: "FanoutWorkItem",
                column: "SourceEventId",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_FanoutWorkItem_ActivityId",
                table: "FanoutWorkItem",
                column: "ActivityId");
            migrationBuilder.CreateIndex(
                name: "IX_FanoutWorkItem_State_Lease_Id",
                table: "FanoutWorkItem",
                columns: new[] { "State", "LeaseExpiresAtUtc", "Id" });
        }
    }
}
