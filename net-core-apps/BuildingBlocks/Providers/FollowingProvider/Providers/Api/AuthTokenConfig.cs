namespace FollowingProvider.Providers.Api
{
    public class ClientCredentialTokenConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string ReadScope { get; set; }

        public ClientCredentialTokenConfig(string clientId, string clientSecret, string readScope)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            ReadScope = readScope;
        }
    }
}
