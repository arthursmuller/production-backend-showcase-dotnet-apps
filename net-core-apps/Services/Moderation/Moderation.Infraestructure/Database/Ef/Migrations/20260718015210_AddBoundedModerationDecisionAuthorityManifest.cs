using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddBoundedModerationDecisionAuthorityManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ModerationDecisionStateRevisions_ModerationCases_CaseId",
                table: "ModerationDecisionStateRevisions");

            migrationBuilder.Sql(
                """
                CREATE TABLE `ModerationDecisionAuthorityCoordinates` (
                    `ExportCursor` BIGINT NOT NULL AUTO_INCREMENT,
                    `CaseId` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                    `CreatedAtUtc` DATETIME(6) NOT NULL DEFAULT UTC_TIMESTAMP(6),
                    CONSTRAINT `PK_ModerationDecisionAuthorityCoordinates` PRIMARY KEY (`ExportCursor`),
                    CONSTRAINT `UX_ModerationDecisionAuthorityCoordinates_Case` UNIQUE (`CaseId`)
                );
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO `ModerationDecisionAuthorityCoordinates`
                    (`CaseId`, `CreatedAtUtc`)
                SELECT `CaseId`, `DecidedAtUtc`
                FROM `ModerationDecisionStates`
                ORDER BY `ExportCursor`;
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE `ModerationDecisionAuthorityCurrentRows` (
                    `CaseId` CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                    `RevisionCursor` BIGINT NOT NULL,
                    `RecordedAtUtc` DATETIME(6) NOT NULL,
                    `DecisionVersion` BIGINT NOT NULL,
                    `CaseVersion` BIGINT NOT NULL,
                    `PartitionKey` TINYINT UNSIGNED NOT NULL,
                    `RowSha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                    CONSTRAINT `PK_ModerationDecisionAuthorityCurrentRows` PRIMARY KEY (`CaseId`),
                    CONSTRAINT `UX_ModerationDecisionAuthorityCurrentRows_Revision` UNIQUE (`RevisionCursor`),
                    INDEX `IX_ModerationDecisionAuthorityCurrentRows_DecisionVersion` (`DecisionVersion`, `CaseId`),
                    INDEX `IX_ModerationDecisionAuthorityCurrentRows_CaseVersion` (`CaseVersion`, `CaseId`),
                    CONSTRAINT `CK_ModerationDecisionAuthorityCurrentRows_Hash` CHECK (CHAR_LENGTH(`RowSha256`) = 64)
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE `ModerationDecisionRevisionPruneCandidates` (
                    `RevisionCursor` BIGINT NOT NULL,
                    `RecordedAtUtc` DATETIME(6) NOT NULL,
                    CONSTRAINT `PK_ModerationDecisionRevisionPruneCandidates` PRIMARY KEY (`RevisionCursor`),
                    INDEX `IX_ModerationDecisionRevisionPruneCandidates_Retention` (`RecordedAtUtc`, `RevisionCursor`)
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE `ModerationDecisionAuthorityPartitionManifests` (
                    `PartitionKey` TINYINT UNSIGNED NOT NULL,
                    `ItemCount` BIGINT NOT NULL DEFAULT 0,
                    `XorSha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000',
                    CONSTRAINT `PK_ModerationDecisionAuthorityPartitionManifests` PRIMARY KEY (`PartitionKey`),
                    CONSTRAINT `CK_ModerationDecisionAuthorityPartitionManifests_Count` CHECK (`ItemCount` >= 0),
                    CONSTRAINT `CK_ModerationDecisionAuthorityPartitionManifests_Hash` CHECK (CHAR_LENGTH(`XorSha256`) = 64)
                );
                """);
            migrationBuilder.Sql(SeedPartitions("ModerationDecisionAuthorityPartitionManifests"));
            migrationBuilder.Sql(
                """
                INSERT INTO `ModerationDecisionAuthorityCurrentRows`
                    (`CaseId`, `RevisionCursor`, `RecordedAtUtc`, `DecisionVersion`, `CaseVersion`,
                     `PartitionKey`, `RowSha256`)
                SELECT `r`.`CaseId`, `r`.`RevisionCursor`, `r`.`RecordedAtUtc`, `r`.`DecisionVersion`,
                       `r`.`CaseVersion`, `r`.`PartitionKey`, `r`.`RowSha256`
                FROM `ModerationDecisionStateRevisions` AS `r`
                INNER JOIN (
                    SELECT `CaseId`, MAX(`RevisionCursor`) AS `RevisionCursor`
                    FROM `ModerationDecisionStateRevisions`
                    GROUP BY `CaseId`
                ) AS `latest` ON `latest`.`RevisionCursor` = `r`.`RevisionCursor`;
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO `ModerationDecisionRevisionPruneCandidates`
                    (`RevisionCursor`, `RecordedAtUtc`)
                SELECT `r`.`RevisionCursor`, `r`.`RecordedAtUtc`
                FROM `ModerationDecisionStateRevisions` AS `r`
                INNER JOIN (
                    SELECT `CaseId`, MAX(`RevisionCursor`) AS `RevisionCursor`
                    FROM `ModerationDecisionStateRevisions`
                    GROUP BY `CaseId`
                ) AS `latest` ON `latest`.`CaseId` = `r`.`CaseId`
                WHERE `r`.`RevisionCursor` < `latest`.`RevisionCursor`;
                """);
            migrationBuilder.Sql(
                """
                UPDATE `ModerationDecisionAuthorityPartitionManifests` AS `m`
                INNER JOIN (
                    SELECT `PartitionKey`, COUNT(*) AS `ItemCount`,
                           CONCAT(
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 1, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 17, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 33, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 49, 16), 16, 10) AS UNSIGNED))), 16, '0')) AS `XorSha256`
                    FROM `ModerationDecisionAuthorityCurrentRows`
                    GROUP BY `PartitionKey`
                ) AS `source` ON `source`.`PartitionKey` = `m`.`PartitionKey`
                SET `m`.`ItemCount` = `source`.`ItemCount`,
                    `m`.`XorSha256` = LOWER(`source`.`XorSha256`);
                """);
            migrationBuilder.Sql(MaintainManifestTrigger());
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_ModerationDecisionStates_MaintainAuthorityCoordinate`
                AFTER INSERT ON `ModerationDecisionStates`
                FOR EACH ROW
                INSERT INTO `ModerationDecisionAuthorityCoordinates`
                    (`CaseId`, `CreatedAtUtc`)
                VALUES (NEW.`CaseId`, NEW.`DecidedAtUtc`);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionStates_MaintainAuthorityCoordinate`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ModerationDecisionRevisions_MaintainManifest`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ModerationDecisionRevisionPruneCandidates`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ModerationDecisionAuthorityPartitionManifests`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ModerationDecisionAuthorityCurrentRows`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `ModerationDecisionAuthorityCoordinates`;");

            migrationBuilder.AddForeignKey(
                name: "FK_ModerationDecisionStateRevisions_ModerationCases_CaseId",
                table: "ModerationDecisionStateRevisions",
                column: "CaseId",
                principalTable: "ModerationCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        private static string SeedPartitions(string table) =>
            $"INSERT INTO `{table}` (`PartitionKey`) VALUES "
            + string.Join(",", Enumerable.Range(0, 256).Select(value => $"({value})"))
            + ";";

        private static string MaintainManifestTrigger()
        {
            var xor = XorHashes("`m`.`XorSha256`", "COALESCE(`c`.`RowSha256`, REPEAT('0', 64))", "NEW.`RowSha256`");
            return $$"""
                CREATE TRIGGER `TR_ModerationDecisionRevisions_MaintainManifest`
                AFTER INSERT ON `ModerationDecisionStateRevisions`
                FOR EACH ROW
                BEGIN
                    INSERT IGNORE INTO `ModerationDecisionRevisionPruneCandidates`
                        (`RevisionCursor`, `RecordedAtUtc`)
                    SELECT `c`.`RevisionCursor`, `c`.`RecordedAtUtc`
                    FROM `ModerationDecisionAuthorityCurrentRows` AS `c`
                    WHERE `c`.`CaseId` = NEW.`CaseId`;

                    UPDATE `ModerationDecisionAuthorityPartitionManifests` AS `m`
                    LEFT JOIN `ModerationDecisionAuthorityCurrentRows` AS `c`
                      ON `c`.`CaseId` = NEW.`CaseId`
                    SET `m`.`ItemCount` = `m`.`ItemCount` + IF(`c`.`RevisionCursor` IS NULL, 1, 0),
                        `m`.`XorSha256` = {{xor}}
                    WHERE `m`.`PartitionKey` = NEW.`PartitionKey`;

                    INSERT INTO `ModerationDecisionAuthorityCurrentRows`
                        (`CaseId`, `RevisionCursor`, `RecordedAtUtc`, `DecisionVersion`, `CaseVersion`,
                         `PartitionKey`, `RowSha256`)
                    VALUES
                        (NEW.`CaseId`, NEW.`RevisionCursor`, NEW.`RecordedAtUtc`, NEW.`DecisionVersion`,
                         NEW.`CaseVersion`, NEW.`PartitionKey`, NEW.`RowSha256`)
                    ON DUPLICATE KEY UPDATE
                        `RevisionCursor` = VALUES(`RevisionCursor`),
                        `RecordedAtUtc` = VALUES(`RecordedAtUtc`),
                        `DecisionVersion` = VALUES(`DecisionVersion`),
                        `CaseVersion` = VALUES(`CaseVersion`),
                        `PartitionKey` = VALUES(`PartitionKey`),
                        `RowSha256` = VALUES(`RowSha256`);
                END;
                """;
        }

        private static string XorHashes(string first, string second, string third) =>
            "CONCAT(" + string.Join(",", new[] { 1, 17, 33, 49 }.Select(offset =>
                $"LPAD(HEX(CAST(CONV(SUBSTRING({first}, {offset}, 16), 16, 10) AS UNSIGNED) ^ "
                + $"CAST(CONV(SUBSTRING({second}, {offset}, 16), 16, 10) AS UNSIGNED) ^ "
                + $"CAST(CONV(SUBSTRING({third}, {offset}, 16), 16, 10) AS UNSIGNED)), 16, '0')")) + ")";
    }
}
