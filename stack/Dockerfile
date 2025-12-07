# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution files
COPY FluxIndex.sln .
COPY Directory.Build.props .
COPY Directory.Packages.props .

# Copy Stack project files
COPY stack/src/FluxIndex.Stack.Domain/FluxIndex.Stack.Domain.csproj stack/src/FluxIndex.Stack.Domain/
COPY stack/src/FluxIndex.Stack.Shared/FluxIndex.Stack.Shared.csproj stack/src/FluxIndex.Stack.Shared/
COPY stack/src/FluxIndex.Stack.Application/FluxIndex.Stack.Application.csproj stack/src/FluxIndex.Stack.Application/
COPY stack/src/FluxIndex.Stack.Infrastructure/FluxIndex.Stack.Infrastructure.csproj stack/src/FluxIndex.Stack.Infrastructure/
COPY stack/src/FluxIndex.Stack.Api/FluxIndex.Stack.Api.csproj stack/src/FluxIndex.Stack.Api/

# Copy FluxIndex library project files
COPY src/FluxIndex.Core/FluxIndex.Core.csproj src/FluxIndex.Core/
COPY src/FluxIndex.SDK/FluxIndex.SDK.csproj src/FluxIndex.SDK/
COPY src/FluxIndex.Storage.PostgreSQL/FluxIndex.Storage.PostgreSQL.csproj src/FluxIndex.Storage.PostgreSQL/
COPY src/FluxIndex.AI.OpenAI/FluxIndex.AI.OpenAI.csproj src/FluxIndex.AI.OpenAI/
COPY src/FluxIndex.AI.LocalEmbedder/FluxIndex.AI.LocalEmbedder.csproj src/FluxIndex.AI.LocalEmbedder/
COPY src/FluxIndex.AI.LocalReranker/FluxIndex.AI.LocalReranker.csproj src/FluxIndex.AI.LocalReranker/
COPY src/FluxIndex.Cache.Redis/FluxIndex.Cache.Redis.csproj src/FluxIndex.Cache.Redis/

# Restore dependencies
RUN dotnet restore stack/src/FluxIndex.Stack.Api/FluxIndex.Stack.Api.csproj

# Copy all source files
COPY stack/src/ stack/src/
COPY src/ src/

# Build the application
RUN dotnet build stack/src/FluxIndex.Stack.Api/FluxIndex.Stack.Api.csproj -c Release -o /app/build --no-restore

# Publish stage
FROM build AS publish
RUN dotnet publish stack/src/FluxIndex.Stack.Api/FluxIndex.Stack.Api.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# Create non-root user
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FluxIndex.Stack.Api.dll"]
