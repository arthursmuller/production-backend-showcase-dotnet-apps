using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Reflection;

namespace Cqrs.Extensions;

public static class CqrsExtensions
{
    public static void RegisterCQRS(this IServiceCollection services, Assembly assembly)
    {
        var commandQueryTypes = assembly.GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsAbstract &&
                        typeof(ICommandQuery).IsAssignableFrom(t));

        foreach (var type in commandQueryTypes)
            services.AddScoped(type);
    }
}

