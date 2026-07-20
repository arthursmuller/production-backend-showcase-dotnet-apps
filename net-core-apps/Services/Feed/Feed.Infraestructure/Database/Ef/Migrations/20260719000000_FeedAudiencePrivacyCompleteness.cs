using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Feed.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class FeedAudiencePrivacyCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudiencePolicy",
                table: "Post",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "owner")
                .Annotation("MySql:CharSet", "utf8mb4");

            // Preserve the deployed boolean meaning for rows that predate the additive policy.
            // New/missing AddPost payloads are normalized to owner by application code.
            migrationBuilder.Sql(
                "UPDATE `Post` SET `AudiencePolicy` = CASE WHEN `IsPublicViewable` = 1 THEN 'public' ELSE 'owner' END;");

            migrationBuilder.CreateTable(
                name: "FeedAudienceBootstrapState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CycleId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    CycleInProgress = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    NextCursor = table.Column<long>(type: "bigint", nullable: false),
                    SnapshotHighWatermark = table.Column<long>(type: "bigint", nullable: true),
                    SnapshotAsOfUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CycleStartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastPageAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastCompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastErrorCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseToken = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedAudienceBootstrapState", x => x.Id);
                    table.CheckConstraint("CK_FeedAudienceBootstrapState_Cursors", "`NextCursor` >= 0 AND (`SnapshotHighWatermark` IS NULL OR `SnapshotHighWatermark` >= `NextCursor`)");
                    table.CheckConstraint("CK_FeedAudienceBootstrapState_Singleton", "`Id` = 1");
                    table.CheckConstraint("CK_FeedAudienceBootstrapState_Version", "`Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "VisbilityLevels",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "friends_of_friends");

            migrationBuilder.CreateIndex(
                name: "IX_Post_User_Audience_Cursor",
                table: "Post",
                columns: new[] { "UserId", "AudiencePolicy", "CreatedDate", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Post_AudiencePolicy",
                table: "Post",
                sql: "`AudiencePolicy` IN ('public','followers','friends','friends_of_friends','owner')");

            migrationBuilder.CreateIndex(
                name: "IX_FeedAudienceBootstrapState_Health",
                table: "FeedAudienceBootstrapState",
                columns: new[] { "Status", "LastCompletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedAudienceBootstrapState");

            migrationBuilder.DropIndex(
                name: "IX_Post_User_Audience_Cursor",
                table: "Post");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Post_AudiencePolicy",
                table: "Post");

            migrationBuilder.DropColumn(
                name: "AudiencePolicy",
                table: "Post");

            migrationBuilder.UpdateData(
                table: "VisbilityLevels",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "friendsoffriends");
        }
    }
}
