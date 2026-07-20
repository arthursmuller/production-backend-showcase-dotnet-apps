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
            _config = config ?? throw new ArgumentNullException(nameof(config));
            ArgumentException.ThrowIfNullOrWhiteSpace(_config.ClientId);
            ArgumentException.ThrowIfNullOrWhiteSpace(_config.ClientSecret);
            ArgumentException.ThrowIfNullOrWhiteSpace(_config.ReadScope);
        }
        public async Task<int> GetIdByName(string name, System.Threading.CancellationToken cancellationToken)
        {
            try
            {
                var token = await GetAccessTokenAsync(cancellationToken);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"api/categories/get-id-by-name/{Uri.EscapeDataString(name)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await _httpClient.SendAsync(request, cancellationToken);
                var result = await getResponseAsync<DefaultResponse<int>>(
                    response,
                    cancellationToken);
                handleError(result);
                if (result.Data <= 0)
                {
                    throw new CategoriesProviderException(
                        "The categories service returned an invalid category identifier.");
                }

                return result.Data;
            }
            catch (HttpRequestException ex)
            {
                handleError(ex);
                throw;
            }
        }

        private Task<string> GetAccessTokenAsync(System.Threading.CancellationToken cancellationToken) =>
            _authService.Api.GetAccessToken(
                _config.ClientId,
                _config.ClientSecret,
                new[] { _config.ReadScope },
                cancellationToken);
    }
}
