using FluxIndex.Stack.Api.BackgroundServices;
using FluxIndex.Stack.Api.Mcp;
using FluxIndex.Stack.Api.Middleware;
using FluxIndex.Stack.Api.Observability;
using FluxIndex.Stack.Application.Interfaces.Services;
using FluxIndex.Stack.Infrastructure.Data;
using FluxIndex.Stack.Infrastructure.Extensions;
using FluxIndex.Stack.Vault.Extensions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

// Add MCP Server with HTTP transport (Anthropic MCP Protocol compliant)
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Register MCP tools with DI (FluxIndexTools needs services injected)
builder.Services.AddScoped<FluxIndexTools>();

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });
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

// Add Vault services (file system synchronization)
builder.Services.AddFluxIndexVault(builder.Configuration);

// Add OpenTelemetry observability (metrics + tracing)
builder.Services.AddFluxIndexObservability(builder.Configuration);

// Add background services
builder.Services.AddHostedService<IndexingBackgroundService>();
builder.Services.AddHostedService<VaultBackgroundService>();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:6101"])
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

// OpenTelemetry Prometheus metrics endpoint (/metrics)
app.UseFluxIndexObservability();

// Map MCP endpoints (SSE transport at /sse and /message)
app.MapMcp();

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

        // Ensure IndexingJobLogs table exists (for schema upgrades)
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""IndexingJobLogs"" (
                ""Id"" uuid NOT NULL,
                ""JobId"" uuid NOT NULL,
                ""Level"" integer NOT NULL,
                ""Message"" character varying(1000) NOT NULL,
                ""Details"" character varying(4000),
                ""Phase"" character varying(100),
                ""ChunkIndex"" integer,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                CONSTRAINT ""PK_IndexingJobLogs"" PRIMARY KEY (""Id""),
                CONSTRAINT ""FK_IndexingJobLogs_IndexingJobs_JobId"" FOREIGN KEY (""JobId"")
                    REFERENCES ""IndexingJobs"" (""Id"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""IX_IndexingJobLogs_JobId"" ON ""IndexingJobLogs"" (""JobId"");
            CREATE INDEX IF NOT EXISTS ""IX_IndexingJobLogs_CreatedAt"" ON ""IndexingJobLogs"" (""CreatedAt"");
        ");

        // Ensure AiProviderSettings table exists (for schema upgrades)
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""AiProviderSettings"" (
                ""Id"" uuid NOT NULL,
                ""ProviderName"" character varying(50) NOT NULL,
                ""DisplayName"" character varying(100) NOT NULL,
                ""ApiKey"" character varying(500),
                ""IsEnabled"" boolean NOT NULL DEFAULT false,
                ""IsDefaultEmbedding"" boolean NOT NULL DEFAULT false,
                ""IsDefaultLlm"" boolean NOT NULL DEFAULT false,
                ""EmbeddingModel"" character varying(100),
                ""LlmModel"" character varying(100),
                ""EndpointUrl"" character varying(500),
                ""AdditionalConfig"" jsonb,
                ""CreatedAt"" timestamp with time zone NOT NULL,
                ""UpdatedAt"" timestamp with time zone NOT NULL,
                CONSTRAINT ""PK_AiProviderSettings"" PRIMARY KEY (""Id"")
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AiProviderSettings_ProviderName"" ON ""AiProviderSettings"" (""ProviderName"");
            CREATE INDEX IF NOT EXISTS ""IX_AiProviderSettings_IsDefaultEmbedding"" ON ""AiProviderSettings"" (""IsDefaultEmbedding"");
            CREATE INDEX IF NOT EXISTS ""IX_AiProviderSettings_IsDefaultLlm"" ON ""AiProviderSettings"" (""IsDefaultLlm"");
        ");

        // Initialize default AI providers
        var settingsService = scope.ServiceProvider.GetRequiredService<IAiProviderSettingsService>();
        await settingsService.InitializeDefaultProvidersAsync();

        logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize database");
        throw;
    }
}

app.Run();
