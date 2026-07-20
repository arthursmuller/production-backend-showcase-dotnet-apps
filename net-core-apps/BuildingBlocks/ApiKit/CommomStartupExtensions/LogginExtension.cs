using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using System.Threading;
using System.Threading.Tasks;

namespace CommomStartupExtensions
{
    public static class loggerExtension
    {
        public static WebApplicationBuilder ConfigureLogger(this WebApplicationBuilder builder,
                                                                            string appName)
        {
            builder.Host.UseSerilog((context, services, configuration) =>
            {
                configuration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .Enrich.WithProperty("ApplicationContext", appName)
                    .Enrich.FromLogContext()
                    .WriteTo.Console();
            });

            return builder;
        }
        public static async Task<WebApplication> RunWithTryCatchAsync(
            this WebApplication app,
            string appName,
            CancellationToken cancellationToken)
        {
            try
            {
                Log.Information("Starting web host ({ApplicationContext})", appName);
                await app.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log.Information("Web host shutdown requested ({ApplicationContext})", appName);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly ({ApplicationContext})", appName);
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }

            return app;
        }

    }
}
