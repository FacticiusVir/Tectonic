using Microsoft.Extensions.DependencyInjection;
using System;

namespace Tectonic
{
    public static class ServiceProviderExtensions
    {
        public static T CreateInstance<T>(this IServiceProvider provider, params object[] parameters)
        {
            return ActivatorUtilities.CreateInstance<T>(provider, parameters);
        }
    }
}
