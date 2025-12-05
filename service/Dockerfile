# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files
COPY src/FluxIndex.Service.Domain/FluxIndex.Service.Domain.csproj FluxIndex.Service.Domain/
COPY src/FluxIndex.Service.Shared/FluxIndex.Service.Shared.csproj FluxIndex.Service.Shared/
COPY src/FluxIndex.Service.Application/FluxIndex.Service.Application.csproj FluxIndex.Service.Application/
COPY src/FluxIndex.Service.Infrastructure/FluxIndex.Service.Infrastructure.csproj FluxIndex.Service.Infrastructure/
COPY src/FluxIndex.Service.Api/FluxIndex.Service.Api.csproj FluxIndex.Service.Api/

# Copy FluxIndex library project files (relative to service directory)
COPY ../src/FluxIndex.Core/FluxIndex.Core.csproj ../src/FluxIndex.Core/
COPY ../src/FluxIndex.SDK/FluxIndex.SDK.csproj ../src/FluxIndex.SDK/
COPY ../src/FluxIndex.Storage.PostgreSQL/FluxIndex.Storage.PostgreSQL.csproj ../src/FluxIndex.Storage.PostgreSQL/
COPY ../src/FluxIndex.AI.OpenAI/FluxIndex.AI.OpenAI.csproj ../src/FluxIndex.AI.OpenAI/
COPY ../src/FluxIndex.AI.LocalEmbedder/FluxIndex.AI.LocalEmbedder.csproj ../src/FluxIndex.AI.LocalEmbedder/
COPY ../src/FluxIndex.AI.LocalReranker/FluxIndex.AI.LocalReranker.csproj ../src/FluxIndex.AI.LocalReranker/

# Restore dependencies
RUN dotnet restore FluxIndex.Service.Api/FluxIndex.Service.Api.csproj

# Copy all source files
COPY src/ .
COPY ../src/ ../src/

# Build the application
RUN dotnet build FluxIndex.Service.Api/FluxIndex.Service.Api.csproj -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish FluxIndex.Service.Api/FluxIndex.Service.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

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

ENTRYPOINT ["dotnet", "FluxIndex.Service.Api.dll"]
