using FluxIndex.Core.Application.Interfaces;
using FluxIndex.Core.Domain.Entities;
using FluxIndex.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RealQualityTest;

/// <summary>
/// RAG 품질 평가 시스템 사용 예제
/// </summary>
public class EvaluationExample
{
    public static async Task RunEvaluationExample()
    {
        Console.WriteLine("🔍 FluxIndex RAG 품질 평가 시스템 예제");
        Console.WriteLine("=====================================");

        try
        {
            // 1. FluxIndex 클라이언트 설정 (평가 시스템 포함)
            var client = new FluxIndexClientBuilder()
                .UseSQLiteInMemory()
                .UseOpenAI(Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "test-key")
                .WithEvaluationSystemForDevelopment()
                .WithLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Information))
                .Build();

            // 2. 평가 서비스 가져오기
            var serviceProvider = (client as dynamic)?._serviceProvider;
            var evaluationService = serviceProvider?.GetService<IRAGEvaluationService>();
            var datasetManager = serviceProvider?.GetService<IGoldenDatasetManager>();
            var qualityGateService = serviceProvider?.GetService<IQualityGateService>();

            if (evaluationService == null || datasetManager == null || qualityGateService == null)
            {
                Console.WriteLine("❌ 평가 서비스 초기화 실패");
                return;
            }

            Console.WriteLine("✅ 평가 시스템 초기화 완료");

            // 3. 골든 데이터셋 생성
            await CreateSampleGoldenDataset(datasetManager);

            // 4. 단일 쿼리 평가 예제
            await RunSingleQueryEvaluation(evaluationService);

            // 5. 배치 평가 예제
            await RunBatchEvaluation(evaluationService, datasetManager);

            // 6. 품질 게이트 예제
            await RunQualityGateExample(qualityGateService);

            Console.WriteLine("\n🎉 모든 평가 예제 완료!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 오류 발생: {ex.Message}");
        }
    }

    private static async Task CreateSampleGoldenDataset(IGoldenDatasetManager datasetManager)
    {
        Console.WriteLine("\n📝 골든 데이터셋 생성 중...");

        var sampleDataset = new List<GoldenDatasetItem>
        {
            new GoldenDatasetItem
            {
                Id = "q1",
                Query = "머신러닝이란 무엇인가요?",
                ExpectedAnswer = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 자동으로 학습하고 예측하는 기술입니다.",
                RelevantChunkIds = new List<string> { "chunk_ml_1", "chunk_ml_2" },
                Difficulty = EvaluationDifficulty.Easy,
                Categories = new List<string> { "기술", "인공지능" },
                Weight = 1.0
            },
            new GoldenDatasetItem
            {
                Id = "q2",
                Query = "딥러닝과 머신러닝의 차이점은 무엇인가요?",
                ExpectedAnswer = "딥러닝은 머신러닝의 하위 분야로, 인공신경망을 사용하여 더 복잡한 패턴을 학습할 수 있습니다.",
                RelevantChunkIds = new List<string> { "chunk_dl_1", "chunk_ml_3" },
                Difficulty = EvaluationDifficulty.Medium,
                Categories = new List<string> { "기술", "인공지능", "비교" },
                Weight = 1.2
            },
            new GoldenDatasetItem
            {
                Id = "q3",
                Query = "자연어 처리에서 트랜스포머 모델의 주요 특징과 장점을 설명해주세요.",
                ExpectedAnswer = "트랜스포머는 어텐션 메커니즘을 사용하여 순차 데이터를 병렬로 처리할 수 있으며, 긴 문맥을 효과적으로 학습할 수 있습니다.",
                RelevantChunkIds = new List<string> { "chunk_transformer_1", "chunk_attention_1", "chunk_nlp_1" },
                Difficulty = EvaluationDifficulty.Hard,
                Categories = new List<string> { "기술", "자연어처리", "딥러닝" },
                Weight = 1.5
            }
        };

        await datasetManager.SaveDatasetAsync("sample_dataset", sampleDataset);

        var statistics = await datasetManager.GetDatasetStatisticsAsync("sample_dataset");
        Console.WriteLine($"✅ 골든 데이터셋 생성 완료: {statistics.TotalQueries}개 쿼리");
        Console.WriteLine($"   카테고리: {string.Join(", ", statistics.CategoryCounts.Keys)}");
        Console.WriteLine($"   난이도 분포: {string.Join(", ", statistics.DifficultyCounts)}");
    }

    private static async Task RunSingleQueryEvaluation(IRAGEvaluationService evaluationService)
    {
        Console.WriteLine("\n🔍 단일 쿼리 평가 예제");

        var query = "머신러닝이란 무엇인가요?";
        var retrievedChunks = CreateMockRetrievedChunks();
        var generatedAnswer = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 자동으로 학습하고 예측하는 기술입니다.";
        var goldenItem = new GoldenDatasetItem
        {
            Id = "eval_q1",
            Query = query,
            ExpectedAnswer = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 자동으로 학습하고 예측하는 기술입니다.",
            RelevantChunkIds = new List<string> { "chunk_1", "chunk_2" }
        };

        var config = new EvaluationConfiguration
        {
            EnableFaithfulnessEvaluation = false, // OpenAI API 키가 없는 경우
            EnableAnswerRelevancyEvaluation = false,
            EnableContextEvaluation = true
        };

        var result = await evaluationService.EvaluateQueryAsync(
            query, retrievedChunks, generatedAnswer, goldenItem, config);

        Console.WriteLine($"📊 평가 결과:");
        Console.WriteLine($"   Precision: {result.Precision:F3}");
        Console.WriteLine($"   Recall: {result.Recall:F3}");
        Console.WriteLine($"   F1 Score: {result.F1Score:F3}");
        Console.WriteLine($"   MRR: {result.MRR:F3}");
        Console.WriteLine($"   NDCG: {result.NDCG:F3}");
        Console.WriteLine($"   Hit Rate: {result.HitRate:F3}");
        Console.WriteLine($"   평가 시간: {result.Duration.TotalMilliseconds:F1}ms");
    }

    private static async Task RunBatchEvaluation(IRAGEvaluationService evaluationService, IGoldenDatasetManager datasetManager)
    {
        Console.WriteLine("\n📊 배치 평가 예제");

        var dataset = await datasetManager.LoadDatasetAsync("sample_dataset");
        var config = new EvaluationConfiguration
        {
            EnableFaithfulnessEvaluation = false,
            EnableAnswerRelevancyEvaluation = false,
            EnableContextEvaluation = true,
            Timeout = TimeSpan.FromMinutes(5)
        };

        var batchResult = await evaluationService.EvaluateBatchAsync(dataset, config);

        Console.WriteLine($"📈 배치 평가 결과:");
        Console.WriteLine($"   총 쿼리: {batchResult.TotalQueries}개");
        Console.WriteLine($"   성공: {batchResult.SuccessfulQueries}개");
        Console.WriteLine($"   실패: {batchResult.FailedQueries}개");
        Console.WriteLine($"   성공률: {batchResult.SuccessRate:P1}");
        Console.WriteLine($"   평균 Precision: {batchResult.AveragePrecision:F3}");
        Console.WriteLine($"   평균 Recall: {batchResult.AverageRecall:F3}");
        Console.WriteLine($"   평균 F1 Score: {batchResult.AverageF1Score:F3}");
        Console.WriteLine($"   평균 응답시간: {batchResult.AverageQueryDuration:F1}ms");
        Console.WriteLine($"   총 실행시간: {batchResult.TotalDuration.TotalSeconds:F1}초");
    }

    private static async Task RunQualityGateExample(IQualityGateService qualityGateService)
    {
        Console.WriteLine("\n🚪 품질 게이트 예제");

        var thresholds = new QualityThresholds
        {
            MinPrecision = 0.7,
            MinRecall = 0.7,
            MinF1Score = 0.7,
            MinMRR = 0.7,
            MinNDCG = 0.7,
            MinHitRate = 0.8,
            MinFaithfulness = 0.8,
            MinAnswerRelevancy = 0.7,
            MinContextRelevancy = 0.7,
            MaxAcceptableLatency = 2000
        };

        var qualityGateResult = await qualityGateService.ExecuteQualityGateAsync(
            "v1.0.0-test", "sample_dataset", thresholds);

        Console.WriteLine($"🎯 품질 게이트 결과:");
        Console.WriteLine($"   시스템 버전: {qualityGateResult.SystemVersion}");
        Console.WriteLine($"   통과 여부: {(qualityGateResult.Passed ? "✅ PASSED" : "❌ FAILED")}");
        Console.WriteLine($"   실행 시간: {qualityGateResult.ExecutedAt:yyyy-MM-dd HH:mm:ss}");

        if (!qualityGateResult.Passed)
        {
            Console.WriteLine($"   실패한 기준:");
            foreach (var criterion in qualityGateResult.FailedCriteria)
            {
                Console.WriteLine($"     - {criterion}");
            }
        }

        // 요약 정보 출력
        var summary = qualityGateResult.Summary;
        if (summary.ContainsKey("evaluation_summary"))
        {
            Console.WriteLine($"   평가 요약: {summary["evaluation_summary"]}");
        }
    }

    private static List<DocumentChunk> CreateMockRetrievedChunks()
    {
        return new List<DocumentChunk>
        {
            new DocumentChunk
            {
                Id = "chunk_1",
                Content = "머신러닝은 인공지능의 한 분야로, 컴퓨터가 데이터를 통해 학습하는 기술입니다.",
                DocumentId = "doc_ai_basics",
                StartPosition = 0,
                EndPosition = 100,
                Embedding = new float[384] // Mock embedding
            },
            new DocumentChunk
            {
                Id = "chunk_2",
                Content = "딥러닝은 머신러닝의 하위 분야로, 신경망을 사용하여 복잡한 패턴을 학습합니다.",
                DocumentId = "doc_ai_basics",
                StartPosition = 100,
                EndPosition = 200,
                Embedding = new float[384]
            },
            new DocumentChunk
            {
                Id = "chunk_3",
                Content = "자연어 처리는 컴퓨터가 인간의 언어를 이해하고 처리하는 분야입니다.",
                DocumentId = "doc_nlp",
                StartPosition = 0,
                EndPosition = 100,
                Embedding = new float[384]
            }
        };
    }
}