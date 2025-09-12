using FluxIndex;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Create service collection and configure services
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddFluxIndex();

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Get FluxIndex client
var fluxClient = serviceProvider.GetRequiredService<FluxIndexClient>();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Testing FluxIndex NuGet package...");

// Test adding documents
fluxClient.AddDocument("This is a test document about machine learning and artificial intelligence.");
fluxClient.AddDocument("Another document discussing natural language processing and deep learning.");
fluxClient.AddDocument("A third document about computer vision and image recognition.");

// Test searching
var results = fluxClient.Search("machine learning");
logger.LogInformation("Search Results: {Results}", results);

logger.LogInformation("FluxIndex package test completed successfully!");