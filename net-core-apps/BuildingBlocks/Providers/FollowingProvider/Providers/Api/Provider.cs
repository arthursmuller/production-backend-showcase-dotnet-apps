using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AuthenticationProvider.Abstractions;
using FollowingProvider.Abstractions;
using FollowingProvider.Dtos;

namespace FollowingProvider.Providers.Api
{
    public class FollowingApiProvider : FollowingApiBase, IFollowingApi
    {
        private readonly IAuthService _authService;
        private readonly ClientCredentialTokenConfig _config;

        public FollowingApiProvider(HttpClient httpClient, ClientCredentialTokenConfig config, IAuthService authProvider) : base(httpClient)
        {
            _authService = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _config = config;
        }
        public async Task<GetUserFollowersAndFriendsIdDto> GetUserFollowersAndFriendsIds(int userId)
        {
			DefaultResponse<GetUserFollowersAndFriendsIdDto> res = null;

			try
			{
                await SetAuthorizationHeaderAsync();
				res = await getAsync<DefaultResponse<GetUserFollowersAndFriendsIdDto>>($"api/relations/v1/get-followers-and-friends-ids/{userId}");
				handleError(res);
			}
			catch (HttpRequestException ex)
			{
				handleError(ex);
			}

			return res.Data;
		}

        private async Task SetAuthorizationHeaderAsync()
        {
            var token = await _authService.Api.GetAccessToken(_config.ClientId, _config.ClientSecret, new[] { _config.ReadScope });
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
