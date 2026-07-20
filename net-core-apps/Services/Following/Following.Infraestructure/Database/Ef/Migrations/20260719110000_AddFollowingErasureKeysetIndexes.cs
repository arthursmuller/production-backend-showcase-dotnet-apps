using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Following.Infra.Database.Ef.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowingErasureKeysetIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserBlock_Erase_Blocked",
                table: "UserBlock",
                columns: new[] { "BlockedUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBlock_Erase_Blocker",
                table: "UserBlock",
                columns: new[] { "BlockerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RelStream_Erase_Owner",
                table: "RelationshipEventStream",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RelStream_Erase_Viewer",
                table: "RelationshipEventStream",
                columns: new[] { "ViewerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_Erase_Owner",
                table: "FriendshipSubscriptionFeature",
                columns: new[] { "OwnerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscription_Erase_Requester",
                table: "FriendshipSubscriptionFeature",
                columns: new[] { "RequesterId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendship_Erase_Friend",
                table: "Friendship",
                columns: new[] { "Friend_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendship_Erase_User",
                table: "Friendship",
                columns: new[] { "User_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequest_Erase_Recipient",
                table: "FriendRequest",
                columns: new[] { "Friend_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequest_Erase_Sender",
                table: "FriendRequest",
                columns: new[] { "User_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Follow_Erase_Followed",
                table: "Follow",
                columns: new[] { "Followed_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Follow_Erase_Follower",
                table: "Follow",
                columns: new[] { "User_Id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryFollow_Erase_User",
                table: "CategoryFollow",
                columns: new[] { "User_Id", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserBlock_Erase_Blocked",
                table: "UserBlock");

            migrationBuilder.DropIndex(
                name: "IX_UserBlock_Erase_Blocker",
                table: "UserBlock");

            migrationBuilder.DropIndex(
                name: "IX_RelStream_Erase_Owner",
                table: "RelationshipEventStream");

            migrationBuilder.DropIndex(
                name: "IX_RelStream_Erase_Viewer",
                table: "RelationshipEventStream");

            migrationBuilder.DropIndex(
                name: "IX_Subscription_Erase_Owner",
                table: "FriendshipSubscriptionFeature");

            migrationBuilder.DropIndex(
                name: "IX_Subscription_Erase_Requester",
                table: "FriendshipSubscriptionFeature");

            migrationBuilder.DropIndex(
                name: "IX_Friendship_Erase_Friend",
                table: "Friendship");

            migrationBuilder.DropIndex(
                name: "IX_Friendship_Erase_User",
                table: "Friendship");

            migrationBuilder.DropIndex(
                name: "IX_FriendRequest_Erase_Recipient",
                table: "FriendRequest");

            migrationBuilder.DropIndex(
                name: "IX_FriendRequest_Erase_Sender",
                table: "FriendRequest");

            migrationBuilder.DropIndex(
                name: "IX_Follow_Erase_Followed",
                table: "Follow");

            migrationBuilder.DropIndex(
                name: "IX_Follow_Erase_Follower",
                table: "Follow");

            migrationBuilder.DropIndex(
                name: "IX_CategoryFollow_Erase_User",
                table: "CategoryFollow");
        }
    }
}
