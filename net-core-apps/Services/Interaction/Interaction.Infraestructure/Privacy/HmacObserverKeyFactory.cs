using System.Text;
using BuildingBlocks.Security;
using Interaction.Application.Providers.Privacy;
using Interaction.Infraestructure.Configuration;
using Microsoft.Extensions.Options;

namespace Interaction.Infraestructure.Privacy;

public sealed class HmacObserverKeyFactory(IOptions<InteractionPrivacyOptions> options)
    : IObserverKeyFactory
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.ViewHashKey);

    public string Create(long actorId, DateTimeOffset bucketStartUtc)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{actorId}:{bucketStartUtc.ToUnixTimeSeconds()}");
        return Convert.ToHexStringLower(HmacSha256.Protect(_key, payload));
    }
}
