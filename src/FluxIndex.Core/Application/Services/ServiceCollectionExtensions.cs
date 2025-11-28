using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Application.Models;
using FluxIndex.Core.Application.Services.Quantization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

/// <summary>
/// 메타데이터 증강 서비스 등록 확장 메서드
/// </summary>
public static class MetadataAugmentationServiceExtensions
{
    /// <summary>
    /// Contextual Header 생성 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddContextualHeaderGenerator(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ContextualHeaderOptions>(_ => { });
        }

        // 생성기 등록
        services.AddScoped<IContextualHeaderGenerator, HybridContextualHeaderGenerator>();

        return services;
    }

    /// <summary>
    /// 메타데이터 증강 전체 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddMetadataAugmentation(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? configureOptions = null)
    {
        services.AddContextualHeaderGenerator(configureOptions);

        return services;
    }

    /// <summary>
    /// 청크 분류 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddChunkClassification(
        this IServiceCollection services,
        Action<ClassificationOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ClassificationOptions>(_ => { });
        }

        // 검증 서비스 등록
        services.AddScoped<IClassificationValidationService, ClassificationValidationService>();

        // 분류 서비스 등록
        services.AddScoped<IChunkClassificationService, LlmChunkClassificationService>();

        return services;
    }

    /// <summary>
    /// 전체 증강 서비스 등록 (Header + Classification)
    /// </summary>
    public static IServiceCollection AddFullAugmentation(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? headerOptions = null,
        Action<ClassificationOptions>? classificationOptions = null)
    {
        services.AddContextualHeaderGenerator(headerOptions);
        services.AddChunkClassification(classificationOptions);

        return services;
    }

    /// <summary>
    /// 토큰 예산 기반 검색 서비스 등록
    /// </summary>
    public static IServiceCollection AddTokenAwareSearch(this IServiceCollection services)
    {
        services.AddSingleton<ITokenCounter, SimpleTokenCounter>();
        services.AddScoped<IQueryAnalysisService, QueryAnalysisService>();
        services.AddScoped<ITokenAwareSearchService, TokenAwareSearchService>();

        return services;
    }

    /// <summary>
    /// 그래프 탐색 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddGraphTraversal(this IServiceCollection services)
    {
        services.AddScoped<IGraphTraversalService, GraphTraversalService>();

        return services;
    }

    /// <summary>
    /// 벡터 양자화 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">양자화 옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddVectorQuantization(
        this IServiceCollection services,
        Action<QuantizationOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<QuantizationOptions>(_ => { });
        }

        // 양자화 타입에 따라 적절한 구현체 등록
        services.AddSingleton<IVectorQuantizer>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<QuantizationOptions>>();
            var loggerFactory = sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>();

            return options.Value.Type switch
            {
                QuantizationType.Binary => new BinaryQuantizer(
                    options,
                    loggerFactory.CreateLogger<BinaryQuantizer>()),

                QuantizationType.ProductQuantization or
                QuantizationType.OptimizedProductQuantization => new ProductQuantizer(
                    options,
                    loggerFactory.CreateLogger<ProductQuantizer>()),

                // 기본값은 Scalar Quantization
                _ => new ScalarQuantizer(
                    options,
                    loggerFactory.CreateLogger<ScalarQuantizer>())
            };
        });

        return services;
    }

    /// <summary>
    /// Scalar Quantization 서비스 등록 (Int8 기본)
    /// </summary>
    public static IServiceCollection AddScalarQuantization(
        this IServiceCollection services,
        int dimension = 1536,
        QuantizationType type = QuantizationType.ScalarInt8)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = type;
        });
    }

    /// <summary>
    /// Product Quantization 서비스 등록
    /// </summary>
    public static IServiceCollection AddProductQuantization(
        this IServiceCollection services,
        int dimension = 1536,
        int numSubvectors = 8,
        int codebookSize = 256)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = QuantizationType.ProductQuantization;
            options.NumSubvectors = numSubvectors;
            options.CodebookSize = codebookSize;
        });
    }

    /// <summary>
    /// Binary Quantization 서비스 등록 (최대 압축)
    /// </summary>
    public static IServiceCollection AddBinaryQuantization(
        this IServiceCollection services,
        int dimension = 1536)
    {
        return services.AddVectorQuantization(options =>
        {
            options.Dimension = dimension;
            options.Type = QuantizationType.Binary;
        });
    }

    /// <summary>
    /// FluxIndex Core 전체 서비스 등록
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="headerOptions">Contextual Header 옵션</param>
    /// <param name="classificationOptions">분류 옵션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddFluxIndexCore(
        this IServiceCollection services,
        Action<ContextualHeaderOptions>? headerOptions = null,
        Action<ClassificationOptions>? classificationOptions = null)
    {
        services.AddFullAugmentation(headerOptions, classificationOptions);
        services.AddTokenAwareSearch();
        services.AddGraphTraversal();

        return services;
    }

    /// <summary>
    /// Quantized Vector Store Decorator 등록.
    /// 기존 IVectorStore 구현체를 래핑하여 양자화 기능을 추가합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="configureOptions">데코레이터 옵션 설정 액션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQuantizedVectorStoreDecorator(
        this IServiceCollection services,
        Action<QuantizedVectorStoreOptions>? configureOptions = null)
    {
        // 옵션 설정
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<QuantizedVectorStoreOptions>(_ => { });
        }

        // 기존 IVectorStore 등록을 찾아서 데코레이터로 래핑
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IVectorStore));
        if (descriptor != null)
        {
            services.Remove(descriptor);

            services.Add(new ServiceDescriptor(
                typeof(IVectorStore),
                sp =>
                {
                    // 원본 구현체 생성
                    IVectorStore innerStore;
                    if (descriptor.ImplementationFactory != null)
                    {
                        innerStore = (IVectorStore)descriptor.ImplementationFactory(sp);
                    }
                    else if (descriptor.ImplementationInstance != null)
                    {
                        innerStore = (IVectorStore)descriptor.ImplementationInstance;
                    }
                    else if (descriptor.ImplementationType != null)
                    {
                        innerStore = (IVectorStore)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
                    }
                    else
                    {
                        throw new InvalidOperationException("Cannot resolve inner IVectorStore implementation.");
                    }

                    var quantizer = sp.GetRequiredService<IVectorQuantizer>();
                    var logger = sp.GetRequiredService<ILogger<QuantizedVectorStoreDecorator>>();
                    var options = sp.GetService<Microsoft.Extensions.Options.IOptions<QuantizedVectorStoreOptions>>()?.Value;
                    return new QuantizedVectorStoreDecorator(innerStore, quantizer, logger, options);
                },
                descriptor.Lifetime));
        }

        // IQuantizedVectorStore 인터페이스도 동일한 인스턴스로 등록
        services.AddScoped<IQuantizedVectorStore>(sp =>
        {
            var store = sp.GetRequiredService<IVectorStore>();
            if (store is IQuantizedVectorStore quantizedStore)
            {
                return quantizedStore;
            }
            throw new InvalidOperationException(
                "IVectorStore is not decorated with QuantizedVectorStoreDecorator. " +
                "Ensure AddQuantizedVectorStoreDecorator is called after registering the base IVectorStore.");
        });

        return services;
    }

    /// <summary>
    /// Quantized Vector Store Decorator 등록 (간단한 설정).
    /// 먼저 AddVectorQuantization 또는 AddScalarQuantization 등을 호출해야 합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <param name="autoQuantize">저장 시 자동 양자화 여부</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddQuantizedVectorStoreDecorator(
        this IServiceCollection services,
        bool autoQuantize = true)
    {
        return services.AddQuantizedVectorStoreDecorator(options =>
        {
            options.AutoQuantizeOnStore = autoQuantize;
        });
    }

    /// <summary>
    /// 벡터 양자화 마이그레이션 서비스 등록.
    /// 기존 벡터를 양자화 형식으로 일괄 변환하는 서비스입니다.
    /// IVectorStore와 IVectorQuantizer가 먼저 등록되어 있어야 합니다.
    /// </summary>
    /// <param name="services">서비스 컬렉션</param>
    /// <returns>서비스 컬렉션</returns>
    public static IServiceCollection AddVectorQuantizationMigration(
        this IServiceCollection services)
    {
        services.AddScoped<VectorQuantizationMigrationService>();
        return services;
    }
}
