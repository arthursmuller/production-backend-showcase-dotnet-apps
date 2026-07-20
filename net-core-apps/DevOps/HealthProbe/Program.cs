if (args.Length != 1
    || !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint)
    || endpoint.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("Usage: HealthProbe <http(s)://endpoint>");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

try
{
    using var response = await client.GetAsync(
        endpoint,
        HttpCompletionOption.ResponseHeadersRead,
        timeout.Token);
    return response.IsSuccessStatusCode ? 0 : 1;
}
catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
