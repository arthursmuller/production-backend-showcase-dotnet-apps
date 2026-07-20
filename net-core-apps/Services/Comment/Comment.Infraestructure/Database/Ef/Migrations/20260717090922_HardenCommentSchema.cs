using Comment.Infra.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <summary>
    /// Expands the legacy schema with canonical identity/version columns, repairs
    /// legacy conflicts, then installs the constraints used by the hardened model.
    /// Every row removed or truncated by repair is copied to
    /// CommentSchemaRepairArchive first.
    /// </summary>
    [DbContext(typeof(CommentContext))]
    [Migration("20260717090922_HardenCommentSchema")]
    public partial class HardenCommentSchema : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS `CommentSchemaRepairArchive` (
                    `Id` BIGINT NOT NULL AUTO_INCREMENT,
                    `MigrationId` VARCHAR(64) NOT NULL,
                    `EntityType` VARCHAR(64) NOT NULL,
                    `LegacyKey` VARCHAR(128) NOT NULL,
                    `Reason` VARCHAR(64) NOT NULL,
                    `Payload` LONGTEXT NOT NULL,
                    `ArchivedAtUtc` DATETIME(6) NOT NULL,
                    CONSTRAINT `PK_CommentSchemaRepairArchive` PRIMARY KEY (`Id`),
                    CONSTRAINT `UX_CmtRepair_Migration_Entity_Key_Reason`
                        UNIQUE (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`)
                ) CHARACTER SET utf8mb4;

                ALTER TABLE `CommentDislike`
                    DROP FOREIGN KEY `FK_CommentDislike_CommentSocialRating_CommentSocialRatingId`;
                ALTER TABLE `CommentLike`
                    DROP FOREIGN KEY `FK_CommentLike_CommentSocialRating_CommentSocialRatingId`;
                ALTER TABLE `CommentMediaItem`
                    DROP FOREIGN KEY `FK_CommentMediaItem_CommentPost_CommentPostId`;
                ALTER TABLE `CommentPost`
                    DROP FOREIGN KEY `FK_CommentPost_CommentSocialAnalytics_SocialId`;
                ALTER TABLE `CommentPostComment`
                    DROP FOREIGN KEY `FK_CommentPostComment_CommentSocialRating_CommentSocialRatingId`;
                ALTER TABLE `CommentRatingLevel`
                    DROP FOREIGN KEY `FK_CommentRatingLevel_CommentSocialRating_CommentSocialRatingId`,
                    DROP FOREIGN KEY `FK_CommentRatingLevel_RatingLevel__levelId`;
                ALTER TABLE `CommentReport`
                    DROP FOREIGN KEY `FK_CommentReport_CommentSocialRating_CommentSocialRatingId`;
                ALTER TABLE `CommentShare`
                    DROP FOREIGN KEY `FK_CommentShare_CommentSocialRating_CommentSocialRatingId`;
                ALTER TABLE `CommentSocialRating`
                    DROP FOREIGN KEY `FK_CommentSocialRating_CommentSocialAnalytics_AnalyticsId`;

                ALTER TABLE `CommentPost`
                    ADD COLUMN `AuthorUserId` INT NOT NULL DEFAULT 0,
                    ADD COLUMN `TargetId` VARCHAR(256) NOT NULL DEFAULT '',
                    ADD COLUMN `TargetType` VARCHAR(64) NOT NULL DEFAULT '',
                    ADD COLUMN `VisibilityLevelId` INT NOT NULL DEFAULT 4,
                    ADD COLUMN `Version` BIGINT NOT NULL DEFAULT 1;
                ALTER TABLE `CommentSocialRating`
                    ADD COLUMN `Version` BIGINT NOT NULL DEFAULT 1;
                ALTER TABLE `CommentSocialAnalytics`
                    ADD COLUMN `Version` BIGINT NOT NULL DEFAULT 1;
                ALTER TABLE `CommentLike`
                    ADD COLUMN `ActorUserId` INT NOT NULL DEFAULT 0;
                ALTER TABLE `CommentDislike`
                    ADD COLUMN `ActorUserId` INT NOT NULL DEFAULT 0;
                ALTER TABLE `CommentRatingLevel`
                    ADD COLUMN `ActorUserId` INT NOT NULL DEFAULT 0;
                ALTER TABLE `CommentReport`
                    ADD COLUMN `ActorUserId` INT NOT NULL DEFAULT 0;
                """);

            migrationBuilder.Sql("""
                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT
                    '20260717090922', 'CommentPost', CAST(`Id` AS CHAR), 'canonical-value-repair',
                    JSON_OBJECT(
                        'UserId', `User_Id`,
                        'TargetId', `IdOfRelatedEntity`,
                        'TargetType', `TypeOfRelatedEntity`,
                        'VisibilityId', `VisibilityLevel_Id`,
                        'Description', `Description`,
                        'GifUrl', `GifUrl`,
                        'UserFullName', `User_FullName`),
                    UTC_TIMESTAMP(6)
                FROM `CommentPost`
                WHERE COALESCE(`User_Id`, 0) <= 0
                   OR NULLIF(TRIM(`IdOfRelatedEntity`), '') IS NULL
                   OR NULLIF(TRIM(`TypeOfRelatedEntity`), '') IS NULL
                   OR `VisibilityLevel_Id` NOT IN (1, 2, 3, 4)
                   OR CHAR_LENGTH(COALESCE(`IdOfRelatedEntity`, '')) > 256
                   OR CHAR_LENGTH(COALESCE(`TypeOfRelatedEntity`, '')) > 64
                   OR CHAR_LENGTH(COALESCE(`Description`, '')) > 10000
                   OR CHAR_LENGTH(COALESCE(`GifUrl`, '')) > 2048
                   OR CHAR_LENGTH(COALESCE(`User_FullName`, '')) > 200;

                UPDATE `CommentPost`
                SET
                    `AuthorUserId` = COALESCE(NULLIF(`User_Id`, 0), 0),
                    `TargetId` = CASE
                        WHEN NULLIF(TRIM(`IdOfRelatedEntity`), '') IS NULL
                            THEN CONCAT('legacy-comment-', `Id`)
                        ELSE LEFT(TRIM(`IdOfRelatedEntity`), 256)
                    END,
                    `TargetType` = CASE
                        WHEN NULLIF(TRIM(`TypeOfRelatedEntity`), '') IS NULL
                            THEN 'legacy-unknown'
                        ELSE LOWER(LEFT(TRIM(`TypeOfRelatedEntity`), 64))
                    END,
                    `VisibilityLevelId` = CASE
                        WHEN COALESCE(`User_Id`, 0) > 0
                         AND NULLIF(TRIM(`IdOfRelatedEntity`), '') IS NOT NULL
                         AND NULLIF(TRIM(`TypeOfRelatedEntity`), '') IS NOT NULL
                         AND `VisibilityLevel_Id` IN (1, 2, 3, 4)
                            THEN `VisibilityLevel_Id`
                        ELSE 4
                    END,
                    `IdOfRelatedEntity` = CASE
                        WHEN NULLIF(TRIM(`IdOfRelatedEntity`), '') IS NULL
                            THEN CONCAT('legacy-comment-', `Id`)
                        ELSE LEFT(TRIM(`IdOfRelatedEntity`), 256)
                    END,
                    `TypeOfRelatedEntity` = CASE
                        WHEN NULLIF(TRIM(`TypeOfRelatedEntity`), '') IS NULL
                            THEN 'legacy-unknown'
                        ELSE LOWER(LEFT(TRIM(`TypeOfRelatedEntity`), 64))
                    END,
                    `User_Id` = COALESCE(NULLIF(`User_Id`, 0), 0),
                    `User_FullName` = LEFT(COALESCE(`User_FullName`, ''), 200),
                    `VisibilityLevel_Id` = CASE
                        WHEN `VisibilityLevel_Id` IN (1, 2, 3, 4) THEN `VisibilityLevel_Id`
                        ELSE 4
                    END,
                    `VisibilityLevel_Name` = CASE
                        WHEN `VisibilityLevel_Id` = 1 THEN 'global'
                        WHEN `VisibilityLevel_Id` = 2 THEN 'friends'
                        WHEN `VisibilityLevel_Id` = 3 THEN 'friendsoffriends'
                        ELSE 'private'
                    END,
                    `Description` = LEFT(`Description`, 10000),
                    `GifUrl` = LEFT(`GifUrl`, 2048),
                    `Version` = 1;

                -- Keep the legacy target columns synchronized for rollback and
                -- old readers while the canonical columns become authoritative.
                UPDATE `CommentPost`
                SET `IdOfRelatedEntity` = `TargetId`,
                    `TypeOfRelatedEntity` = `TargetType`,
                    `VisibilityLevel_Id` = `VisibilityLevelId`;
                """);

            migrationBuilder.Sql("""
                -- Give every comment a distinct, valid analytics row without
                -- guessing ownership or discarding a comment.
                ALTER TABLE `CommentSocialAnalytics`
                    ADD COLUMN `SchemaRepairCommentId` INT NULL;

                INSERT INTO `CommentSocialAnalytics`
                    (`CreatedDate`, `UpdateDate`, `UserUpdate`, `TotalViews`,
                     `TotalRatings`, `TotalReports`, `TotalLikes`, `TotalDislikes`,
                     `TotalShares`, `TotalComments`, `RatingsTrueCount`,
                     `AverageRating`, `Version`, `SchemaRepairCommentId`)
                SELECT
                    `p`.`CreatedDate`, `p`.`UpdateDate`, 'SCHEMA', 0,
                    0, 0, 0, 0, 0, 0, 0, 0, 1, `p`.`Id`
                FROM `CommentPost` AS `p`
                LEFT JOIN `CommentSocialAnalytics` AS `a` ON `a`.`Id` = `p`.`SocialId`
                WHERE `a`.`Id` IS NULL
                   OR EXISTS (
                        SELECT 1
                        FROM `CommentPost` AS `earlier`
                        WHERE `earlier`.`SocialId` = `p`.`SocialId`
                          AND `earlier`.`Id` < `p`.`Id`);

                UPDATE `CommentPost` AS `p`
                INNER JOIN `CommentSocialAnalytics` AS `a`
                    ON `a`.`SchemaRepairCommentId` = `p`.`Id`
                SET `p`.`SocialId` = `a`.`Id`;

                ALTER TABLE `CommentSocialAnalytics`
                    DROP COLUMN `SchemaRepairCommentId`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT
                    '20260717090922', 'CommentSocialRating', CAST(`r`.`Id` AS CHAR),
                    'orphan-rating',
                    JSON_OBJECT('CommentId', `r`.`CommentId`, 'AnalyticsId', `r`.`AnalyticsId`),
                    UTC_TIMESTAMP(6)
                FROM `CommentSocialRating` AS `r`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentDislike', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentDislike` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL
                UNION ALL
                SELECT '20260717090922', 'CommentLike', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentLike` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL
                UNION ALL
                SELECT '20260717090922', 'CommentPostComment', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`, 'UserId', `c`.`UserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentPostComment` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL
                UNION ALL
                SELECT '20260717090922', 'CommentRatingLevel', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentRatingLevel` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL
                UNION ALL
                SELECT '20260717090922', 'CommentReport', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`, 'Reason', `c`.`Reason`),
                       UTC_TIMESTAMP(6)
                FROM `CommentReport` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL
                UNION ALL
                SELECT '20260717090922', 'CommentShare', CAST(`c`.`Id` AS CHAR),
                       'orphan-parent-rating',
                       JSON_OBJECT('RatingId', `c`.`CommentSocialRatingId`, 'PostId', `c`.`PostId`, 'UserId', `c`.`UserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentShare` AS `c`
                INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                WHERE `p`.`Id` IS NULL;

                DELETE `c` FROM `CommentDislike` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `c` FROM `CommentLike` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `c` FROM `CommentPostComment` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `c` FROM `CommentRatingLevel` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `c` FROM `CommentReport` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `c` FROM `CommentShare` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;
                DELETE `r` FROM `CommentSocialRating` AS `r`
                    LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                    WHERE `p`.`Id` IS NULL;

                CREATE TEMPORARY TABLE `_CmtRatingKeep` AS
                    SELECT `CommentId`, MIN(`Id`) AS `KeepId`
                    FROM `CommentSocialRating`
                    GROUP BY `CommentId`;
                ALTER TABLE `_CmtRatingKeep` ADD PRIMARY KEY (`CommentId`);

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT
                    '20260717090922', 'CommentSocialRating', CAST(`r`.`Id` AS CHAR),
                    'duplicate-comment-rating',
                    JSON_OBJECT('CommentId', `r`.`CommentId`, 'KeptId', `k`.`KeepId`),
                    UTC_TIMESTAMP(6)
                FROM `CommentSocialRating` AS `r`
                INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                WHERE `r`.`Id` <> `k`.`KeepId`;

                UPDATE `CommentDislike` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;
                UPDATE `CommentLike` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;
                UPDATE `CommentPostComment` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;
                UPDATE `CommentRatingLevel` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;
                UPDATE `CommentReport` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;
                UPDATE `CommentShare` AS `c`
                    INNER JOIN `CommentSocialRating` AS `r` ON `r`.`Id` = `c`.`CommentSocialRatingId`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    SET `c`.`CommentSocialRatingId` = `k`.`KeepId`, `c`.`PostId` = `r`.`CommentId`;

                DELETE `r` FROM `CommentSocialRating` AS `r`
                    INNER JOIN `_CmtRatingKeep` AS `k` ON `k`.`CommentId` = `r`.`CommentId`
                    WHERE `r`.`Id` <> `k`.`KeepId`;
                DROP TEMPORARY TABLE `_CmtRatingKeep`;

                INSERT INTO `CommentSocialRating`
                    (`CommentId`, `CreatedDate`, `UpdateDate`, `UserUpdate`, `AnalyticsId`, `Version`)
                SELECT `p`.`Id`, `p`.`CreatedDate`, `p`.`UpdateDate`, 'SCHEMA', `p`.`SocialId`, 1
                FROM `CommentPost` AS `p`
                LEFT JOIN `CommentSocialRating` AS `r` ON `r`.`CommentId` = `p`.`Id`
                WHERE `r`.`Id` IS NULL;

                UPDATE `CommentSocialRating` AS `r`
                INNER JOIN `CommentPost` AS `p` ON `p`.`Id` = `r`.`CommentId`
                SET `r`.`AnalyticsId` = `p`.`SocialId`, `r`.`Version` = 1;
                """);

            migrationBuilder.Sql("""
                UPDATE `CommentLike`
                    SET `ActorUserId` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_Id` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_FullName` = COALESCE(`User_FullName`, '');
                UPDATE `CommentDislike`
                    SET `ActorUserId` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_Id` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_FullName` = COALESCE(`User_FullName`, '');
                UPDATE `CommentRatingLevel`
                    SET `ActorUserId` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_Id` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_FullName` = COALESCE(`User_FullName`, '');
                UPDATE `CommentReport`
                    SET `ActorUserId` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_Id` = COALESCE(NULLIF(`User_Id`, 0), -`Id`),
                        `User_FullName` = COALESCE(`User_FullName`, ''),
                        `Reason` = COALESCE(NULLIF(TRIM(`Reason`), ''), 'legacy-unspecified');
                UPDATE `CommentPostComment`
                    SET `UserId` = CASE WHEN `UserId` > 0 THEN `UserId` ELSE -`Id` END;
                UPDATE `CommentShare`
                    SET `UserId` = CASE WHEN `UserId` > 0 THEN `UserId` ELSE -`Id` END;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentLike', CAST(`Id` AS CHAR),
                       'bounded-field-repair', JSON_OBJECT('UserFullName', `User_FullName`), UTC_TIMESTAMP(6)
                FROM `CommentLike` WHERE CHAR_LENGTH(COALESCE(`User_FullName`, '')) > 200
                UNION ALL
                SELECT '20260717090922', 'CommentDislike', CAST(`Id` AS CHAR),
                       'bounded-field-repair', JSON_OBJECT('UserFullName', `User_FullName`), UTC_TIMESTAMP(6)
                FROM `CommentDislike` WHERE CHAR_LENGTH(COALESCE(`User_FullName`, '')) > 200
                UNION ALL
                SELECT '20260717090922', 'CommentRatingLevel', CAST(`Id` AS CHAR),
                       'bounded-field-repair',
                       JSON_OBJECT('UserFullName', `User_FullName`, 'Comment', `Comment`), UTC_TIMESTAMP(6)
                FROM `CommentRatingLevel`
                WHERE CHAR_LENGTH(COALESCE(`User_FullName`, '')) > 200
                   OR CHAR_LENGTH(COALESCE(`Comment`, '')) > 2000
                UNION ALL
                SELECT '20260717090922', 'CommentReport', CAST(`Id` AS CHAR),
                       'bounded-field-repair',
                       JSON_OBJECT('UserFullName', `User_FullName`, 'Reason', `Reason`, 'Reply', `Reply`), UTC_TIMESTAMP(6)
                FROM `CommentReport`
                WHERE CHAR_LENGTH(COALESCE(`User_FullName`, '')) > 200
                   OR CHAR_LENGTH(COALESCE(`Reason`, '')) > 2000
                   OR CHAR_LENGTH(COALESCE(`Reply`, '')) > 4000;

                UPDATE `CommentLike`
                    SET `User_FullName` = LEFT(`User_FullName`, 200);
                UPDATE `CommentDislike`
                    SET `User_FullName` = LEFT(`User_FullName`, 200);
                UPDATE `CommentRatingLevel`
                    SET `User_FullName` = LEFT(`User_FullName`, 200),
                        `Comment` = LEFT(`Comment`, 2000);
                UPDATE `CommentReport`
                    SET `User_FullName` = LEFT(`User_FullName`, 200),
                        `Reason` = LEFT(`Reason`, 2000),
                        `Reply` = LEFT(`Reply`, 4000);

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentLike', CAST(`newer`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `newer`.`PostId`, 'ActorUserId', `newer`.`ActorUserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentLike` AS `newer`
                INNER JOIN `CommentLike` AS `keeper`
                    ON `keeper`.`PostId` = `newer`.`PostId`
                   AND `keeper`.`ActorUserId` = `newer`.`ActorUserId`
                   AND `keeper`.`Id` < `newer`.`Id`;
                DELETE `newer` FROM `CommentLike` AS `newer`
                INNER JOIN `CommentLike` AS `keeper`
                    ON `keeper`.`PostId` = `newer`.`PostId`
                   AND `keeper`.`ActorUserId` = `newer`.`ActorUserId`
                   AND `keeper`.`Id` < `newer`.`Id`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentDislike', CAST(`newer`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `newer`.`PostId`, 'ActorUserId', `newer`.`ActorUserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentDislike` AS `newer`
                INNER JOIN `CommentDislike` AS `keeper`
                    ON `keeper`.`PostId` = `newer`.`PostId`
                   AND `keeper`.`ActorUserId` = `newer`.`ActorUserId`
                   AND `keeper`.`Id` < `newer`.`Id`;
                DELETE `newer` FROM `CommentDislike` AS `newer`
                INNER JOIN `CommentDislike` AS `keeper`
                    ON `keeper`.`PostId` = `newer`.`PostId`
                   AND `keeper`.`ActorUserId` = `newer`.`ActorUserId`
                   AND `keeper`.`Id` < `newer`.`Id`;

                CREATE TEMPORARY TABLE `_CmtReactionLoser` (
                    `EntityType` VARCHAR(16) NOT NULL,
                    `Id` INT NOT NULL,
                    PRIMARY KEY (`EntityType`, `Id`)
                );
                INSERT INTO `_CmtReactionLoser` (`EntityType`, `Id`)
                SELECT 'like', `l`.`Id`
                FROM `CommentLike` AS `l`
                INNER JOIN `CommentDislike` AS `d`
                    ON `d`.`PostId` = `l`.`PostId`
                   AND `d`.`ActorUserId` = `l`.`ActorUserId`
                WHERE `d`.`CreatedDate` > `l`.`CreatedDate`
                   OR (`d`.`CreatedDate` = `l`.`CreatedDate` AND `d`.`Id` > `l`.`Id`)
                UNION ALL
                SELECT 'dislike', `d`.`Id`
                FROM `CommentLike` AS `l`
                INNER JOIN `CommentDislike` AS `d`
                    ON `d`.`PostId` = `l`.`PostId`
                   AND `d`.`ActorUserId` = `l`.`ActorUserId`
                WHERE `l`.`CreatedDate` > `d`.`CreatedDate`
                   OR (`l`.`CreatedDate` = `d`.`CreatedDate` AND `l`.`Id` >= `d`.`Id`);

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentReaction',
                       CONCAT(`loser`.`EntityType`, ':', `loser`.`Id`),
                       'mutually-exclusive-reaction',
                       JSON_OBJECT('Kind', `loser`.`EntityType`, 'Id', `loser`.`Id`),
                       UTC_TIMESTAMP(6)
                FROM `_CmtReactionLoser` AS `loser`;
                DELETE `l` FROM `CommentLike` AS `l`
                    INNER JOIN `_CmtReactionLoser` AS `x`
                        ON `x`.`EntityType` = 'like' AND `x`.`Id` = `l`.`Id`;
                DELETE `d` FROM `CommentDislike` AS `d`
                    INNER JOIN `_CmtReactionLoser` AS `x`
                        ON `x`.`EntityType` = 'dislike' AND `x`.`Id` = `d`.`Id`;
                DROP TEMPORARY TABLE `_CmtReactionLoser`;
                """);

            migrationBuilder.Sql("""
                -- Deterministically retain the lowest legacy row for every
                -- remaining actor/target invariant.
                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentRatingLevel', CAST(`n`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `n`.`PostId`, 'ActorUserId', `n`.`ActorUserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentRatingLevel` AS `n`
                INNER JOIN `CommentRatingLevel` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`ActorUserId` = `n`.`ActorUserId`
                   AND `k`.`Id` < `n`.`Id`;
                DELETE `n` FROM `CommentRatingLevel` AS `n`
                INNER JOIN `CommentRatingLevel` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`ActorUserId` = `n`.`ActorUserId`
                   AND `k`.`Id` < `n`.`Id`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentReport', CAST(`n`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `n`.`PostId`, 'ActorUserId', `n`.`ActorUserId`,
                                   'Reason', `n`.`Reason`, 'Reply', `n`.`Reply`),
                       UTC_TIMESTAMP(6)
                FROM `CommentReport` AS `n`
                INNER JOIN `CommentReport` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`ActorUserId` = `n`.`ActorUserId`
                   AND `k`.`Id` < `n`.`Id`;
                DELETE `n` FROM `CommentReport` AS `n`
                INNER JOIN `CommentReport` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`ActorUserId` = `n`.`ActorUserId`
                   AND `k`.`Id` < `n`.`Id`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentPostComment', CAST(`n`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `n`.`PostId`, 'UserId', `n`.`UserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentPostComment` AS `n`
                INNER JOIN `CommentPostComment` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`UserId` = `n`.`UserId`
                   AND `k`.`Id` < `n`.`Id`
                UNION ALL
                SELECT '20260717090922', 'CommentShare', CAST(`n`.`Id` AS CHAR),
                       'duplicate-actor-target',
                       JSON_OBJECT('PostId', `n`.`PostId`, 'UserId', `n`.`UserId`),
                       UTC_TIMESTAMP(6)
                FROM `CommentShare` AS `n`
                INNER JOIN `CommentShare` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`UserId` = `n`.`UserId`
                   AND `k`.`Id` < `n`.`Id`;

                DELETE `n` FROM `CommentPostComment` AS `n`
                INNER JOIN `CommentPostComment` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`UserId` = `n`.`UserId`
                   AND `k`.`Id` < `n`.`Id`;
                DELETE `n` FROM `CommentShare` AS `n`
                INNER JOIN `CommentShare` AS `k`
                    ON `k`.`PostId` = `n`.`PostId`
                   AND `k`.`UserId` = `n`.`UserId`
                   AND `k`.`Id` < `n`.`Id`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentMediaItem', CAST(`m`.`Id` AS CHAR),
                       'orphan-media',
                       JSON_OBJECT('CommentPostId', `m`.`CommentPostId`, 'Url', `m`.`Url`),
                       UTC_TIMESTAMP(6)
                FROM `CommentMediaItem` AS `m`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `m`.`CommentPostId`
                WHERE `p`.`Id` IS NULL;
                DELETE `m` FROM `CommentMediaItem` AS `m`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `m`.`CommentPostId`
                WHERE `p`.`Id` IS NULL;

                UPDATE `CommentMediaItem`
                SET `Url` = LEFT(`Url`, 2048),
                    `ThumbnailUrl` = LEFT(`ThumbnailUrl`, 2048),
                    `FileName` = LEFT(`FileName`, 255),
                    `MimeType` = LEFT(COALESCE(NULLIF(TRIM(`MimeType`), ''),
                        CASE `MediaType`
                            WHEN 1 THEN 'image/unknown'
                            WHEN 2 THEN 'video/unknown'
                            WHEN 3 THEN 'audio/unknown'
                            ELSE 'application/octet-stream'
                        END), 100);
                CREATE TEMPORARY TABLE `_CmtMediaOrder` AS
                    SELECT `Id`,
                           ROW_NUMBER() OVER (
                               PARTITION BY `CommentPostId`
                               ORDER BY `Order`, `Id`) - 1 AS `NewOrder`
                    FROM `CommentMediaItem`;
                ALTER TABLE `_CmtMediaOrder` ADD PRIMARY KEY (`Id`);
                UPDATE `CommentMediaItem` AS `m`
                    INNER JOIN `_CmtMediaOrder` AS `o` ON `o`.`Id` = `m`.`Id`
                    SET `m`.`Order` = `o`.`NewOrder`;
                DROP TEMPORARY TABLE `_CmtMediaOrder`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentBookmark', CAST(`b`.`Id` AS CHAR),
                       'orphan-comment',
                       JSON_OBJECT('PostId', `b`.`PostId`, 'UserId', `b`.`UserId`,
                                   'GroupId', `b`.`GroupId`, 'GroupName', `b`.`GroupName`),
                       UTC_TIMESTAMP(6)
                FROM `CommentBookmark` AS `b`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `b`.`PostId`
                WHERE `p`.`Id` IS NULL;
                DELETE `b` FROM `CommentBookmark` AS `b`
                LEFT JOIN `CommentPost` AS `p` ON `p`.`Id` = `b`.`PostId`
                WHERE `p`.`Id` IS NULL;
                UPDATE `CommentBookmark`
                    SET `GroupName` = LEFT(COALESCE(NULLIF(TRIM(`GroupName`), ''),
                        CONCAT('legacy-group-', `GroupId`)), 120);

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CategoryName',
                       CONCAT(`CommentPostId`, ':', `Id`),
                       'invalid-or-duplicate-category',
                       JSON_OBJECT('CommentPostId', `CommentPostId`, 'Name', `Name`),
                       UTC_TIMESTAMP(6)
                FROM `CategoryName` AS `n`
                WHERE NULLIF(TRIM(`n`.`Name`), '') IS NULL
                   OR EXISTS (
                        SELECT 1 FROM `CategoryName` AS `k`
                        WHERE `k`.`CommentPostId` = `n`.`CommentPostId`
                          AND LOWER(LEFT(TRIM(`k`.`Name`), 100)) = LOWER(LEFT(TRIM(`n`.`Name`), 100))
                          AND `k`.`Id` < `n`.`Id`);
                DELETE FROM `CategoryName` WHERE NULLIF(TRIM(`Name`), '') IS NULL;
                UPDATE `CategoryName` SET `Name` = LEFT(TRIM(`Name`), 100);
                DELETE `n` FROM `CategoryName` AS `n`
                INNER JOIN `CategoryName` AS `k`
                    ON `k`.`CommentPostId` = `n`.`CommentPostId`
                   AND LOWER(`k`.`Name`) = LOWER(`n`.`Name`)
                   AND `k`.`Id` < `n`.`Id`;

                INSERT IGNORE INTO `CommentSchemaRepairArchive`
                    (`MigrationId`, `EntityType`, `LegacyKey`, `Reason`, `Payload`, `ArchivedAtUtc`)
                SELECT '20260717090922', 'CommentMentions',
                       CONCAT(`CommentPostId`, ':', `Id`),
                       'invalid-or-duplicate-mention',
                       JSON_OBJECT('CommentPostId', `CommentPostId`, 'UserId', `UserId`, 'UserName', `UserName`),
                       UTC_TIMESTAMP(6)
                FROM `CommentMentions` AS `n`
                WHERE `n`.`UserId` <= 0
                   OR NULLIF(TRIM(`n`.`UserName`), '') IS NULL
                   OR EXISTS (
                        SELECT 1 FROM `CommentMentions` AS `k`
                        WHERE `k`.`CommentPostId` = `n`.`CommentPostId`
                          AND `k`.`UserId` = `n`.`UserId`
                          AND `k`.`Id` < `n`.`Id`);
                DELETE FROM `CommentMentions`
                    WHERE `UserId` <= 0 OR NULLIF(TRIM(`UserName`), '') IS NULL;
                UPDATE `CommentMentions` SET `UserName` = LEFT(TRIM(`UserName`), 50);
                DELETE `n` FROM `CommentMentions` AS `n`
                INNER JOIN `CommentMentions` AS `k`
                    ON `k`.`CommentPostId` = `n`.`CommentPostId`
                   AND `k`.`UserId` = `n`.`UserId`
                   AND `k`.`Id` < `n`.`Id`;
                """);

            migrationBuilder.Sql("""
                -- Reconcile all materialized counters after deduplication so the
                -- concurrency-tracked aggregate starts from database truth.
                UPDATE `CommentSocialAnalytics` AS `a`
                INNER JOIN `CommentPost` AS `p` ON `p`.`SocialId` = `a`.`Id`
                SET
                    `a`.`TotalLikes` = (SELECT COUNT(*) FROM `CommentLike` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`TotalDislikes` = (SELECT COUNT(*) FROM `CommentDislike` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`TotalComments` = (SELECT COUNT(*) FROM `CommentPostComment` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`TotalShares` = (SELECT COUNT(*) FROM `CommentShare` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`TotalReports` = (SELECT COUNT(*) FROM `CommentReport` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`TotalRatings` = (SELECT COUNT(*) FROM `CommentRatingLevel` AS `x` WHERE `x`.`PostId` = `p`.`Id`),
                    `a`.`RatingsTrueCount` = COALESCE((SELECT SUM(`x`.`_levelId`) FROM `CommentRatingLevel` AS `x` WHERE `x`.`PostId` = `p`.`Id`), 0),
                    `a`.`AverageRating` = CASE
                        WHEN (SELECT COUNT(*) FROM `CommentRatingLevel` AS `x` WHERE `x`.`PostId` = `p`.`Id`) = 0 THEN 0
                        ELSE COALESCE((SELECT SUM(`x`.`_levelId`) FROM `CommentRatingLevel` AS `x` WHERE `x`.`PostId` = `p`.`Id`), 0)
                             / (SELECT COUNT(*) FROM `CommentRatingLevel` AS `x` WHERE `x`.`PostId` = `p`.`Id`)
                    END,
                    `a`.`Version` = 1;

                ALTER TABLE `CommentPost`
                    MODIFY COLUMN `User_Id` INT NOT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NOT NULL,
                    MODIFY COLUMN `IdOfRelatedEntity` VARCHAR(256) NOT NULL,
                    MODIFY COLUMN `TypeOfRelatedEntity` VARCHAR(64) NOT NULL,
                    MODIFY COLUMN `Description` VARCHAR(10000) NULL,
                    MODIFY COLUMN `GifUrl` VARCHAR(2048) NULL,
                    MODIFY COLUMN `VisibilityLevel_Id` INT NOT NULL,
                    MODIFY COLUMN `VisibilityLevel_Name` VARCHAR(32) NOT NULL,
                    MODIFY COLUMN `SocialId` INT NOT NULL;
                ALTER TABLE `CommentSocialRating`
                    MODIFY COLUMN `AnalyticsId` INT NOT NULL;
                ALTER TABLE `CommentLike`
                    MODIFY COLUMN `User_Id` INT NOT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NOT NULL;
                ALTER TABLE `CommentDislike`
                    MODIFY COLUMN `User_Id` INT NOT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NOT NULL;
                ALTER TABLE `CommentRatingLevel`
                    MODIFY COLUMN `User_Id` INT NOT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NOT NULL,
                    MODIFY COLUMN `Comment` VARCHAR(2000) NULL;
                ALTER TABLE `CommentReport`
                    MODIFY COLUMN `User_Id` INT NOT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NOT NULL,
                    MODIFY COLUMN `Reason` VARCHAR(2000) NOT NULL,
                    MODIFY COLUMN `Reply` VARCHAR(4000) NULL;
                ALTER TABLE `CommentMediaItem`
                    MODIFY COLUMN `CommentPostId` INT NOT NULL,
                    MODIFY COLUMN `Url` VARCHAR(2048) NOT NULL,
                    MODIFY COLUMN `ThumbnailUrl` VARCHAR(2048) NULL,
                    MODIFY COLUMN `MimeType` VARCHAR(100) NOT NULL;
                ALTER TABLE `CommentBookmark`
                    MODIFY COLUMN `GroupName` VARCHAR(120) NOT NULL;
                ALTER TABLE `CategoryName`
                    MODIFY COLUMN `Name` VARCHAR(100) NOT NULL;
                ALTER TABLE `CommentMentions`
                    MODIFY COLUMN `UserName` VARCHAR(50) NOT NULL;
                ALTER TABLE `CommentSocialAnalytics`
                    ADD CONSTRAINT `CK_CmtAnalytics_Nonnegative` CHECK (
                        `TotalViews` >= 0 AND `TotalRatings` >= 0 AND `TotalReports` >= 0
                        AND `TotalLikes` >= 0 AND `TotalDislikes` >= 0
                        AND `TotalComments` >= 0 AND `TotalShares` >= 0
                        AND `RatingsTrueCount` >= 0);
                """);

            migrationBuilder.Sql("""
                DROP INDEX `IX_CommentSocialRating_AnalyticsId` ON `CommentSocialRating`;
                DROP INDEX `IX_CommentShare_CommentSocialRatingId` ON `CommentShare`;
                DROP INDEX `IX_CommentReport_CommentSocialRatingId` ON `CommentReport`;
                DROP INDEX `IX_CommentRatingLevel_CommentSocialRatingId` ON `CommentRatingLevel`;
                DROP INDEX `IX_CommentPostComment_CommentSocialRatingId` ON `CommentPostComment`;
                DROP INDEX `IX_CommentPost_SocialId` ON `CommentPost`;
                DROP INDEX `IX_CommentMediaItem_CommentPostId` ON `CommentMediaItem`;
                DROP INDEX `IX_CommentLike_CommentSocialRatingId` ON `CommentLike`;
                DROP INDEX `IX_CommentDislike_CommentSocialRatingId` ON `CommentDislike`;
                DROP INDEX `IX_CommentBookmark_UserId_GroupId_PostId` ON `CommentBookmark`;

                ALTER TABLE `CommentSocialRating`
                    ADD CONSTRAINT `AK_CommentSocialRating_Id_CommentId` UNIQUE (`Id`, `CommentId`);
                CREATE UNIQUE INDEX `UX_CommentSocialRating_AnalyticsId`
                    ON `CommentSocialRating` (`AnalyticsId`);
                CREATE UNIQUE INDEX `UX_CommentSocialRating_CommentId`
                    ON `CommentSocialRating` (`CommentId`);
                CREATE UNIQUE INDEX `UX_CommentPost_SocialId`
                    ON `CommentPost` (`SocialId`);
                CREATE INDEX `IX_CommentPost_Author_Created_Id`
                    ON `CommentPost` (`AuthorUserId`, `CreatedDate` DESC, `Id` DESC);
                CREATE INDEX `IX_CommentPost_Target_Visibility_Created_Id`
                    ON `CommentPost` (`TargetType`, `TargetId`, `VisibilityLevelId`, `CreatedDate` DESC, `Id` DESC);
                CREATE INDEX `IX_CommentPost_Visibility_Created_Id`
                    ON `CommentPost` (`VisibilityLevelId`, `CreatedDate` DESC, `Id` DESC);

                CREATE UNIQUE INDEX `UX_CommentLike_PostId_ActorUserId`
                    ON `CommentLike` (`PostId`, `ActorUserId`);
                CREATE INDEX `IX_CommentLike_CommentSocialRatingId_PostId`
                    ON `CommentLike` (`CommentSocialRatingId`, `PostId`);
                CREATE UNIQUE INDEX `UX_CommentDislike_PostId_ActorUserId`
                    ON `CommentDislike` (`PostId`, `ActorUserId`);
                CREATE INDEX `IX_CommentDislike_CommentSocialRatingId_PostId`
                    ON `CommentDislike` (`CommentSocialRatingId`, `PostId`);
                CREATE UNIQUE INDEX `UX_CommentRatingLevel_PostId_ActorUserId`
                    ON `CommentRatingLevel` (`PostId`, `ActorUserId`);
                CREATE INDEX `IX_CommentRatingLevel_CommentSocialRatingId_PostId`
                    ON `CommentRatingLevel` (`CommentSocialRatingId`, `PostId`);
                CREATE UNIQUE INDEX `UX_CommentReport_PostId_ActorUserId`
                    ON `CommentReport` (`PostId`, `ActorUserId`);
                CREATE INDEX `IX_CommentReport_CommentSocialRatingId_PostId`
                    ON `CommentReport` (`CommentSocialRatingId`, `PostId`);
                CREATE UNIQUE INDEX `UX_CommentPostComment_PostId_UserId`
                    ON `CommentPostComment` (`PostId`, `UserId`);
                CREATE INDEX `IX_CommentPostComment_CommentSocialRatingId_PostId`
                    ON `CommentPostComment` (`CommentSocialRatingId`, `PostId`);
                CREATE UNIQUE INDEX `UX_CommentShare_PostId_UserId`
                    ON `CommentShare` (`PostId`, `UserId`);
                CREATE INDEX `IX_CommentShare_CommentSocialRatingId_PostId`
                    ON `CommentShare` (`CommentSocialRatingId`, `PostId`);

                CREATE UNIQUE INDEX `UX_CommentMediaItem_CommentPostId_Order`
                    ON `CommentMediaItem` (`CommentPostId`, `Order`);
                CREATE UNIQUE INDEX `UX_CategoryName_CommentPostId_Name`
                    ON `CategoryName` (`CommentPostId`, `Name`);
                CREATE UNIQUE INDEX `UX_CommentMentions_CommentPostId_UserId`
                    ON `CommentMentions` (`CommentPostId`, `UserId`);
                CREATE UNIQUE INDEX `UX_CommentBookmark_UserId_GroupId_PostId`
                    ON `CommentBookmark` (`UserId`, `GroupId`, `PostId`);
                CREATE INDEX `IX_CommentBookmark_PostId`
                    ON `CommentBookmark` (`PostId`);

                ALTER TABLE `CommentPost`
                    ADD CONSTRAINT `FK_CmtPost_Analytics`
                        FOREIGN KEY (`SocialId`) REFERENCES `CommentSocialAnalytics` (`Id`) ON DELETE RESTRICT;
                ALTER TABLE `CommentSocialRating`
                    ADD CONSTRAINT `FK_CmtRating_Comment`
                        FOREIGN KEY (`CommentId`) REFERENCES `CommentPost` (`Id`) ON DELETE CASCADE,
                    ADD CONSTRAINT `FK_CmtRating_Analytics`
                        FOREIGN KEY (`AnalyticsId`) REFERENCES `CommentSocialAnalytics` (`Id`) ON DELETE RESTRICT;
                ALTER TABLE `CommentLike`
                    ADD CONSTRAINT `FK_CmtLike_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE;
                ALTER TABLE `CommentDislike`
                    ADD CONSTRAINT `FK_CmtDislike_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE;
                ALTER TABLE `CommentRatingLevel`
                    ADD CONSTRAINT `FK_CmtRatingLevel_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE,
                    ADD CONSTRAINT `FK_CmtRatingLevel_Level`
                        FOREIGN KEY (`_levelId`) REFERENCES `RatingLevel` (`Id`) ON DELETE RESTRICT;
                ALTER TABLE `CommentReport`
                    ADD CONSTRAINT `FK_CmtReport_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE;
                ALTER TABLE `CommentPostComment`
                    ADD CONSTRAINT `FK_CmtComment_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE;
                ALTER TABLE `CommentShare`
                    ADD CONSTRAINT `FK_CmtShare_Rating`
                        FOREIGN KEY (`CommentSocialRatingId`, `PostId`)
                        REFERENCES `CommentSocialRating` (`Id`, `CommentId`) ON DELETE CASCADE;
                ALTER TABLE `CommentMediaItem`
                    ADD CONSTRAINT `FK_CmtMedia_Comment`
                        FOREIGN KEY (`CommentPostId`) REFERENCES `CommentPost` (`Id`) ON DELETE CASCADE;
                ALTER TABLE `CommentBookmark`
                    ADD CONSTRAINT `FK_CmtBookmark_Comment`
                        FOREIGN KEY (`PostId`) REFERENCES `CommentPost` (`Id`) ON DELETE CASCADE;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deduplication is intentionally not undone. The archive remains so an
            // operator can restore a selected legacy row without reintroducing every
            // conflict. The old application schema and relationships are restored.
            migrationBuilder.Sql("""
                ALTER TABLE `CommentBookmark` DROP FOREIGN KEY `FK_CmtBookmark_Comment`;
                ALTER TABLE `CommentMediaItem` DROP FOREIGN KEY `FK_CmtMedia_Comment`;
                ALTER TABLE `CommentShare` DROP FOREIGN KEY `FK_CmtShare_Rating`;
                ALTER TABLE `CommentPostComment` DROP FOREIGN KEY `FK_CmtComment_Rating`;
                ALTER TABLE `CommentReport` DROP FOREIGN KEY `FK_CmtReport_Rating`;
                ALTER TABLE `CommentRatingLevel`
                    DROP FOREIGN KEY `FK_CmtRatingLevel_Rating`,
                    DROP FOREIGN KEY `FK_CmtRatingLevel_Level`;
                ALTER TABLE `CommentDislike` DROP FOREIGN KEY `FK_CmtDislike_Rating`;
                ALTER TABLE `CommentLike` DROP FOREIGN KEY `FK_CmtLike_Rating`;
                ALTER TABLE `CommentSocialRating`
                    DROP FOREIGN KEY `FK_CmtRating_Comment`,
                    DROP FOREIGN KEY `FK_CmtRating_Analytics`;
                ALTER TABLE `CommentPost` DROP FOREIGN KEY `FK_CmtPost_Analytics`;

                DROP INDEX `IX_CommentBookmark_PostId` ON `CommentBookmark`;
                DROP INDEX `UX_CommentBookmark_UserId_GroupId_PostId` ON `CommentBookmark`;
                DROP INDEX `UX_CommentMentions_CommentPostId_UserId` ON `CommentMentions`;
                DROP INDEX `UX_CategoryName_CommentPostId_Name` ON `CategoryName`;
                DROP INDEX `UX_CommentMediaItem_CommentPostId_Order` ON `CommentMediaItem`;
                DROP INDEX `IX_CommentShare_CommentSocialRatingId_PostId` ON `CommentShare`;
                DROP INDEX `UX_CommentShare_PostId_UserId` ON `CommentShare`;
                DROP INDEX `IX_CommentPostComment_CommentSocialRatingId_PostId` ON `CommentPostComment`;
                DROP INDEX `UX_CommentPostComment_PostId_UserId` ON `CommentPostComment`;
                DROP INDEX `IX_CommentReport_CommentSocialRatingId_PostId` ON `CommentReport`;
                DROP INDEX `UX_CommentReport_PostId_ActorUserId` ON `CommentReport`;
                DROP INDEX `IX_CommentRatingLevel_CommentSocialRatingId_PostId` ON `CommentRatingLevel`;
                DROP INDEX `UX_CommentRatingLevel_PostId_ActorUserId` ON `CommentRatingLevel`;
                DROP INDEX `IX_CommentDislike_CommentSocialRatingId_PostId` ON `CommentDislike`;
                DROP INDEX `UX_CommentDislike_PostId_ActorUserId` ON `CommentDislike`;
                DROP INDEX `IX_CommentLike_CommentSocialRatingId_PostId` ON `CommentLike`;
                DROP INDEX `UX_CommentLike_PostId_ActorUserId` ON `CommentLike`;
                DROP INDEX `IX_CommentPost_Visibility_Created_Id` ON `CommentPost`;
                DROP INDEX `IX_CommentPost_Target_Visibility_Created_Id` ON `CommentPost`;
                DROP INDEX `IX_CommentPost_Author_Created_Id` ON `CommentPost`;
                DROP INDEX `UX_CommentPost_SocialId` ON `CommentPost`;
                DROP INDEX `UX_CommentSocialRating_CommentId` ON `CommentSocialRating`;
                DROP INDEX `UX_CommentSocialRating_AnalyticsId` ON `CommentSocialRating`;
                DROP INDEX `AK_CommentSocialRating_Id_CommentId` ON `CommentSocialRating`;
                ALTER TABLE `CommentSocialAnalytics`
                    DROP CONSTRAINT `CK_CmtAnalytics_Nonnegative`;

                CREATE UNIQUE INDEX `IX_CommentBookmark_UserId_GroupId_PostId`
                    ON `CommentBookmark` (`UserId`, `GroupId`, `PostId`);
                CREATE INDEX `IX_CommentDislike_CommentSocialRatingId`
                    ON `CommentDislike` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentLike_CommentSocialRatingId`
                    ON `CommentLike` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentMediaItem_CommentPostId`
                    ON `CommentMediaItem` (`CommentPostId`);
                CREATE INDEX `IX_CommentPost_SocialId`
                    ON `CommentPost` (`SocialId`);
                CREATE INDEX `IX_CommentPostComment_CommentSocialRatingId`
                    ON `CommentPostComment` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentRatingLevel_CommentSocialRatingId`
                    ON `CommentRatingLevel` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentReport_CommentSocialRatingId`
                    ON `CommentReport` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentShare_CommentSocialRatingId`
                    ON `CommentShare` (`CommentSocialRatingId`);
                CREATE INDEX `IX_CommentSocialRating_AnalyticsId`
                    ON `CommentSocialRating` (`AnalyticsId`);

                ALTER TABLE `CommentDislike`
                    ADD CONSTRAINT `FK_CommentDislike_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`);
                ALTER TABLE `CommentLike`
                    ADD CONSTRAINT `FK_CommentLike_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`);
                ALTER TABLE `CommentMediaItem`
                    ADD CONSTRAINT `FK_CommentMediaItem_CommentPost_CommentPostId`
                        FOREIGN KEY (`CommentPostId`) REFERENCES `CommentPost` (`Id`);
                ALTER TABLE `CommentPost`
                    ADD CONSTRAINT `FK_CommentPost_CommentSocialAnalytics_SocialId`
                        FOREIGN KEY (`SocialId`) REFERENCES `CommentSocialAnalytics` (`Id`);
                ALTER TABLE `CommentPostComment`
                    ADD CONSTRAINT `FK_CommentPostComment_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`);
                ALTER TABLE `CommentRatingLevel`
                    ADD CONSTRAINT `FK_CommentRatingLevel_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`),
                    ADD CONSTRAINT `FK_CommentRatingLevel_RatingLevel__levelId`
                        FOREIGN KEY (`_levelId`) REFERENCES `RatingLevel` (`Id`) ON DELETE CASCADE;
                ALTER TABLE `CommentReport`
                    ADD CONSTRAINT `FK_CommentReport_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`);
                ALTER TABLE `CommentShare`
                    ADD CONSTRAINT `FK_CommentShare_CommentSocialRating_CommentSocialRatingId`
                        FOREIGN KEY (`CommentSocialRatingId`) REFERENCES `CommentSocialRating` (`Id`);
                ALTER TABLE `CommentSocialRating`
                    ADD CONSTRAINT `FK_CommentSocialRating_CommentSocialAnalytics_AnalyticsId`
                        FOREIGN KEY (`AnalyticsId`) REFERENCES `CommentSocialAnalytics` (`Id`);

                ALTER TABLE `CommentSocialRating`
                    MODIFY COLUMN `AnalyticsId` INT NULL,
                    DROP COLUMN `Version`;
                ALTER TABLE `CommentSocialAnalytics` DROP COLUMN `Version`;
                ALTER TABLE `CommentLike`
                    MODIFY COLUMN `User_Id` INT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NULL,
                    DROP COLUMN `ActorUserId`;
                ALTER TABLE `CommentDislike`
                    MODIFY COLUMN `User_Id` INT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NULL,
                    DROP COLUMN `ActorUserId`;
                ALTER TABLE `CommentRatingLevel`
                    MODIFY COLUMN `User_Id` INT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NULL,
                    MODIFY COLUMN `Comment` VARCHAR(2000) NULL,
                    DROP COLUMN `ActorUserId`;
                ALTER TABLE `CommentReport`
                    MODIFY COLUMN `User_Id` INT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NULL,
                    MODIFY COLUMN `Reason` VARCHAR(2000) NULL,
                    MODIFY COLUMN `Reply` VARCHAR(4000) NULL,
                    DROP COLUMN `ActorUserId`;
                ALTER TABLE `CommentMediaItem`
                    MODIFY COLUMN `CommentPostId` INT NULL,
                    MODIFY COLUMN `MimeType` VARCHAR(100) NULL;
                ALTER TABLE `CommentBookmark` MODIFY COLUMN `GroupName` VARCHAR(120) NULL;
                ALTER TABLE `CategoryName` MODIFY COLUMN `Name` VARCHAR(100) NULL;
                ALTER TABLE `CommentMentions` MODIFY COLUMN `UserName` VARCHAR(50) NULL;
                ALTER TABLE `CommentPost`
                    MODIFY COLUMN `User_Id` INT NULL,
                    MODIFY COLUMN `User_FullName` VARCHAR(200) NULL,
                    MODIFY COLUMN `IdOfRelatedEntity` VARCHAR(256) NULL,
                    MODIFY COLUMN `TypeOfRelatedEntity` VARCHAR(64) NULL,
                    MODIFY COLUMN `Description` VARCHAR(10000) NULL,
                    MODIFY COLUMN `GifUrl` VARCHAR(2048) NULL,
                    MODIFY COLUMN `VisibilityLevel_Id` INT NULL,
                    MODIFY COLUMN `VisibilityLevel_Name` VARCHAR(32) NULL,
                    MODIFY COLUMN `SocialId` INT NULL,
                    DROP COLUMN `AuthorUserId`,
                    DROP COLUMN `TargetId`,
                    DROP COLUMN `TargetType`,
                    DROP COLUMN `VisibilityLevelId`,
                    DROP COLUMN `Version`;
                """);
        }
    }
}
