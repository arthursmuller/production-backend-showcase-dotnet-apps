using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class FeedDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "FeedActivity",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "push")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "LastEventId",
                table: "FeedActivity",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "LegacyPostId",
                table: "FeedActivity",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RankKey",
                table: "FeedActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceStreamId",
                table: "FeedActivity",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<long>(
                name: "SourceVersion",
                table: "FeedActivity",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "FeedActivity",
                type: "varchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "AudienceHighWatermarkUserId",
                table: "FanoutWorkItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "FanoutWorkItem",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "NextViewerUserId",
                table: "FanoutWorkItem",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublisherUserId",
                table: "FanoutWorkItem",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "WorkKind",
                table: "FanoutWorkItem",
                type: "varchar(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "fanout")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Expand safely over foundation rows written before the canonical stream and
            // publisher columns existed. The data is made valid before unique indexes and
            // positive-value checks are installed.
            migrationBuilder.Sql(
                """
                UPDATE `FeedActivity`
                SET `SourceStreamId` = COALESCE(`SourceStreamId`, UUID()),
                    `LastEventId` = COALESCE(`LastEventId`, UUID()),
                    `SourceVersion` = CASE WHEN `SourceVersion` <= 0 THEN 1 ELSE `SourceVersion` END,
                    `RankKey` = CASE
                        WHEN `RankKey` = 0 THEN
                            TIMESTAMPDIFF(MICROSECOND, '1970-01-01 00:00:00', `PublishedAtUtc`) * 10
                            + 621355968000000000
                        ELSE `RankKey`
                    END;

                UPDATE `FanoutWorkItem` AS work
                INNER JOIN `FeedActivity` AS activity ON activity.`Id` = work.`ActivityId`
                SET work.`PublisherUserId` = activity.`ActorUserId`
                WHERE work.`PublisherUserId` <= 0;
                """);

            migrationBuilder.CreateTable(
                name: "FeedActivityEventQuarantine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StreamVersion = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetId = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivityKind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    AudiencePolicy = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RankKey = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedActivityEventQuarantine", x => x.Id);
                    table.CheckConstraint("CK_FeedActivityEventQuarantine_Version", "`StreamVersion` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedMaintenanceCheckpoint",
                columns: table => new
                {
                    JobName = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    LastCompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMaintenanceCheckpoint", x => x.JobName);
                    table.CheckConstraint("CK_FeedMaintenanceCheckpoint_Cursor", "`Cursor` >= 0");
                    table.CheckConstraint("CK_FeedMaintenanceCheckpoint_Version", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedPublisherState",
                columns: table => new
                {
                    PublisherUserId = table.Column<int>(type: "int", nullable: false),
                    DeliveryMode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AudienceCount = table.Column<long>(type: "bigint", nullable: false),
                    LastEvaluatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedPublisherState", x => x.PublisherUserId);
                    table.CheckConstraint("CK_FeedPublisherState_Audience", "`AudienceCount` >= 0");
                    table.CheckConstraint("CK_FeedPublisherState_Publisher", "`PublisherUserId` > 0");
                    table.CheckConstraint("CK_FeedPublisherState_Version", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedRevocationWorkItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceEventId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RevocationKind = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActivityId = table.Column<long>(type: "bigint", nullable: true),
                    OwnerUserId = table.Column<int>(type: "int", nullable: true),
                    ViewerUserId = table.Column<int>(type: "int", nullable: true),
                    AudiencePolicy = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextEntryId = table.Column<long>(type: "bigint", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseToken = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
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
                    table.PrimaryKey("PK_FeedRevocationWorkItem", x => x.Id);
                    table.CheckConstraint("CK_FeedRevocationWorkItem_Priority", "`Priority` > 0");
                    table.CheckConstraint("CK_FeedRevocationWorkItem_Progress", "`NextEntryId` >= 0");
                    table.CheckConstraint("CK_FeedRevocationWorkItem_Version", "`Version` > 0");
                    table.ForeignKey(
                        name: "FK_FeedRevocationWorkItem_FeedActivity_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "FeedActivity",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FeedActivity_LegacyPostId",
                table: "FeedActivity",
                column: "LegacyPostId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedActivity_Pull_Cursor",
                table: "FeedActivity",
                columns: new[] { "ActorUserId", "IsTombstoned", "DeliveryMode", "RankKey", "PublishedAtUtc", "Id" },
                descending: new[] { false, false, false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "UX_FeedActivity_SourceStreamId",
                table: "FeedActivity",
                column: "SourceStreamId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FeedActivity_SourceVersion_NonNegative",
                table: "FeedActivity",
                sql: "`SourceVersion` >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_FanoutWorkItem_FairClaim",
                table: "FanoutWorkItem",
                columns: new[] { "State", "LeaseExpiresAtUtc", "UpdatedAtUtc", "PublisherUserId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FanoutWorkItem_Publisher_Positive",
                table: "FanoutWorkItem",
                sql: "`PublisherUserId` > 0");

            migrationBuilder.CreateIndex(
                name: "IX_FeedActivityEventQuarantine_Resolution",
                table: "FeedActivityEventQuarantine",
                columns: new[] { "ResolvedAtUtc", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_FeedActivityEventQuarantine_EventId",
                table: "FeedActivityEventQuarantine",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FeedActivityEventQuarantine_Stream_Version",
                table: "FeedActivityEventQuarantine",
                columns: new[] { "StreamId", "StreamVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedPublisherState_Mode_Updated",
                table: "FeedPublisherState",
                columns: new[] { "DeliveryMode", "UpdatedAtUtc", "PublisherUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedRevocationWorkItem_ActivityId",
                table: "FeedRevocationWorkItem",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedRevocationWorkItem_PriorityClaim",
                table: "FeedRevocationWorkItem",
                columns: new[] { "State", "Priority", "LeaseExpiresAtUtc", "UpdatedAtUtc", "Id" },
                descending: new[] { false, true, false, false, false });

            migrationBuilder.CreateIndex(
                name: "UX_FeedRevocationWorkItem_SourceEventId",
                table: "FeedRevocationWorkItem",
                column: "SourceEventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedActivityEventQuarantine");

            migrationBuilder.DropTable(
                name: "FeedMaintenanceCheckpoint");

            migrationBuilder.DropTable(
                name: "FeedPublisherState");

            migrationBuilder.DropTable(
                name: "FeedRevocationWorkItem");

            migrationBuilder.DropIndex(
                name: "IX_FeedActivity_LegacyPostId",
                table: "FeedActivity");

            migrationBuilder.DropIndex(
                name: "IX_FeedActivity_Pull_Cursor",
                table: "FeedActivity");

            migrationBuilder.DropIndex(
                name: "UX_FeedActivity_SourceStreamId",
                table: "FeedActivity");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FeedActivity_SourceVersion_NonNegative",
                table: "FeedActivity");

            migrationBuilder.DropIndex(
                name: "IX_FanoutWorkItem_FairClaim",
                table: "FanoutWorkItem");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FanoutWorkItem_Publisher_Positive",
                table: "FanoutWorkItem");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "LastEventId",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "LegacyPostId",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "RankKey",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "SourceStreamId",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "FeedActivity");

            migrationBuilder.DropColumn(
                name: "AudienceHighWatermarkUserId",
                table: "FanoutWorkItem");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "FanoutWorkItem");

            migrationBuilder.DropColumn(
                name: "NextViewerUserId",
                table: "FanoutWorkItem");

            migrationBuilder.DropColumn(
                name: "PublisherUserId",
                table: "FanoutWorkItem");

            migrationBuilder.DropColumn(
                name: "WorkKind",
                table: "FanoutWorkItem");
        }
    }
}
