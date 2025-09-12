# FluxIndex 설치 가이드

## 시스템 요구사항

### 필수 요구사항
- **.NET 9.0 SDK** 이상
- **운영체제**: Windows, Linux, macOS
- **메모리**: 최소 512MB (권장 2GB+)

### 선택적 요구사항
- **PostgreSQL 14+** (pgvector 확장 사용 시)
- **SQLite 3.35+** (로컬 스토리지 사용 시)
- **Redis 6.0+** (캐싱 사용 시)
- **OpenAI API 키** 또는 **Azure OpenAI 리소스**

## 설치 방법

### 1. NuGet 패키지 설치

#### 방법 1: .NET CLI
```bash
# 핵심 패키지
dotnet add package FluxIndex.Core
dotnet add package FluxIndex.SDK

# AI Provider (선택)
dotnet add package FluxIndex.AI.OpenAI

# Storage Provider (선택 - 하나 이상 필요)
dotnet add package FluxIndex.Storage.PostgreSQL
dotnet add package FluxIndex.Storage.SQLite

# Cache Provider (선택)
dotnet add package FluxIndex.Cache.Redis
```

#### 방법 2: Package Manager Console
```powershell
Install-Package FluxIndex.Core
Install-Package FluxIndex.SDK
Install-Package FluxIndex.AI.OpenAI
Install-Package FluxIndex.Storage.PostgreSQL
```

#### 방법 3: PackageReference (.csproj)
```xml
<ItemGroup>
  <PackageReference Include="FluxIndex.Core" Version="0.1.0" />
  <PackageReference Include="FluxIndex.SDK" Version="0.1.0" />
  <PackageReference Include="FluxIndex.AI.OpenAI" Version="0.1.0" />
  <PackageReference Include="FluxIndex.Storage.PostgreSQL" Version="0.1.0" />
</ItemGroup>
```

### 2. 프로젝트 템플릿 사용

```bash
# FluxIndex 템플릿 설치
dotnet new install FluxIndex.Templates

# 새 프로젝트 생성
dotnet new fluxindex-console -n MyRAGApp
dotnet new fluxindex-webapi -n MyRAGAPI
dotnet new fluxindex-blazor -n MyRAGUI
```

## Storage Provider 설정

### PostgreSQL + pgvector

#### 1. PostgreSQL 설치
```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install postgresql-14 postgresql-contrib

# macOS (Homebrew)
brew install postgresql@14

# Windows
# PostgreSQL 설치 프로그램 다운로드: https://www.postgresql.org/download/windows/
```

#### 2. pgvector 확장 설치
```bash
# Ubuntu/Debian
sudo apt-get install postgresql-14-pgvector

# macOS
brew install pgvector

# 소스에서 빌드
git clone https://github.com/pgvector/pgvector.git
cd pgvector
make
make install
```

#### 3. 데이터베이스 설정
```sql
-- 데이터베이스 생성
CREATE DATABASE fluxindex;

-- pgvector 확장 활성화
\c fluxindex
CREATE EXTENSION IF NOT EXISTS vector;

-- 테이블 자동 생성 (FluxIndex가 처리)
-- 수동으로 생성하려면:
CREATE TABLE documents (
    id TEXT PRIMARY KEY,
    content TEXT NOT NULL,
    embedding vector(1536),
    metadata JSONB,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 벡터 인덱스 생성 (성능 향상)
CREATE INDEX ON documents USING ivfflat (embedding vector_cosine_ops)
WITH (lists = 100);
```

#### 4. 연결 문자열 설정
```json
{
  "ConnectionStrings": {
    "PostgreSQL": "Host=localhost;Port=5432;Database=fluxindex;Username=postgres;Password=yourpassword"
  }
}
```

### SQLite

#### 1. NuGet 패키지 설치
```bash
dotnet add package FluxIndex.Storage.SQLite
```

#### 2. 설정
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.SQLite, options =>
    {
        options.DatabasePath = "fluxindex.db";
        options.InMemory = false;  // true면 메모리 DB
    })
    .Build();
```

### In-Memory Storage

개발 및 테스트용으로 적합합니다.

```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureVectorStore(VectorStoreType.InMemory)
    .Build();
```

## AI Provider 설정

### OpenAI

#### 1. API 키 발급
[OpenAI Platform](https://platform.openai.com/api-keys)에서 API 키 발급

#### 2. 환경 변수 설정
```bash
# Windows
setx OPENAI_API_KEY "sk-..."

# Linux/macOS
export OPENAI_API_KEY="sk-..."
```

#### 3. 코드에서 설정
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureEmbeddingService(config =>
    {
        config.UseOpenAI(
            apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
            model: "text-embedding-ada-002"  // 또는 "text-embedding-3-small"
        );
    })
    .Build();
```

### Azure OpenAI

#### 1. Azure 리소스 생성
Azure Portal에서 OpenAI 리소스 생성 및 모델 배포

#### 2. 설정
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureEmbeddingService(config =>
    {
        config.UseAzureOpenAI(
            endpoint: "https://your-resource.openai.azure.com/",
            apiKey: Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY"),
            deploymentName: "text-embedding-ada-002"
        );
    })
    .Build();
```

## Cache Provider 설정

### Redis

#### 1. Redis 설치
```bash
# Docker
docker run -d -p 6379:6379 redis:latest

# Ubuntu/Debian
sudo apt-get install redis-server

# macOS
brew install redis
```

#### 2. 설정
```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureCache(CacheType.Redis, options =>
    {
        options.ConnectionString = "localhost:6379";
        options.KeyPrefix = "fluxindex:";
        options.DefaultExpiration = TimeSpan.FromHours(1);
    })
    .Build();
```

### In-Memory Cache

```csharp
var client = new FluxIndexClientBuilder()
    .ConfigureCache(CacheType.InMemory, options =>
    {
        options.SizeLimit = 1000;  // 최대 항목 수
        options.DefaultExpiration = TimeSpan.FromMinutes(30);
    })
    .Build();
```

## 구성 파일 설정

### appsettings.json

```json
{
  "FluxIndex": {
    "Storage": {
      "Type": "PostgreSQL",
      "ConnectionString": "Host=localhost;Database=fluxindex;Username=postgres;Password=pass",
      "VectorDimension": 1536,
      "CreateIndexIfNotExists": true
    },
    "Embedding": {
      "Provider": "OpenAI",
      "OpenAI": {
        "ApiKey": "${OPENAI_API_KEY}",
        "Model": "text-embedding-ada-002",
        "MaxRetries": 3,
        "TimeoutSeconds": 30
      }
    },
    "Cache": {
      "Type": "Redis",
      "ConnectionString": "localhost:6379",
      "ExpirationMinutes": 60
    },
    "Search": {
      "DefaultTopK": 10,
      "MinimumScore": 0.7,
      "UseReranking": true,
      "HybridSearch": {
        "VectorWeight": 0.7,
        "KeywordWeight": 0.3
      }
    },
    "Indexing": {
      "BatchSize": 100,
      "ParallelDegree": 4,
      "MaxDocumentSize": 1048576
    }
  }
}
```

### 환경별 설정

#### appsettings.Development.json
```json
{
  "FluxIndex": {
    "Storage": {
      "Type": "InMemory"
    },
    "Embedding": {
      "Provider": "Local"
    },
    "Cache": {
      "Type": "InMemory"
    }
  }
}
```

#### appsettings.Production.json
```json
{
  "FluxIndex": {
    "Storage": {
      "Type": "PostgreSQL",
      "ConnectionString": "${DATABASE_URL}"
    },
    "Embedding": {
      "Provider": "AzureOpenAI",
      "AzureOpenAI": {
        "Endpoint": "${AZURE_OPENAI_ENDPOINT}",
        "ApiKey": "${AZURE_OPENAI_KEY}",
        "DeploymentName": "production-embeddings"
      }
    },
    "Cache": {
      "Type": "Redis",
      "ConnectionString": "${REDIS_URL}"
    }
  }
}
```

## Dependency Injection 설정

### ASP.NET Core

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// FluxIndex 서비스 등록
builder.Services.AddFluxIndex(builder.Configuration, options =>
{
    options.Storage.Type = VectorStoreType.PostgreSQL;
    options.Embedding.Provider = "OpenAI";
    options.Cache.Type = CacheType.Redis;
});

// 또는 개별 등록
builder.Services.AddFluxIndexCore();
builder.Services.AddFluxIndexOpenAI(builder.Configuration);
builder.Services.AddFluxIndexPostgreSQL(builder.Configuration);
builder.Services.AddFluxIndexRedisCache(builder.Configuration);

var app = builder.Build();

// 데이터베이스 마이그레이션 (선택)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FluxIndexDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

### 콘솔 애플리케이션

```csharp
// Program.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddFluxIndex(context.Configuration);
        services.AddHostedService<RAGWorker>();
    })
    .Build();

await host.RunAsync();
```

## 도커 설정

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["MyRAGApp.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyRAGApp.dll"]
```

### docker-compose.yml

```yaml
version: '3.8'

services:
  app:
    build: .
    ports:
      - "8080:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - OPENAI_API_KEY=${OPENAI_API_KEY}
      - ConnectionStrings__PostgreSQL=Host=postgres;Database=fluxindex;Username=postgres;Password=postgres
      - ConnectionStrings__Redis=redis:6379
    depends_on:
      - postgres
      - redis

  postgres:
    image: pgvector/pgvector:pg14
    environment:
      - POSTGRES_DB=fluxindex
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
    volumes:
      - postgres_data:/var/lib/postgresql/data
    ports:
      - "5432:5432"

  redis:
    image: redis:alpine
    ports:
      - "6379:6379"
    volumes:
      - redis_data:/data

volumes:
  postgres_data:
  redis_data:
```

## 문제 해결

### 일반적인 문제

#### 1. "Unable to load DLL 'vector'"
pgvector가 설치되지 않았습니다.
```sql
CREATE EXTENSION IF NOT EXISTS vector;
```

#### 2. "OpenAI API key not found"
환경 변수를 확인하세요.
```bash
echo $OPENAI_API_KEY  # Linux/macOS
echo %OPENAI_API_KEY%  # Windows
```

#### 3. "Connection refused" (PostgreSQL)
PostgreSQL 서비스가 실행 중인지 확인:
```bash
sudo systemctl status postgresql  # Linux
brew services list  # macOS
```

#### 4. "Dimension mismatch"
임베딩 차원이 일치하지 않습니다. 설정 확인:
```csharp
options.VectorDimension = 1536;  // OpenAI ada-002
// options.VectorDimension = 384;  // all-MiniLM-L6-v2
```

### 성능 최적화

#### 1. PostgreSQL 튜닝
```sql
-- postgresql.conf
shared_buffers = 256MB
work_mem = 4MB
maintenance_work_mem = 64MB
effective_cache_size = 1GB
```

#### 2. 인덱스 최적화
```sql
-- IVFFlat 인덱스 (빠른 인덱싱)
CREATE INDEX ON documents USING ivfflat (embedding vector_cosine_ops)
WITH (lists = 100);

-- HNSW 인덱스 (더 정확한 검색)
CREATE INDEX ON documents USING hnsw (embedding vector_cosine_ops)
WITH (m = 16, ef_construction = 64);
```

#### 3. 연결 풀 설정
```csharp
services.AddDbContext<FluxIndexDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseVector();
        npgsqlOptions.CommandTimeout(30);
    });
}, ServiceLifetime.Scoped);
```

## 다음 단계

- [빠른 시작 가이드](./getting-started.md)
- [API 레퍼런스](./api-reference.md)
- [아키텍처 가이드](./architecture.md)