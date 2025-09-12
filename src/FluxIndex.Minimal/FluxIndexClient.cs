using System;
using Microsoft.Extensions.DependencyInjection;

namespace FluxIndex
{
    public class FluxIndexClient
    {
        public FluxIndexClient() { }
        
        public void AddDocument(string content) 
        {
            Console.WriteLine($"Document added: {content.Substring(0, Math.Min(50, content.Length))}...");
        }
        
        public string Search(string query)
        {
            return $"Search results for: {query}";
        }
    }
    
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddFluxIndex(this IServiceCollection services)
        {
            services.AddSingleton<FluxIndexClient>();
            return services;
        }
    }
}
