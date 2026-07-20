using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace InspacyApiHost
{
    /// <summary>
    /// Identifies the configuration entry used as the multipart request limit for a
    /// <see cref="FormDataEndpointAttribute{TSuccess,TLimit}"/>.
    /// </summary>
    public interface IFormDataEndpointLimit
    {
        static abstract string ConfigurationKey { get; }
    }

    /// <summary>
    /// Reads the <c>MaxMultipartBodyBytes</c> value from the application's configuration.
    /// </summary>
    public sealed class MaxMultipartBodyBytes : IFormDataEndpointLimit
    {
        private MaxMultipartBodyBytes()
        {
        }

        public static string ConfigurationKey => nameof(MaxMultipartBodyBytes);
    }

    /// <summary>
    /// Describes a multipart MVC endpoint and enforces its configuration-driven body limit.
    /// This single attribute replaces <see cref="ConsumesAttribute"/>,
    /// <see cref="RequestSizeLimitAttribute"/>, <see cref="RequestFormLimitsAttribute"/>,
    /// and the success/error <see cref="ProducesResponseTypeAttribute"/> declarations.
    /// </summary>
    /// <typeparam name="TSuccess">The successful response payload wrapped in <see cref="Response{T}"/>.</typeparam>
    /// <typeparam name="TLimit">The configuration key that supplies the request limit.</typeparam>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class FormDataEndpointAttribute<TSuccess, TLimit> : Attribute,
        IActionModelConvention,
        IFilterFactory,
        IOrderedFilter
        where TLimit : IFormDataEndpointLimit
    {
        /// <summary>
        /// Success status advertised by API Explorer.
        /// </summary>
        public int SuccessStatusCode { get; set; } = StatusCodes.Status200OK;

        public bool IsReusable => false;

        // Request limits must be applied before model binding reads the form body.
        public int Order => int.MinValue + 100;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetService(typeof(IConfiguration)) as IConfiguration
                ?? throw new InvalidOperationException("The application configuration service is unavailable.");
            var defaultFormOptions = (serviceProvider.GetService(typeof(IOptions<FormOptions>))
                as IOptions<FormOptions>)?.Value ?? new FormOptions();
            var configuredValue = configuration[TLimit.ConfigurationKey];

            if (!long.TryParse(
                    configuredValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var maxBodyBytes) ||
                maxBodyBytes <= 0)
            {
                throw new InvalidOperationException(
                    $"Configuration value '{TLimit.ConfigurationKey}' must be a positive byte count.");
            }

            return new FormDataRequestLimitFilter(
                maxBodyBytes,
                CopyFormOptions(defaultFormOptions, maxBodyBytes));
        }

        public void Apply(ActionModel action)
        {
            action.Filters.Add(new ConsumesAttribute("multipart/form-data"));
            action.Filters.Add(new ProducesResponseTypeAttribute(
                typeof(Response<TSuccess>),
                SuccessStatusCode));
            action.Filters.Add(new ProducesResponseTypeAttribute(
                typeof(ProblemDetails),
                StatusCodes.Status400BadRequest,
                "application/problem+json"));
        }

        private static FormOptions CopyFormOptions(FormOptions source, long maxBodyBytes) =>
            new()
            {
                BufferBody = source.BufferBody,
                MemoryBufferThreshold = source.MemoryBufferThreshold,
                BufferBodyLengthLimit = source.BufferBodyLengthLimit,
                ValueCountLimit = source.ValueCountLimit,
                KeyLengthLimit = source.KeyLengthLimit,
                ValueLengthLimit = source.ValueLengthLimit,
                MultipartBoundaryLengthLimit = source.MultipartBoundaryLengthLimit,
                MultipartHeadersCountLimit = source.MultipartHeadersCountLimit,
                MultipartHeadersLengthLimit = source.MultipartHeadersLengthLimit,
                MultipartBodyLengthLimit = maxBodyBytes,
            };

        private sealed class FormDataRequestLimitFilter : IAuthorizationFilter
        {
            private readonly long _maxBodyBytes;
            private readonly FormOptions _formOptions;

            public FormDataRequestLimitFilter(long maxBodyBytes, FormOptions formOptions)
            {
                _maxBodyBytes = maxBodyBytes;
                _formOptions = formOptions;
            }

            public void OnAuthorization(AuthorizationFilterContext context)
            {
                var httpContext = context.HttpContext;
                var requestSizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();

                if (requestSizeFeature is { IsReadOnly: false })
                    requestSizeFeature.MaxRequestBodySize = _maxBodyBytes;

                if (httpContext.Request.HasFormContentType)
                {
                    httpContext.Features.Set<IFormFeature>(new FormFeature(
                        httpContext.Request,
                        _formOptions));
                }
            }
        }
    }
}
