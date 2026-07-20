using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentAudienceProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentAudienceProjectionState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CycleId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CycleInProgress = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NextCursor = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHighWatermark = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotAsOfUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CycleStartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseToken = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentAudienceProjectionState", x => x.Id);
                    table.CheckConstraint("CK_CmtAudienceProjection_Singleton", "`Id` = 1");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentRelationshipCheckpoint",
                columns: table => new
                {
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastAppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    HighestObservedVersion = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GapFromVersion = table.Column<long>(type: "bigint", nullable: true),
                    GapToVersion = table.Column<long>(type: "bigint", nullable: true),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LastSourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastConfirmedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LastReconciledAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRelationshipCheckpoint", x => x.StreamId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentRelationshipEdge",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceStreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    SourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRelationshipEdge", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentRelationshipEventQuarantine",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    EventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<string>(type: "varchar(24)", unicode: false, maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReceivedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Resolution = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRelationshipEventQuarantine", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CmtAudienceProjection_Health",
                table: "CommentAudienceProjectionState",
                columns: new[] { "Status", "LastCompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtRelCheckpoint_Health",
                table: "CommentRelationshipCheckpoint",
                columns: new[] { "Status", "LastConfirmedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtRelCheckpoint_Identity",
                table: "CommentRelationshipCheckpoint",
                columns: new[] { "OwnerUserId", "ViewerUserId", "RelationshipKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmtRelEdge_Audience",
                table: "CommentRelationshipEdge",
                columns: new[] { "OwnerUserId", "RelationshipKind", "State", "ViewerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtRelEdge_ReverseAudience",
                table: "CommentRelationshipEdge",
                columns: new[] { "ViewerUserId", "RelationshipKind", "State", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtRelEdge_Identity",
                table: "CommentRelationshipEdge",
                columns: new[] { "OwnerUserId", "ViewerUserId", "RelationshipKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmtRelQuarantine_StreamResolution",
                table: "CommentRelationshipEventQuarantine",
                columns: new[] { "StreamId", "ResolvedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtRelQuarantine_EventId",
                table: "CommentRelationshipEventQuarantine",
                column: "EventId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentAudienceProjectionState");

            migrationBuilder.DropTable(
                name: "CommentRelationshipCheckpoint");

            migrationBuilder.DropTable(
                name: "CommentRelationshipEdge");

            migrationBuilder.DropTable(
                name: "CommentRelationshipEventQuarantine");
        }
    }
}
