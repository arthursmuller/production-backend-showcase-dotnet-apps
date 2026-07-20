using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddBoundedInteractionAuthorityManifest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InteractionAuthoritativeStateRevisions_ResourceStates_Resour~",
                table: "InteractionAuthoritativeStateRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_InteractionAuthorityCoordinates_ResourceStates_ResourceType_~",
                table: "InteractionAuthorityCoordinates");

            migrationBuilder.Sql(
                """
                CREATE TABLE `InteractionAuthorityCurrentRows` (
                    `ResourceType` TINYINT UNSIGNED NOT NULL,
                    `ResourceId` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
                    `RevisionCursor` BIGINT NOT NULL,
                    `RecordedAtUtc` DATETIME(6) NOT NULL,
                    `AppliedResourceVersion` BIGINT NOT NULL,
                    `SummaryVersion` BIGINT NULL,
                    `PartitionKey` TINYINT UNSIGNED NOT NULL,
                    `RowSha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
                    CONSTRAINT `PK_InteractionAuthorityCurrentRows` PRIMARY KEY (`ResourceType`, `ResourceId`),
                    CONSTRAINT `UX_InteractionAuthorityCurrentRows_Revision` UNIQUE (`RevisionCursor`),
                    INDEX `IX_InteractionAuthorityCurrentRows_ResourceVersion` (`AppliedResourceVersion`, `ResourceType`, `ResourceId`),
                    INDEX `IX_InteractionAuthorityCurrentRows_SummaryVersion` (`SummaryVersion`, `ResourceType`, `ResourceId`),
                    CONSTRAINT `CK_InteractionAuthorityCurrentRows_Hash` CHECK (CHAR_LENGTH(`RowSha256`) = 64)
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE `InteractionAuthorityRevisionPruneCandidates` (
                    `RevisionCursor` BIGINT NOT NULL,
                    `RecordedAtUtc` DATETIME(6) NOT NULL,
                    CONSTRAINT `PK_InteractionAuthorityRevisionPruneCandidates` PRIMARY KEY (`RevisionCursor`),
                    INDEX `IX_InteractionAuthorityRevisionPruneCandidates_Retention` (`RecordedAtUtc`, `RevisionCursor`)
                );
                """);
            migrationBuilder.Sql(
                """
                CREATE TABLE `InteractionAuthorityPartitionManifests` (
                    `PartitionKey` TINYINT UNSIGNED NOT NULL,
                    `ItemCount` BIGINT NOT NULL DEFAULT 0,
                    `XorSha256` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT '0000000000000000000000000000000000000000000000000000000000000000',
                    CONSTRAINT `PK_InteractionAuthorityPartitionManifests` PRIMARY KEY (`PartitionKey`),
                    CONSTRAINT `CK_InteractionAuthorityPartitionManifests_Count` CHECK (`ItemCount` >= 0),
                    CONSTRAINT `CK_InteractionAuthorityPartitionManifests_Hash` CHECK (CHAR_LENGTH(`XorSha256`) = 64)
                );
                """);
            migrationBuilder.Sql(SeedPartitions("InteractionAuthorityPartitionManifests"));
            migrationBuilder.Sql(
                """
                INSERT INTO `InteractionAuthorityCurrentRows`
                    (`ResourceType`, `ResourceId`, `RevisionCursor`, `RecordedAtUtc`, `AppliedResourceVersion`,
                     `SummaryVersion`, `PartitionKey`, `RowSha256`)
                SELECT `r`.`ResourceType`, `r`.`ResourceId`, `r`.`RevisionCursor`, `r`.`RecordedAtUtc`,
                       `r`.`AppliedResourceVersion`, `r`.`SummaryVersion`,
                       `r`.`PartitionKey`, `r`.`RowSha256`
                FROM `InteractionAuthoritativeStateRevisions` AS `r`
                INNER JOIN (
                    SELECT `ResourceType`, `ResourceId`, MAX(`RevisionCursor`) AS `RevisionCursor`
                    FROM `InteractionAuthoritativeStateRevisions`
                    GROUP BY `ResourceType`, `ResourceId`
                ) AS `latest` ON `latest`.`RevisionCursor` = `r`.`RevisionCursor`;
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO `InteractionAuthorityRevisionPruneCandidates`
                    (`RevisionCursor`, `RecordedAtUtc`)
                SELECT `r`.`RevisionCursor`, `r`.`RecordedAtUtc`
                FROM `InteractionAuthoritativeStateRevisions` AS `r`
                INNER JOIN (
                    SELECT `ResourceType`, `ResourceId`, MAX(`RevisionCursor`) AS `RevisionCursor`
                    FROM `InteractionAuthoritativeStateRevisions`
                    GROUP BY `ResourceType`, `ResourceId`
                ) AS `latest`
                  ON `latest`.`ResourceType` = `r`.`ResourceType`
                 AND `latest`.`ResourceId` = `r`.`ResourceId`
                WHERE `r`.`RevisionCursor` < `latest`.`RevisionCursor`;
                """);
            migrationBuilder.Sql(
                """
                UPDATE `InteractionAuthorityPartitionManifests` AS `m`
                INNER JOIN (
                    SELECT `PartitionKey`, COUNT(*) AS `ItemCount`,
                           CONCAT(
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 1, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 17, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 33, 16), 16, 10) AS UNSIGNED))), 16, '0'),
                               LPAD(HEX(BIT_XOR(CAST(CONV(SUBSTRING(`RowSha256`, 49, 16), 16, 10) AS UNSIGNED))), 16, '0')) AS `XorSha256`
                    FROM `InteractionAuthorityCurrentRows`
                    GROUP BY `PartitionKey`
                ) AS `source` ON `source`.`PartitionKey` = `m`.`PartitionKey`
                SET `m`.`ItemCount` = `source`.`ItemCount`,
                    `m`.`XorSha256` = LOWER(`source`.`XorSha256`);
                """);
            migrationBuilder.Sql(MaintainManifestTrigger());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_InteractionAuthorityRevisions_MaintainManifest`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `InteractionAuthorityRevisionPruneCandidates`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `InteractionAuthorityPartitionManifests`;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS `InteractionAuthorityCurrentRows`;");

            migrationBuilder.AddForeignKey(
                name: "FK_InteractionAuthoritativeStateRevisions_ResourceStates_Resour~",
                table: "InteractionAuthoritativeStateRevisions",
                columns: new[] { "ResourceType", "ResourceId" },
                principalTable: "ResourceStates",
                principalColumns: new[] { "ResourceType", "ResourceId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InteractionAuthorityCoordinates_ResourceStates_ResourceType_~",
                table: "InteractionAuthorityCoordinates",
                columns: new[] { "ResourceType", "ResourceId" },
                principalTable: "ResourceStates",
                principalColumns: new[] { "ResourceType", "ResourceId" },
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
                CREATE TRIGGER `TR_InteractionAuthorityRevisions_MaintainManifest`
                AFTER INSERT ON `InteractionAuthoritativeStateRevisions`
                FOR EACH ROW
                BEGIN
                    INSERT IGNORE INTO `InteractionAuthorityRevisionPruneCandidates`
                        (`RevisionCursor`, `RecordedAtUtc`)
                    SELECT `c`.`RevisionCursor`, `c`.`RecordedAtUtc`
                    FROM `InteractionAuthorityCurrentRows` AS `c`
                    WHERE `c`.`ResourceType` = NEW.`ResourceType`
                      AND `c`.`ResourceId` = NEW.`ResourceId`;

                    UPDATE `InteractionAuthorityPartitionManifests` AS `m`
                    LEFT JOIN `InteractionAuthorityCurrentRows` AS `c`
                      ON `c`.`ResourceType` = NEW.`ResourceType`
                     AND `c`.`ResourceId` = NEW.`ResourceId`
                    SET `m`.`ItemCount` = `m`.`ItemCount` + IF(`c`.`RevisionCursor` IS NULL, 1, 0),
                        `m`.`XorSha256` = {{xor}}
                    WHERE `m`.`PartitionKey` = NEW.`PartitionKey`;

                    INSERT INTO `InteractionAuthorityCurrentRows`
                        (`ResourceType`, `ResourceId`, `RevisionCursor`, `RecordedAtUtc`, `AppliedResourceVersion`,
                         `SummaryVersion`, `PartitionKey`, `RowSha256`)
                    VALUES
                        (NEW.`ResourceType`, NEW.`ResourceId`, NEW.`RevisionCursor`, NEW.`RecordedAtUtc`,
                         NEW.`AppliedResourceVersion`, NEW.`SummaryVersion`,
                         NEW.`PartitionKey`, NEW.`RowSha256`)
                    ON DUPLICATE KEY UPDATE
                        `RevisionCursor` = VALUES(`RevisionCursor`),
                        `RecordedAtUtc` = VALUES(`RecordedAtUtc`),
                        `AppliedResourceVersion` = VALUES(`AppliedResourceVersion`),
                        `SummaryVersion` = VALUES(`SummaryVersion`),
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
