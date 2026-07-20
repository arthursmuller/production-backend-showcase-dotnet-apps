using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Interaction.Infraestructure.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class EraseDeletedInteractionResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsErased",
                table: "EngagementSummaries",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_ViewObservations_ResourceStates_ResourceType_ResourceId",
                table: "ViewObservations",
                columns: new[] { "ResourceType", "ResourceId" },
                principalTable: "ResourceStates",
                principalColumns: new[] { "ResourceType", "ResourceId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ViewObservations_ResourceStates_ResourceType_ResourceId",
                table: "ViewObservations");

            migrationBuilder.DropColumn(
                name: "IsErased",
                table: "EngagementSummaries");
        }
    }
}
