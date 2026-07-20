using System.Security.Cryptography;
using System.Text;
using Interaction.Infraestructure.Configuration;
using Interaction.Infraestructure.Privacy;
using Microsoft.Extensions.Options;
using Xunit;

namespace Interaction.UnitTests;

public sealed class InteractionPrivacyTests
{
    [Fact]
    public void View_hash_key_uses_the_shared_HMAC_policy_and_preserves_the_existing_digest()
    {
        var validator = new InteractionPrivacyOptionsValidator();
        var key = new string('k', 32);
        Assert.True(validator.Validate(null, new InteractionPrivacyOptions
        {
            ViewHashKey = key,
        }).Succeeded);
        Assert.False(validator.Validate(null, new InteractionPrivacyOptions
        {
            ViewHashKey = new string('k', 31),
        }).Succeeded);

        var bucket = DateTimeOffset.FromUnixTimeSeconds(1_721_260_800);
        var factory = new HmacObserverKeyFactory(Options.Create(
            new InteractionPrivacyOptions { ViewHashKey = key }));
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key),
            Encoding.UTF8.GetBytes($"42:{bucket.ToUnixTimeSeconds()}")));

        Assert.Equal(expected, factory.Create(42, bucket));
    }
}
