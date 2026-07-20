using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CommomStartupExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommomStartupExtensions.UnitTests;

public sealed class RequestOutcomeTelemetryTests
{
    [Fact]
    public async Task Request_outcomes_are_exact_and_use_only_the_finite_outcome_label()
    {
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == PlatformTelemetry.MeterName
                    && instrument.Name == "inspacy.http.server.request.outcomes")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument.Name == "inspacy.http.server.request.outcomes")
                measurements.Enqueue(new Measurement(value, tags.ToArray()));
        });
        listener.Start();

        var cases = new (int Status, HttpRequestOutcome? Explicit, bool Cancelled, string Expected)[]
        {
            (StatusCodes.Status200OK, null, false, "success"),
            (StatusCodes.Status400BadRequest, null, false, "invalid_request"),
            (StatusCodes.Status401Unauthorized, null, false, "authentication_required"),
            (StatusCodes.Status403Forbidden, null, false, "access_forbidden"),
            (StatusCodes.Status404NotFound, null, false, "not_found"),
            (StatusCodes.Status409Conflict, null, false, "conflict"),
            (StatusCodes.Status429TooManyRequests, null, false, "business_rejection"),
            (StatusCodes.Status429TooManyRequests, HttpRequestOutcome.ClientRateLimited, false, "client_rate_limited"),
            (StatusCodes.Status503ServiceUnavailable, HttpRequestOutcome.LoadShed, false, "load_shed"),
            (StatusCodes.Status504GatewayTimeout, null, true, "server_timeout"),
            (StatusCodes.Status503ServiceUnavailable, HttpRequestOutcome.DependencyUnavailable, false, "dependency_unavailable"),
            (StatusCodes.Status500InternalServerError, null, false, "server_error"),
            (StatusCodes.Status200OK, null, true, "client_cancelled"),
        };

        foreach (var testCase in cases)
        {
            using var cancellation = new CancellationTokenSource();
            if (testCase.Cancelled)
                cancellation.Cancel();

            var context = new DefaultHttpContext
            {
                RequestAborted = cancellation.Token,
            };
            context.Response.StatusCode = testCase.Status;
            if (testCase.Explicit is { } explicitOutcome)
            {
                context.Items[HttpRequestOutcomeContext.ItemKey] = explicitOutcome;
            }

            var middleware = new RequestLoggingMiddleware(
                _ => Task.CompletedTask,
                NullLogger<RequestLoggingMiddleware>.Instance);
            await middleware.InvokeAsync(context);
        }

        Assert.Equal(cases.Length, measurements.Count);
        var recorded = measurements.ToArray();
        Assert.Equal(
            cases.Select(testCase => testCase.Expected),
            recorded.Select(measurement => measurement.Tags
                .Single(tag => tag.Key == "http_request_outcome")
                .Value as string));
        Assert.All(recorded, measurement =>
        {
            Assert.Equal(1, measurement.Value);
            Assert.Equal(2, measurement.Tags.Length);
            Assert.Equal(
                "unmatched",
                measurement.Tags.Single(tag => tag.Key == "http_route").Value);
        });
    }

    private sealed record Measurement(
        long Value,
        KeyValuePair<string, object?>[] Tags);
}
