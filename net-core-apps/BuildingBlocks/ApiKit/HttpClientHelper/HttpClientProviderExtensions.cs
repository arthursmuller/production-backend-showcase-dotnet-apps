using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;

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
            IDictionary<string, string> defaultHeaders = null,
            TimeSpan? timeout = null) where T : class
        {
            services.AddHttpClient<T>(httpClient =>
            {
                httpClient.BaseAddress = new Uri(baseAddress);
                if (timeout.HasValue)
                    httpClient.Timeout = timeout.Value;

                if (defaultHeaders is not null)
                    foreach (var header in defaultHeaders)
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            })
            .ConfigureHttpMessageHandlerBuilder((c) =>
                new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });

            return services;
        }

        public static IServiceCollection New<T, T1>(
            IServiceCollection services,
            string baseAddress,
            IDictionary<string, string> defaultHeaders = null,
            TimeSpan? timeout = null
        )
            where T : class
            where T1 : class, T
        {
                services.AddHttpClient<T, T1>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri(baseAddress);
                    if (timeout.HasValue)
                        httpClient.Timeout = timeout.Value;

                    if (defaultHeaders is not null)
                        foreach (var header in defaultHeaders)
                            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                });

                return services;
            }
    }
}
