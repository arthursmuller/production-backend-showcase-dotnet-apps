using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableCommentMediaIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CommentMediaIngestion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UploadIdentity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TargetCommentId = table.Column<int>(type: "int", nullable: true),
                    BoundCommentId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LeaseOwner = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastFailureCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentMediaIngestion", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentMediaIngestionItem",
                columns: table => new
                {
                    Order = table.Column<int>(type: "int", nullable: false),
                    CommentMediaIngestionId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeclaredMimeType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MediaType = table.Column<int>(type: "int", nullable: false),
                    QuarantineObjectName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuarantineObjectVersion = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuarantinedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    QuarantineDeletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CanonicalFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedMimeType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CanonicalExtension = table.Column<string>(type: "varchar(11)", maxLength: 11, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VerifiedSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Scanner = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScannerVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignatureVersion = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScannedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FinalObjectName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FinalObjectVersion = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PrivateObjectUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PromotedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    BoundAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CleanedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentMediaIngestionItem", x => new { x.CommentMediaIngestionId, x.Order });
                    table.ForeignKey(
                        name: "FK_CmtMediaIngestionItem_Batch",
                        column: x => x.CommentMediaIngestionId,
                        principalTable: "CommentMediaIngestion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CmtMediaIngestion_Due",
                table: "CommentMediaIngestion",
                columns: new[] { "State", "NextAttemptAtUtc", "LeaseUntilUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CmtMediaIngestion_Owner_BoundComment",
                table: "CommentMediaIngestion",
                columns: new[] { "OwnerUserId", "BoundCommentId" });

            migrationBuilder.CreateIndex(
                name: "UX_CmtMediaIngestion_UploadIdentity",
                table: "CommentMediaIngestion",
                column: "UploadIdentity",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentMediaIngestionItem");

            migrationBuilder.DropTable(
                name: "CommentMediaIngestion");
        }
    }
}
