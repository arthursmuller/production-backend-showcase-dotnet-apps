using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Following.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AuthoritativeRelationshipStreams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelationshipEventStream",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StreamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    ViewerUserId = table.Column<int>(type: "int", nullable: false),
                    RelationshipKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentState = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrentVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastEventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LastOccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RelationshipEventStream", x => x.Id);
                    table.CheckConstraint("CK_RelationshipEventStream_CurrentVersion", "`CurrentVersion` > 0");
                    table.CheckConstraint("CK_RelationshipEventStream_Directional", "`OwnerUserId` <> `ViewerUserId`");
                    table.CheckConstraint("CK_RelationshipEventStream_Owner", "`OwnerUserId` > 0");
                    table.CheckConstraint("CK_RelationshipEventStream_Version", "`Version` > 0");
                    table.CheckConstraint("CK_RelationshipEventStream_Viewer", "`ViewerUserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RelationshipEventStream_Updated_Cursor",
                table: "RelationshipEventStream",
                columns: new[] { "UpdatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_RelationshipEventStream_Direction_Kind",
                table: "RelationshipEventStream",
                columns: new[] { "OwnerUserId", "ViewerUserId", "RelationshipKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_RelationshipEventStream_StreamId",
                table: "RelationshipEventStream",
                column: "StreamId",
                unique: true);

            // Existing relationships receive a durable synthetic initial checkpoint. No event is
            // fabricated during migration; Feed obtains these states through bounded bootstrap.
            migrationBuilder.Sql(
                """
                INSERT INTO `RelationshipEventStream`
                    (`StreamId`, `OwnerUserId`, `ViewerUserId`, `RelationshipKind`,
                     `CurrentState`, `CurrentVersion`, `LastEventId`, `LastOccurredAtUtc`,
                     `CreatedAtUtc`, `UpdatedAtUtc`, `Version`)
                SELECT UUID(), `Followed_Id`, `User_Id`, 'follow', 'active', 1, UUID(),
                       COALESCE(`UpdateDate`, `CreatedDate`, UTC_TIMESTAMP(6)),
                       COALESCE(`CreatedDate`, UTC_TIMESTAMP(6)),
                       COALESCE(`UpdateDate`, `CreatedDate`, UTC_TIMESTAMP(6)), 1
                FROM `Follow`;

                INSERT INTO `RelationshipEventStream`
                    (`StreamId`, `OwnerUserId`, `ViewerUserId`, `RelationshipKind`,
                     `CurrentState`, `CurrentVersion`, `LastEventId`, `LastOccurredAtUtc`,
                     `CreatedAtUtc`, `UpdatedAtUtc`, `Version`)
                SELECT UUID(), `User_Id`, `Friend_Id`, 'friend', 'active', 1, UUID(),
                       COALESCE(`UpdateDate`, `CreatedDate`, UTC_TIMESTAMP(6)),
                       COALESCE(`CreatedDate`, UTC_TIMESTAMP(6)),
                       COALESCE(`UpdateDate`, `CreatedDate`, UTC_TIMESTAMP(6)), 1
                FROM `Friendship`;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RelationshipEventStream");
        }
    }
}
