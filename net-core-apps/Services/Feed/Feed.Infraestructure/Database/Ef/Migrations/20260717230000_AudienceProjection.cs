using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AudienceProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LastEventId",
                table: "FeedAudienceEdge",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceOccurredAtUtc",
                table: "FeedAudienceEdge",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceStreamId",
                table: "FeedAudienceEdge",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "FeedRelationshipCheckpoint",
                columns: table => new
                {
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastAppliedVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    HighestObservedVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GapFromVersion = table.Column<long>(type: "bigint", nullable: true),
                    GapToVersion = table.Column<long>(type: "bigint", nullable: true),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LastSourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastConfirmedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastReconciledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedRelationshipCheckpoint", x => x.StreamId);
                    table.CheckConstraint("CK_FeedRelationshipCheckpoint_Directional", "`OwnerUserId` <> `ViewerUserId`");
                    table.CheckConstraint("CK_FeedRelationshipCheckpoint_Owner", "`OwnerUserId` > 0");
                    table.CheckConstraint("CK_FeedRelationshipCheckpoint_Version", "`Version` > 0");
                    table.CheckConstraint("CK_FeedRelationshipCheckpoint_Versions", "`LastAppliedVersion` >= 0 AND `HighestObservedVersion` >= `LastAppliedVersion`");
                    table.CheckConstraint("CK_FeedRelationshipCheckpoint_Viewer", "`ViewerUserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FeedRelationshipEventQuarantine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedVersion = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_FeedRelationshipEventQuarantine", x => x.Id);
                    table.CheckConstraint("CK_FeedRelationshipEventQuarantine_Versions", "`ReceivedVersion` > 0 AND `ExpectedVersion` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "UX_FeedAudienceEdge_SourceStreamId",
                table: "FeedAudienceEdge",
                column: "SourceStreamId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipCheckpoint_Status_Updated",
                table: "FeedRelationshipCheckpoint",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_FeedRelationshipCheckpoint_Direction_Kind",
                table: "FeedRelationshipCheckpoint",
                columns: new[] { "OwnerUserId", "ViewerUserId", "RelationshipKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedRelationshipEventQuarantine_Resolution_Cursor",
                table: "FeedRelationshipEventQuarantine",
                columns: new[] { "ResolvedAtUtc", "RecordedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_FeedRelationshipEventQuarantine_EventId",
                table: "FeedRelationshipEventQuarantine",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FeedRelationshipEventQuarantine_Stream_Version",
                table: "FeedRelationshipEventQuarantine",
                columns: new[] { "StreamId", "ReceivedVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedRelationshipCheckpoint");

            migrationBuilder.DropTable(
                name: "FeedRelationshipEventQuarantine");

            migrationBuilder.DropIndex(
                name: "UX_FeedAudienceEdge_SourceStreamId",
                table: "FeedAudienceEdge");

            migrationBuilder.DropColumn(
                name: "LastEventId",
                table: "FeedAudienceEdge");

            migrationBuilder.DropColumn(
                name: "SourceOccurredAtUtc",
                table: "FeedAudienceEdge");

            migrationBuilder.DropColumn(
                name: "SourceStreamId",
                table: "FeedAudienceEdge");
        }
    }
}
