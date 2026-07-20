using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace ApiContract.Tests;

public sealed class HttpTransportBoundaryTests
{
    [Fact]
    public async Task Public_route_metadata_never_declares_domain_or_broker_messages_as_http_models()
    {
        await using var host = await HttpContractHost.StartAsync();
        var endpoints = ((IEndpointRouteBuilder)host.App).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => !IsInternal(endpoint.RoutePattern.RawText))
            .ToArray();

        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText ?? "<unknown>";
            foreach (var response in endpoint.Metadata
                         .GetOrderedMetadata<IProducesResponseTypeMetadata>())
            {
                if (response.Type is not null)
                    AssertTransportRoot(response.Type, route, "response");
            }

            foreach (var request in endpoint.Metadata.GetOrderedMetadata<IAcceptsMetadata>())
            {
                if (request.RequestType is not null)
                    AssertTransportRoot(request.RequestType, route, "request");
            }
        }
    }

    private static bool IsInternal(string? route) =>
        route?.StartsWith("/internal/", StringComparison.OrdinalIgnoreCase) == true
        || route?.StartsWith("/api/internal/", StringComparison.OrdinalIgnoreCase) == true;

    private static void AssertTransportRoot(Type root, string route, string direction)
    {
        foreach (var type in EnumerateRootAndGenericArguments(root))
        {
            var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            var typeNamespace = type.Namespace ?? string.Empty;
            var isDomain = assemblyName.EndsWith(".Domain", StringComparison.Ordinal)
                || typeNamespace.Contains(".Domain.", StringComparison.Ordinal)
                || typeNamespace.EndsWith(".Domain", StringComparison.Ordinal)
                || typeNamespace.StartsWith("StarterEntities", StringComparison.Ordinal);
            var isBrokerMessage = string.Equals(
                assemblyName,
                "MessageBrokerContracts",
                StringComparison.Ordinal);

            Assert.False(
                isDomain || isBrokerMessage,
                $"HTTP {direction} metadata for {route} exposes {type.FullName} from " +
                $"{assemblyName}; use an Application-owned DTO and map at the boundary.");
        }
    }

    private static IEnumerable<Type> EnumerateRootAndGenericArguments(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in EnumerateRootAndGenericArguments(elementType))
                yield return nested;
        }

        if (!type.IsGenericType)
            yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in EnumerateRootAndGenericArguments(argument))
                yield return nested;
        }
    }
}
