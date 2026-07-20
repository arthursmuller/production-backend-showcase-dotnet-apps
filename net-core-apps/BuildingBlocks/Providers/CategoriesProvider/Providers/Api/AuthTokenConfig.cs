namespace CategoriesProvider.Providers.Api
{
    public class ClientCredentialTokenConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ReadScope { get; set; } = string.Empty;

        public ClientCredentialTokenConfig(string clientId, string clientSecret, string readScope)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            ReadScope = readScope;
        }

        public ClientCredentialTokenConfig()
        {
        }
    }
}
