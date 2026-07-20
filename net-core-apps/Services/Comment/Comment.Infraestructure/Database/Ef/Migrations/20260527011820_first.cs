using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comment.Infra.Migrations
{
    /// <inheritdoc />
    public partial class First : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentBookmark",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    GroupName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentBookmark", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentSocialAnalytics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TotalViews = table.Column<int>(type: "int", nullable: false),
                    TotalRatings = table.Column<int>(type: "int", nullable: false),
                    TotalReports = table.Column<int>(type: "int", nullable: false),
                    TotalLikes = table.Column<int>(type: "int", nullable: false),
                    TotalDislikes = table.Column<int>(type: "int", nullable: false),
                    TotalShares = table.Column<int>(type: "int", nullable: false),
                    TotalComments = table.Column<int>(type: "int", nullable: false),
                    RatingsTrueCount = table.Column<double>(type: "double", nullable: false),
                    AverageRating = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentSocialAnalytics", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RatingLevel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingLevel", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentPost",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    User_Id = table.Column<int>(type: "int", nullable: true),
                    User_FullName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RelatedEntity = table.Column<string>(type: "longtext", nullable: true, computedColumnSql: "LOWER(CONCAT(IdOfRelatedEntity,TypeOfRelatedEntity))")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GifUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdOfRelatedEntity = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TypeOfRelatedEntity = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedOnFeed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VisibilityLevel_Id = table.Column<int>(type: "int", nullable: true),
                    VisibilityLevel_Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SocialId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentPost", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentPost_CommentSocialAnalytics_SocialId",
                        column: x => x.SocialId,
                        principalTable: "CommentSocialAnalytics",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentSocialRating",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnalyticsId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentSocialRating", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentSocialRating_CommentSocialAnalytics_AnalyticsId",
                        column: x => x.AnalyticsId,
                        principalTable: "CommentSocialAnalytics",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CategoryName",
                columns: table => new
                {
                    CommentPostId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryName", x => new { x.CommentPostId, x.Id });
                    table.ForeignKey(
                        name: "FK_CategoryName_CommentPost_CommentPostId",
                        column: x => x.CommentPostId,
                        principalTable: "CommentPost",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentMediaItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentPostId = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MediaType = table.Column<int>(type: "int", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DurationSeconds = table.Column<long>(type: "bigint", nullable: true),
                    MimeType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentMediaItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentMediaItem_CommentPost_CommentPostId",
                        column: x => x.CommentPostId,
                        principalTable: "CommentPost",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentMentions",
                columns: table => new
                {
                    CommentPostId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentMentions", x => new { x.CommentPostId, x.Id });
                    table.ForeignKey(
                        name: "FK_CommentMentions_CommentPost_CommentPostId",
                        column: x => x.CommentPostId,
                        principalTable: "CommentPost",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentDislike",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    User_Id = table.Column<int>(type: "int", nullable: true),
                    User_FullName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentDislike", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentDislike_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentLike",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    User_Id = table.Column<int>(type: "int", nullable: true),
                    User_FullName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentLike", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentLike_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentPostComment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentPostComment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentPostComment_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentRatingLevel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    _levelId = table.Column<int>(type: "int", nullable: false),
                    User_Id = table.Column<int>(type: "int", nullable: true),
                    User_FullName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Comment = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRatingLevel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentRatingLevel_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CommentRatingLevel_RatingLevel__levelId",
                        column: x => x._levelId,
                        principalTable: "RatingLevel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentReport",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reply = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Approved = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    User_Id = table.Column<int>(type: "int", nullable: true),
                    User_FullName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentReport", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentReport_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CommentShare",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CommentSocialRatingId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserUpdate = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentShare", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentShare_CommentSocialRating_CommentSocialRatingId",
                        column: x => x.CommentSocialRatingId,
                        principalTable: "CommentSocialRating",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommentBookmark_UserId_GroupId_PostId",
                table: "CommentBookmark",
                columns: new[] { "UserId", "GroupId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentBookmark_UserId_PostId",
                table: "CommentBookmark",
                columns: new[] { "UserId", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommentDislike_CommentSocialRatingId",
                table: "CommentDislike",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentLike_CommentSocialRatingId",
                table: "CommentLike",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentMediaItem_CommentPostId",
                table: "CommentMediaItem",
                column: "CommentPostId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentPost_SocialId",
                table: "CommentPost",
                column: "SocialId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentPostComment_CommentSocialRatingId",
                table: "CommentPostComment",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentRatingLevel__levelId",
                table: "CommentRatingLevel",
                column: "_levelId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentRatingLevel_CommentSocialRatingId",
                table: "CommentRatingLevel",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentReport_CommentSocialRatingId",
                table: "CommentReport",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentShare_CommentSocialRatingId",
                table: "CommentShare",
                column: "CommentSocialRatingId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentSocialRating_AnalyticsId",
                table: "CommentSocialRating",
                column: "AnalyticsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryName");

            migrationBuilder.DropTable(
                name: "CommentBookmark");

            migrationBuilder.DropTable(
                name: "CommentDislike");

            migrationBuilder.DropTable(
                name: "CommentLike");

            migrationBuilder.DropTable(
                name: "CommentMediaItem");

            migrationBuilder.DropTable(
                name: "CommentMentions");

            migrationBuilder.DropTable(
                name: "CommentPostComment");

            migrationBuilder.DropTable(
                name: "CommentRatingLevel");

            migrationBuilder.DropTable(
                name: "CommentReport");

            migrationBuilder.DropTable(
                name: "CommentShare");

            migrationBuilder.DropTable(
                name: "CommentPost");

            migrationBuilder.DropTable(
                name: "RatingLevel");

            migrationBuilder.DropTable(
                name: "CommentSocialRating");

            migrationBuilder.DropTable(
                name: "CommentSocialAnalytics");
        }
    }
}
