using FluxIndex.Core.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace FluxIndex.Core.Application.Services;

public partial class RetrievalVerificationService
{

    [LoggerMessage(Level = LogLevel.Debug, Message = "Verifying {Count} documents for query: {Query}")]
    private static partial void LogRetrievalVerification4(ILogger logger, int count, string query);
    [LoggerMessage(Level = LogLevel.Information, Message = "Verification complete: {Status}, confidence: {Confidence:F3}, {RelevantCount}/{TotalCount} relevant")]
    private static partial void LogRetrievalVerification3(ILogger logger, VerificationStatus status, double confidence, double relevantCount, int totalCount);
    [LoggerMessage(Level = LogLevel.Error, Message = "Verification failed for query: {Query}")]
    private static partial void LogRetrievalVerification2(ILogger logger, Exception exception, string query);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to grade document {DocumentId}")]
    private static partial void LogRetrievalVerification1(ILogger logger, Exception exception, string documentId);

}
