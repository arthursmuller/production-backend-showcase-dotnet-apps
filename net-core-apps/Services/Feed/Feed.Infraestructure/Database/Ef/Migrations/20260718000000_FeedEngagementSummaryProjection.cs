using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class FeedEngagementSummaryProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedEngagementSummary",
                columns: table => new
                {
                    ResourceType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    DislikeCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingTotal = table.Column<long>(type: "bigint", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false),
                    ShareCount = table.Column<long>(type: "bigint", nullable: false),
                    LastAppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestObservedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GapFromVersion = table.Column<long>(type: "bigint", nullable: true),
                    GapToVersion = table.Column<long>(type: "bigint", nullable: true),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LastSourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedEngagementSummary", x => new { x.ResourceType, x.ResourceId });
                    table.CheckConstraint("CK_FeedEngagementSummary_Counts_NonNegative", "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
                    table.CheckConstraint("CK_FeedEngagementSummary_RatingScale", "(`RatingCount` = 0 AND `RatingTotal` = 0) OR (`RatingCount` > 0 AND `RatingTotal` >= `RatingCount` AND `RatingTotal` <= `RatingCount` * 6)");
                    table.CheckConstraint("CK_FeedEngagementSummary_ResourceType", "`ResourceType` IN ('comment', 'post')");
                    table.CheckConstraint("CK_FeedEngagementSummary_Status", "`Status` IN ('uninitialized', 'healthy', 'gap', 'quarantined')");
                    table.CheckConstraint("CK_FeedEngagementSummary_Versions", "`LastAppliedVersion` >= 0 AND `HighestObservedVersion` >= `LastAppliedVersion` AND `Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedEngagementSummaryEventQuarantine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ResourceType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResourceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LikeCount = table.Column<long>(type: "bigint", nullable: false),
                    DislikeCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingCount = table.Column<long>(type: "bigint", nullable: false),
                    RatingTotal = table.Column<long>(type: "bigint", nullable: false),
                    ViewCount = table.Column<long>(type: "bigint", nullable: false),
                    CommentCount = table.Column<long>(type: "bigint", nullable: false),
                    ShareCount = table.Column<long>(type: "bigint", nullable: false),
                    SummaryVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Resolution = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedEngagementSummaryEventQuarantine", x => x.Id);
                    table.CheckConstraint("CK_FeedEngagementSummaryEventQuarantine_Counts_NonNegative", "`LikeCount` >= 0 AND `DislikeCount` >= 0 AND `RatingCount` >= 0 AND `RatingTotal` >= 0 AND `ViewCount` >= 0 AND `CommentCount` >= 0 AND `ShareCount` >= 0");
                    table.CheckConstraint("CK_FeedEngagementSummaryEventQuarantine_RatingScale", "(`RatingCount` = 0 AND `RatingTotal` = 0) OR (`RatingCount` > 0 AND `RatingTotal` >= `RatingCount` AND `RatingTotal` <= `RatingCount` * 6)");
                    table.CheckConstraint("CK_FeedEngagementSummaryEventQuarantine_ResourceType", "`ResourceType` IN ('comment', 'post')");
                    table.CheckConstraint("CK_FeedEngagementSummaryEventQuarantine_Versions", "`SummaryVersion` > 0 AND `ExpectedVersion` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FeedEngagementSummary_Status_Updated",
                table: "FeedEngagementSummary",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_FeedEngagementSummary_LastEventId",
                table: "FeedEngagementSummary",
                column: "LastEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedEngagementSummaryEventQuarantine_Drain",
                table: "FeedEngagementSummaryEventQuarantine",
                columns: new[] { "ResourceType", "ResourceId", "ResolvedAtUtc", "SummaryVersion" });

            migrationBuilder.CreateIndex(
                name: "UX_FeedEngagementSummaryEventQuarantine_EventId",
                table: "FeedEngagementSummaryEventQuarantine",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedEngagementSummary");

            migrationBuilder.DropTable(
                name: "FeedEngagementSummaryEventQuarantine");
        }
    }
}
