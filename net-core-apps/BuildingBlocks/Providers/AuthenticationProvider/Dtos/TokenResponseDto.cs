using System;

namespace AuthenticationProvider.Dtos
{
    public class TokenResponse
    {
        public string AccessToken { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
