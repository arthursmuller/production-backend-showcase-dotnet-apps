using Interaction.Infraestructure.Database.Ef;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations;

[DbContext(typeof(InteractionDbContext))]
[Migration("20260723010000_AgeInteractionAuthorityPruneCandidatesFromSupersession")]
public sealed class AgeInteractionAuthorityPruneCandidatesFromSupersession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MaintainManifestTrigger("UTC_TIMESTAMP(6)"));
        migrationBuilder.Sql(
            """
            UPDATE `InteractionAuthorityRevisionPruneCandidates`
            SET `RecordedAtUtc` = UTC_TIMESTAMP(6);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MaintainManifestTrigger("`c`.`RecordedAtUtc`"));
        migrationBuilder.Sql(
            """
            UPDATE `InteractionAuthorityRevisionPruneCandidates` AS `candidate`
            INNER JOIN `InteractionAuthoritativeStateRevisions` AS `revision`
              ON `revision`.`RevisionCursor` = `candidate`.`RevisionCursor`
            SET `candidate`.`RecordedAtUtc` = `revision`.`RecordedAtUtc`;
            """);
    }

    private static string MaintainManifestTrigger(string candidateTimestampValue)
    {
        var xor = XorHashes(
            "`m`.`XorSha256`",
            "COALESCE(`c`.`RowSha256`, REPEAT('0', 64))",
            "NEW.`RowSha256`");
        return $$"""
            CREATE OR REPLACE TRIGGER `TR_InteractionAuthorityRevisions_MaintainManifest`
            AFTER INSERT ON `InteractionAuthoritativeStateRevisions`
            FOR EACH ROW
            BEGIN
                INSERT IGNORE INTO `InteractionAuthorityRevisionPruneCandidates`
                    (`RevisionCursor`, `RecordedAtUtc`)
                SELECT `c`.`RevisionCursor`, {{candidateTimestampValue}}
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
