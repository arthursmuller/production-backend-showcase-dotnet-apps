using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableInteractionUserErasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InteractionUserErasures",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ErasureVersion = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RawViewsExpireAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Stage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    LeaseOwner = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false),
                    LastProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteractionUserErasures", x => x.UserId);
                    table.CheckConstraint("CK_InteractionUserErasure_Completion", "(`Stage` = 3 AND `CompletedAtUtc` IS NOT NULL) OR (`Stage` <> 3 AND `CompletedAtUtc` IS NULL)");
                    table.CheckConstraint("CK_InteractionUserErasure_Lease", "(`LeaseOwner` IS NULL AND `LeaseUntilUtc` IS NULL) OR (`LeaseOwner` IS NOT NULL AND `LeaseUntilUtc` IS NOT NULL)");
                    table.CheckConstraint("CK_InteractionUserErasure_Stage", "`Stage` IN (1,2,3)");
                    table.CheckConstraint("CK_InteractionUserErasure_User", "`UserId` > 0");
                    table.CheckConstraint("CK_InteractionUserErasure_Version", "`ErasureVersion` > 0 AND `FencingToken` >= 0");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InteractionUserErasure_Work",
                table: "InteractionUserErasures",
                columns: new[] { "Stage", "RawViewsExpireAtUtc", "LastProcessedAtUtc", "LeaseUntilUtc", "RequestedAtUtc", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_InteractionUserErasure_Request",
                table: "InteractionUserErasures",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InteractionUserErasures");
        }
    }
}
