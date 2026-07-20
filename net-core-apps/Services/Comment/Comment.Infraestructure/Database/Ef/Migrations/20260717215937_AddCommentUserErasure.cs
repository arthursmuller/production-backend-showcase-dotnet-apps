using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentErasureMediaObject",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    IngestionId = table.Column<int>(type: "int", nullable: false),
                    ObjectKind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ObjectName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ObjectVersion = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LastFailureCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentErasureMediaObject", x => x.Id);
                    table.CheckConstraint("CK_CmtEraseMedia_Kind", "`ObjectKind` IN ('final','quarantine')");
                    table.CheckConstraint("CK_CmtEraseMedia_User", "`UserId` > 0 AND `IngestionId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentUserErasure",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    RequestEventId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ErasureVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Cursor = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastFailureCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastFailureAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    CompletionEventId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentUserErasure", x => x.UserId);
                    table.CheckConstraint("CK_CmtUserErasure_Completion", "(`Status` = 4 AND `CompletedAtUtc` IS NOT NULL AND `CompletionEventId` IS NOT NULL) OR (`Status` <> 4 AND `CompletedAtUtc` IS NULL AND `CompletionEventId` IS NULL)");
                    table.CheckConstraint("CK_CmtUserErasure_Lease", "(`Status` = 2 AND `LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL) OR (`Status` <> 2 AND `LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL)");
                    table.CheckConstraint("CK_CmtUserErasure_Stage", "`Stage` BETWEEN 1 AND 17");
                    table.CheckConstraint("CK_CmtUserErasure_Status", "`Status` IN (1,2,3,4)");
                    table.CheckConstraint("CK_CmtUserErasure_User", "`UserId` > 0");
                    table.CheckConstraint("CK_CmtUserErasure_Version", "`ErasureVersion` > 0 AND `FencingToken` >= 0 AND `Version` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentUserMutationGuard",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentUserMutationGuard", x => x.UserId);
                    table.CheckConstraint("CK_CmtUserMutationGuard_User", "`UserId` > 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CmtEraseMedia_Ingestion",
                table: "CommentErasureMediaObject",
                columns: new[] { "UserId", "IngestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtEraseMedia_Work",
                table: "CommentErasureMediaObject",
                columns: new[] { "UserId", "DeletedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtEraseMedia_Owner_Object",
                table: "CommentErasureMediaObject",
                columns: new[] { "UserId", "ObjectKind", "ObjectName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CmtUserErasure_Work",
                table: "CommentUserErasure",
                columns: new[] { "Status", "LeaseUntilUtc", "RequestedAtUtc", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtUserErasure_Request",
                table: "CommentUserErasure",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CommentErasureMediaObject");
            migrationBuilder.DropTable(name: "CommentUserErasure");
            migrationBuilder.DropTable(name: "CommentUserMutationGuard");
        }
    }
}
