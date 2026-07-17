using System;
using MessageBrokerRabbitMqExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Feed.Infra.Providers.MessageBrokers.RabbitMq;
using MessageBrokerRabbitMq;
using Microsoft.AspNetCore.Builder;

namespace Feed.Infra.Providers.MessageBrokers
{
    public static class MessageBrokersExtensions
    {
        public static IServiceCollection ConfigureMessageBroker(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureRabbitMq(configuration);
            
            return services;
        }

        public static void ConfigureConsumers(IApplicationBuilder app)
        {
            RabbitMqBrokerExtensions.ConfigureConsumers(app);
        }
    }
}
