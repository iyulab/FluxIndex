using FluxIndex.Service.Api.Middleware;
using FluxIndex.Service.Infrastructure.Data;
using FluxIndex.Service.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "FluxIndex Service API",
        Version = "v1",
        Description = "RAG (Retrieval-Augmented Generation) service API for document indexing and semantic search."
    });
    options.AddSecurityDefinition("ApiKey", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-API-Key",
        Description = "API key for authentication"
    });
    options.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

// Add infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRedisCache(builder.Configuration);

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ServiceDbContext>("database");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

// Custom middleware
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

// Ensure database is created in development (use migrations in production)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Create database and enable pgvector extension
        await context.Database.EnsureCreatedAsync();

        // Ensure pgvector extension is enabled
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

app.Run();
