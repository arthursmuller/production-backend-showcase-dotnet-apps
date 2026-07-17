using System;

namespace FollowingProvider.Dtos
{
    public class GetUserFollowersAndFriendsIdDto
    {
        public int[] Friends { get; set; }
        public int[] Followers { get; set; }
        public int[] Categories { get; set; }
    }
}
