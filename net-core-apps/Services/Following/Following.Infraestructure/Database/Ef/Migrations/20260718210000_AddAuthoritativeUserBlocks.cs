using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Following.Infra.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoritativeUserBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RelationshipPairGuard",
                columns: table => new
                {
                    PairMinUserId = table.Column<int>(type: "int", nullable: false),
                    PairMaxUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey(
                        "PK_RelationshipPairGuard",
                        x => new { x.PairMinUserId, x.PairMaxUserId });
                    table.CheckConstraint(
                        "CK_RelationshipPairGuard_Normalized",
                        "`PairMinUserId` > 0 AND `PairMaxUserId` > `PairMinUserId`");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserBlock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BlockerUserId = table.Column<int>(type: "int", nullable: false),
                    BlockedUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "SISTEMA")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlock", x => x.Id);
                    table.CheckConstraint("CK_UserBlock_NoSelf", "`BlockerUserId` <> `BlockedUserId`");
                    table.CheckConstraint("CK_UserBlock_PositiveUsers", "`BlockerUserId` > 0 AND `BlockedUserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserBlock_Blocker_Cursor",
                table: "UserBlock",
                columns: new[] { "BlockerUserId", "CreatedDate", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlock_ReverseDirection",
                table: "UserBlock",
                columns: new[] { "BlockedUserId", "BlockerUserId" });

            migrationBuilder.CreateIndex(
                name: "UX_UserBlock_Direction",
                table: "UserBlock",
                columns: new[] { "BlockerUserId", "BlockedUserId" },
                unique: true);

            AddBlockedRelationshipTriggers(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropBlockedRelationshipTriggers(migrationBuilder);

            migrationBuilder.DropTable(
                name: "UserBlock");

            migrationBuilder.DropTable(
                name: "RelationshipPairGuard");
        }

        private static void AddBlockedRelationshipTriggers(MigrationBuilder migrationBuilder)
        {
            AddBlockedRelationshipTrigger(
                migrationBuilder,
                "Follow",
                "NEW.`User_Id`",
                "NEW.`Followed_Id`");
            AddBlockedRelationshipTrigger(
                migrationBuilder,
                "FriendRequest",
                "NEW.`User_Id`",
                "NEW.`Friend_Id`");
            AddBlockedRelationshipTrigger(
                migrationBuilder,
                "Friendship",
                "NEW.`User_Id`",
                "NEW.`Friend_Id`");
        }

        private static void AddBlockedRelationshipTrigger(
            MigrationBuilder migrationBuilder,
            string table,
            string firstUserSql,
            string secondUserSql)
        {
            migrationBuilder.Sql(
                $"""
                CREATE TRIGGER `TR_{table}_RejectBlockedInsert`
                BEFORE INSERT ON `{table}`
                FOR EACH ROW
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM `UserBlock` block_authority
                        WHERE (block_authority.`BlockerUserId` = {firstUserSql}
                               AND block_authority.`BlockedUserId` = {secondUserSql})
                           OR (block_authority.`BlockerUserId` = {secondUserSql}
                               AND block_authority.`BlockedUserId` = {firstUserSql})
                    ) THEN
                        SIGNAL SQLSTATE '45000'
                            SET MESSAGE_TEXT = 'blocked relationship mutation rejected';
                    END IF;
                END;
                """);
        }

        private static void DropBlockedRelationshipTriggers(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Follow", "FriendRequest", "Friendship" })
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `TR_{table}_RejectBlockedInsert`;");
        }
    }
}
