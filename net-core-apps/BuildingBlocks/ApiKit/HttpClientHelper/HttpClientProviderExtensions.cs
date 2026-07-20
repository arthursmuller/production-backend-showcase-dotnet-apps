using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Http.Resilience;

namespace HttpClientHelper
{
    public static class HttpProvider
    {
        //https://github.com/openiddict/openiddict-core/issues/1445
        //client credentials https://www.seniordevtutorial.com/Lesson/openiddict-client-credentials-flow
        //https://www.youtube.com/watch?v=CtHcT5X_rYQ
        //improve https://rehansaeed.com/optimally-configuring-asp-net-core-httpclientfactory/
        public static IServiceCollection New<T>(
            IServiceCollection services,
            string baseAddress,
            IDictionary<string, string>? defaultHeaders = null,
            TimeSpan? timeout = null,
            bool retryUnsafeMethods = false) where T : class
        {
            var validatedBaseAddress = ValidateBaseAddress(baseAddress);
            ValidateTimeout(timeout);

            var builder = services.AddHttpClient<T>(httpClient =>
            {
                httpClient.BaseAddress = validatedBaseAddress;
                // The resilience pipeline owns attempt and total timeouts.
                httpClient.Timeout = Timeout.InfiniteTimeSpan;

                if (defaultHeaders is not null)
                    foreach (var header in defaultHeaders)
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            });

            AddProductionResilience(builder, timeout, retryUnsafeMethods);

            return services;
        }

        public static IServiceCollection New<T, T1>(
            IServiceCollection services,
            string baseAddress,
            IDictionary<string, string>? defaultHeaders = null,
            TimeSpan? timeout = null,
            bool retryUnsafeMethods = false
        )
            where T : class
            where T1 : class, T
        {
            var validatedBaseAddress = ValidateBaseAddress(baseAddress);
            ValidateTimeout(timeout);

            var builder = services.AddHttpClient<T, T1>(httpClient =>
            {
                httpClient.BaseAddress = validatedBaseAddress;
                httpClient.Timeout = Timeout.InfiniteTimeSpan;

                if (defaultHeaders is not null)
                    foreach (var header in defaultHeaders)
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            });

            AddProductionResilience(builder, timeout, retryUnsafeMethods);

            return services;
        }

        private static void AddProductionResilience(
            IHttpClientBuilder builder,
            TimeSpan? totalTimeout,
            bool retryUnsafeMethods)
        {
            var configuredTotalTimeout = totalTimeout ?? TimeSpan.FromSeconds(30);
            var attemptTimeout = TimeSpan.FromSeconds(
                Math.Max(1, Math.Min(10, configuredTotalTimeout.TotalSeconds / 3)));

            builder.AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = configuredTotalTimeout;
                options.AttemptTimeout.Timeout = attemptTimeout;

                // Fail fast instead of accumulating a per-replica dependency queue.
                options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = 128;
                options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 0;

                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(200);
                options.Retry.UseJitter = true;
                if (!retryUnsafeMethods)
                    options.Retry.DisableForUnsafeHttpMethods();

                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 20;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            });
        }

        private static Uri ValidateBaseAddress(string baseAddress)
        {
            const int maxBaseAddressLength = 2048;

            if (string.IsNullOrWhiteSpace(baseAddress))
                throw new InvalidOperationException("An HTTP provider base address is required.");
            if (baseAddress.Length > maxBaseAddressLength)
                throw new InvalidOperationException("The HTTP provider base address exceeds the maximum length.");
            if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    "The HTTP provider base address must be an absolute HTTP(S) URI without credentials or a fragment.");
            }

            return uri;
        }

        private static void ValidateTimeout(TimeSpan? timeout)
        {
            if (timeout is { } value && (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(5)))
                throw new InvalidOperationException("HTTP provider timeouts must be greater than zero and no more than five minutes.");
        }
    }
}
