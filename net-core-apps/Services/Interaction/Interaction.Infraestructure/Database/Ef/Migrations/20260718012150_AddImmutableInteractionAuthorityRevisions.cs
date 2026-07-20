using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableInteractionAuthorityRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractionAuthoritativeStateRevisions",
                columns: table => new
                {
                    RevisionCursor = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ResourceType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ResourceId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Lifecycle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Visibility = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AppliedResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestSeenResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResourceHasGap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ResourceConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: true),
                    DislikeCount = table.Column<long>(type: "bigint", nullable: true),
                    RatingCount = table.Column<long>(type: "bigint", nullable: true),
                    RatingTotal = table.Column<long>(type: "bigint", nullable: true),
                    ViewCount = table.Column<long>(type: "bigint", nullable: true),
                    CommentCount = table.Column<long>(type: "bigint", nullable: true),
                    ShareCount = table.Column<long>(type: "bigint", nullable: true),
                    ContributionSourceVersion = table.Column<long>(type: "bigint", nullable: true),
                    ContributionHighestSeenVersion = table.Column<long>(type: "bigint", nullable: true),
                    ContributionHasGap = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    HasExternalBaseline = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    IsErased = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    SummaryVersion = table.Column<long>(type: "bigint", nullable: true),
                    SummaryUpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    PartitionKey = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RowSha256 = table.Column<string>(type: "char(64)", unicode: false, nullable: false, collation: "ascii_bin")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionAuthoritativeStateRevisions", x => x.RevisionCursor);
                    table.CheckConstraint("CK_InteractionAuthorityRevision_Hash", "CHAR_LENGTH(`RowSha256`) = 64");
                    table.CheckConstraint("CK_InteractionAuthorityRevision_Partition", "`PartitionKey` BETWEEN 0 AND 255");
                    table.ForeignKey(
                        name: "FK_InteractionAuthoritativeStateRevisions_ResourceStates_Resour~",
                        columns: x => new { x.ResourceType, x.ResourceId },
                        principalTable: "ResourceStates",
                        principalColumns: new[] { "ResourceType", "ResourceId" },
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionAuthorityRevision_Partition_Cursor",
                table: "InteractionAuthoritativeStateRevisions",
                columns: new[] { "PartitionKey", "RevisionCursor" });

            migrationBuilder.CreateIndex(
                name: "IX_InteractionAuthorityRevision_Resource_Cursor",
                table: "InteractionAuthoritativeStateRevisions",
                columns: new[] { "ResourceType", "ResourceId", "RevisionCursor" });

            migrationBuilder.Sql(InsertCurrentRevision(
                "SELECT 1 AS `Seed`",
                "`ResourceStates` AS `r`",
                "LEFT JOIN `EngagementSummaries` AS `s` ON `s`.`ResourceType` = `r`.`ResourceType` AND `s`.`ResourceId` = `r`.`ResourceId`",
                "r",
                "s"));
            migrationBuilder.Sql(ResourceTrigger("TR_ResourceStates_AuthorityRevision_Insert", "INSERT"));
            migrationBuilder.Sql(ResourceTrigger("TR_ResourceStates_AuthorityRevision_Update", "UPDATE"));
            migrationBuilder.Sql(SummaryTrigger("TR_EngagementSummaries_AuthorityRevision_Insert", "INSERT"));
            migrationBuilder.Sql(SummaryTrigger("TR_EngagementSummaries_AuthorityRevision_Update", "UPDATE"));
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_InteractionAuthorityRevisions_RejectUpdate`
                BEFORE UPDATE ON `InteractionAuthoritativeStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Interaction authority revisions are immutable';
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER `TR_InteractionAuthorityRevisions_RejectDelete`
                BEFORE DELETE ON `InteractionAuthoritativeStateRevisions`
                FOR EACH ROW
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'Interaction authority revisions are immutable';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_InteractionAuthorityRevisions_RejectDelete`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_InteractionAuthorityRevisions_RejectUpdate`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_EngagementSummaries_AuthorityRevision_Update`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_EngagementSummaries_AuthorityRevision_Insert`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ResourceStates_AuthorityRevision_Update`;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS `TR_ResourceStates_AuthorityRevision_Insert`;");
            migrationBuilder.DropTable(
                name: "InteractionAuthoritativeStateRevisions");
        }

        private static string ResourceTrigger(string name, string operation) =>
            $"""
            CREATE TRIGGER `{name}`
            AFTER {operation} ON `ResourceStates`
            FOR EACH ROW
            {InsertCurrentRevision(
                "SELECT 1 AS `Seed`",
                "(SELECT 1 AS `Seed`) AS `seed`",
                "LEFT JOIN `EngagementSummaries` AS `s` ON `s`.`ResourceType` = NEW.`ResourceType` AND `s`.`ResourceId` = NEW.`ResourceId`",
                "NEW",
                "s")}
            """;

        private static string SummaryTrigger(string name, string operation) =>
            $"""
            CREATE TRIGGER `{name}`
            AFTER {operation} ON `EngagementSummaries`
            FOR EACH ROW
            {InsertCurrentRevision(
                "SELECT 1 AS `Seed`",
                "`ResourceStates` AS `r`",
                "",
                "r",
                "NEW",
                "WHERE `r`.`ResourceType` = NEW.`ResourceType` AND `r`.`ResourceId` = NEW.`ResourceId`")}
            """;

        private static string InsertCurrentRevision(
            string seed,
            string from,
            string join,
            string resource,
            string summary,
            string where = "") =>
            $"""
            INSERT INTO `InteractionAuthoritativeStateRevisions`
                (`ResourceType`, `ResourceId`, `Lifecycle`, `Visibility`,
                 `AppliedResourceVersion`, `HighestSeenResourceVersion`, `ResourceHasGap`,
                 `ResourceConcurrencyVersion`, `ResourceUpdatedAtUtc`, `LikeCount`, `DislikeCount`,
                 `RatingCount`, `RatingTotal`, `ViewCount`, `CommentCount`, `ShareCount`,
                 `ContributionSourceVersion`, `ContributionHighestSeenVersion`,
                 `ContributionHasGap`, `HasExternalBaseline`, `IsErased`, `SummaryVersion`,
                 `SummaryUpdatedAtUtc`, `PartitionKey`, `RowSha256`)
            SELECT {resource}.`ResourceType`, {resource}.`ResourceId`, {resource}.`Lifecycle`,
                   {resource}.`Visibility`, {resource}.`AppliedVersion`, {resource}.`HighestSeenVersion`,
                   {resource}.`HasGap`, {resource}.`ConcurrencyVersion`, {resource}.`UpdatedAtUtc`,
                   {summary}.`LikeCount`, {summary}.`DislikeCount`, {summary}.`RatingCount`,
                   {summary}.`RatingTotal`, {summary}.`ViewCount`, {summary}.`CommentCount`,
                   {summary}.`ShareCount`, {summary}.`ContributionSourceVersion`,
                   {summary}.`ContributionHighestSeenVersion`, {summary}.`ContributionHasGap`,
                   {summary}.`HasExternalBaseline`, {summary}.`IsErased`, {summary}.`Version`,
                   {summary}.`UpdatedAtUtc`,
                   CAST(CONV(SUBSTRING(SHA2(CONCAT(
                       CAST({resource}.`ResourceType` AS CHAR), ':',
                       CHAR_LENGTH({resource}.`ResourceId`), ':', {resource}.`ResourceId`), 256), 1, 2), 16, 10) AS UNSIGNED),
                   LOWER(SHA2(CONCAT_WS(CHAR(31),
                       'interaction-authority-revision',
                       CAST({resource}.`ResourceType` AS CHAR),
                       CONCAT(CHAR_LENGTH({resource}.`ResourceId`), ':', {resource}.`ResourceId`),
                       CAST({resource}.`Lifecycle` AS CHAR), CAST({resource}.`Visibility` AS CHAR),
                       CAST({resource}.`AppliedVersion` AS CHAR), CAST({resource}.`HighestSeenVersion` AS CHAR),
                       IF({resource}.`HasGap`, '1', '0'), CAST({resource}.`ConcurrencyVersion` AS CHAR),
                       DATE_FORMAT({resource}.`UpdatedAtUtc`, '%Y-%m-%dT%H:%i:%s.%fZ'),
                       IFNULL(CAST({summary}.`LikeCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`DislikeCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`RatingCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`RatingTotal` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`ViewCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`CommentCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`ShareCount` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`ContributionSourceVersion` AS CHAR), '~'),
                       IFNULL(CAST({summary}.`ContributionHighestSeenVersion` AS CHAR), '~'),
                       IF({summary}.`ContributionHasGap` IS NULL, '~', IF({summary}.`ContributionHasGap`, '1', '0')),
                       IF({summary}.`HasExternalBaseline` IS NULL, '~', IF({summary}.`HasExternalBaseline`, '1', '0')),
                       IF({summary}.`IsErased` IS NULL, '~', IF({summary}.`IsErased`, '1', '0')),
                       IFNULL(CAST({summary}.`Version` AS CHAR), '~'),
                       IFNULL(DATE_FORMAT({summary}.`UpdatedAtUtc`, '%Y-%m-%dT%H:%i:%s.%fZ'), '~')), 256))
            FROM {from}
            {join}
            {where};
            """;
    }
}
