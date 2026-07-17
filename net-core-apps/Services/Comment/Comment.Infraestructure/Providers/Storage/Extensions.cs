using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Comment.Application.Providers.Storage;
using StorageProvider;
using System;
using System.Linq;

namespace Comment.Infra.Providers.Storage
{
    public static class StorageExtensions
    {
        public static IServiceCollection ConfigureStorage(this IServiceCollection services, IConfiguration configuration)
        {
            ConfigureStorageVariables(services, configuration);
            
            services.ConfigureStorageProvider(configuration);
            services.AddScoped<IStorage, Storage>();

            return services;
        }

        private static IServiceCollection ConfigureStorageVariables(IServiceCollection services, IConfiguration configuration)
        {
            var config = new StorageConfiguration();
            var propertyNames = typeof(StorageConfiguration).GetProperties().Select(property => property.Name);

            foreach (var propertyName in propertyNames)
            {
                var propNameWithUderScoreBeforeEachUpper = string.Join("", propertyName.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c.ToString() : c.ToString()));
                var envVarToSearch = $"STORAGE_CONTAINER{propNameWithUderScoreBeforeEachUpper}";
                var defaultValue = propertyName.ToLower();
                var settingValue = GetSetting(envVarToSearch, configuration) ?? defaultValue;
                typeof(StorageConfiguration).GetProperty(propertyName)?.SetValue(config, settingValue);
            }

            services.AddSingleton(config);

            return services;
        }

        private static string GetSetting(string setting, IConfiguration configuration) =>
            Environment.GetEnvironmentVariable(setting) ?? configuration[setting];
    }
}

