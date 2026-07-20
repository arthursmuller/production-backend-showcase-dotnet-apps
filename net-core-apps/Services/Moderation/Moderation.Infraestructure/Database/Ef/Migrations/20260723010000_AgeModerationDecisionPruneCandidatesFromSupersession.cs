using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moderation.Infra.Database.Ef;

#nullable disable

namespace Moderation.Infra.Database.Ef.Migrations;

[DbContext(typeof(ModerationDbContext))]
[Migration("20260723010000_AgeModerationDecisionPruneCandidatesFromSupersession")]
public sealed class AgeModerationDecisionPruneCandidatesFromSupersession : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MaintainManifestTrigger("UTC_TIMESTAMP(6)"));
        migrationBuilder.Sql(
            """
            UPDATE `ModerationDecisionRevisionPruneCandidates`
            SET `RecordedAtUtc` = UTC_TIMESTAMP(6);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MaintainManifestTrigger("`c`.`RecordedAtUtc`"));
        migrationBuilder.Sql(
            """
            UPDATE `ModerationDecisionRevisionPruneCandidates` AS `candidate`
            INNER JOIN `ModerationDecisionStateRevisions` AS `revision`
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
            CREATE OR REPLACE TRIGGER `TR_ModerationDecisionRevisions_MaintainManifest`
            AFTER INSERT ON `ModerationDecisionStateRevisions`
            FOR EACH ROW
            BEGIN
                INSERT IGNORE INTO `ModerationDecisionRevisionPruneCandidates`
                    (`RevisionCursor`, `RecordedAtUtc`)
                SELECT `c`.`RevisionCursor`, {{candidateTimestampValue}}
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
