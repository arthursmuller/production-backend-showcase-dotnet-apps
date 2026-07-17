using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace CommomStartupExtensions
{
    public static class loggerExtension
    {
        public static WebApplicationBuilder ConfigureLogger(this WebApplicationBuilder builder,
                                                                            string appName)
        {
            builder.Host.UseSerilog((context, services, configuration) => {
                var connectionString = getSetting("SQL_CONNECTION_STRING");

                if (string.IsNullOrEmpty(connectionString))
                {
                    Console.WriteLine("Warning: SQL_CONNECTION_STRING is not set. MySQL sink will not be configured.");
                    Log.Warning("SQL_CONNECTION_STRING is not set. MySQL sink will not be configured."); // Use Serilog's static Log instance for early logging
                }

                configuration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                    .Enrich.WithProperty("ApplicationContext", appName)
                    .Enrich.FromLogContext()
                    .WriteTo.Console();

                if (!string.IsNullOrEmpty(connectionString))
                {
                    configuration.WriteTo.MySQL(
                        connectionString: connectionString,
                        tableName: "Logs",
                        restrictedToMinimumLevel: LogEventLevel.Error
                    );
                }
            });

            builder.Services.AddTransient<Microsoft.Extensions.Logging.ILogger>(p =>
            {
                var loggerFactory = p.GetRequiredService<ILoggerFactory>();
                // You could also use the HttpContext to make the name dynamic for example
                return loggerFactory.CreateLogger("Http Controller Logger");
            });

            return builder;
        }


        public static WebApplication RunWithTryCatch(this WebApplication app, string appName)
        {
            try
            {
                Log.Information("Starting web host ({ApplicationContext})", appName);
                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly ({ApplicationContext})", appName);
            }
            finally
            {
                Log.CloseAndFlush();
            }

            return app;
        }

        private static string getSetting(string setting) => Environment.GetEnvironmentVariable(setting);
    }
}
