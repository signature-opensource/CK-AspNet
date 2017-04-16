using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.AspNet
{
    public static class ServiceCollectionExtensions
    {
        public static void Replace<TRegisteredType>(this IServiceCollection services, TRegisteredType replacement)
        {
            for (var i = 0; i < services.Count; i++)
            {
                if (services[i].ServiceType == typeof(TRegisteredType))
                {
                    services[i] = new ServiceDescriptor(typeof(TRegisteredType), replacement);
                }
            }
        }
        public static void Replace<TRegisteredType,TNewType>(this IServiceCollection services)
        {
            for (var i = 0; i < services.Count; i++)
            {
                if (services[i].ServiceType == typeof(TRegisteredType))
                {
                    services[i] = new ServiceDescriptor(typeof(TRegisteredType), typeof(TNewType), services[i].Lifetime);
                }
            }
        }
    }
}
