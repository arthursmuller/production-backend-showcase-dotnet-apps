using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AuthenticationProvider.Abstractions;
using CategoriesProvider.Abstractions;

namespace CategoriesProvider.Providers.Api
{
    public class CategoriesApiProvider : CategoriesApiBase, ICategoriesApi
    {
        private readonly IAuthService _authService;
        private readonly ClientCredentialTokenConfig _config;

        public CategoriesApiProvider(HttpClient httpClient, ClientCredentialTokenConfig config, IAuthService authProvider) : base(httpClient)
        {
            _authService = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _config = config;
        }
        public async Task<int> GetIdByName(string name)
		{
			DefaultResponse<int> res = null;

			try
			{
                await SetAuthorizationHeaderAsync();
				res = await getAsync<DefaultResponse<int>>($"api/categories/v1/get-id-by-name/{name}");
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
