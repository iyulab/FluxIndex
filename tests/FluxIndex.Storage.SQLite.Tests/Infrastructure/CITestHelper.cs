using System;
using Xunit;

namespace FluxIndex.Storage.SQLite.Tests.Infrastructure;

/// <summary>
/// Helper class for CI environment-dependent tests
/// </summary>
public static class CITestHelper
{
    private static bool? _isCIEnvironment;
    private static bool? _isSqliteVecAvailable;

    /// <summary>
    /// Check if running in CI environment
    /// </summary>
    public static bool IsCIEnvironment()
    {
        if (_isCIEnvironment.HasValue)
            return _isCIEnvironment.Value;

        // Check for common CI environment variables
        _isCIEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_PIPELINES")) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAVIS")) ||
                          !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRCLECI"));

        return _isCIEnvironment.Value;
    }

    /// <summary>
    /// Check if sqlite-vec should be skipped
    /// </summary>
    public static bool ShouldSkipSqliteVec()
    {
        if (_isSqliteVecAvailable.HasValue)
            return !_isSqliteVecAvailable.Value;

        // Check for environment variables that indicate SQLite-vec should be explicitly enabled
        var enableSqliteVec = Environment.GetEnvironmentVariable("ENABLE_SQLITEVEC_TESTS");
        if (!string.IsNullOrEmpty(enableSqliteVec) &&
            (enableSqliteVec.Equals("true", StringComparison.OrdinalIgnoreCase) || enableSqliteVec == "1"))
        {
            _isSqliteVecAvailable = true;
            return false;
        }

        // Skip sqlite-vec tests by default unless explicitly enabled
        // This prevents local development issues with native extensions
        _isSqliteVecAvailable = false;
        return true;
    }

    /// <summary>
    /// Check if performance tests should be skipped
    /// </summary>
    public static bool ShouldSkipPerformanceTests()
    {
        // Check for environment variables that indicate performance tests should be explicitly enabled
        var enablePerformanceTests = Environment.GetEnvironmentVariable("ENABLE_PERFORMANCE_TESTS");
        if (!string.IsNullOrEmpty(enablePerformanceTests) &&
            (enablePerformanceTests.Equals("true", StringComparison.OrdinalIgnoreCase) || enablePerformanceTests == "1"))
        {
            return false;
        }

        // Skip performance tests by default unless explicitly enabled
        // This prevents long test execution times and resource issues
        return true;
    }

    /// <summary>
    /// Skip message for SQLite-vec dependent tests
    /// </summary>
    public const string SqliteVecNotAvailableSkipMessage = "SQLite-vec tests are disabled by default. Set ENABLE_SQLITEVEC_TESTS=true to run these tests.";

    /// <summary>
    /// Skip message for CI environment tests
    /// </summary>
    public const string CIEnvironmentSkipMessage = "This test is skipped in CI environment due to native dependency requirements.";

    /// <summary>
    /// Skip message for performance tests
    /// </summary>
    public const string PerformanceTestSkipMessage = "Performance tests are disabled by default. Set ENABLE_PERFORMANCE_TESTS=true to run these tests.";

    /// <summary>
    /// Helper method to skip test if SQLite-vec is not available
    /// </summary>
    public static void SkipIfSqliteVecNotAvailable()
    {
        var shouldSkip = ShouldSkipSqliteVec();
        if (shouldSkip)
        {
            System.Console.WriteLine($"Skipping SQLite-vec test - Enable Flag: {Environment.GetEnvironmentVariable("ENABLE_SQLITEVEC_TESTS")}");
        }
        Skip.If(shouldSkip, SqliteVecNotAvailableSkipMessage);
    }

    /// <summary>
    /// Helper method to skip test if running in CI environment
    /// </summary>
    public static void SkipIfCIEnvironment(string? reason = null)
    {
        Skip.If(IsCIEnvironment(), reason ?? CIEnvironmentSkipMessage);
    }

    /// <summary>
    /// Helper method to skip performance tests
    /// </summary>
    public static void SkipIfPerformanceTestsDisabled()
    {
        var shouldSkip = ShouldSkipPerformanceTests();
        if (shouldSkip)
        {
            System.Console.WriteLine($"Skipping performance test - Enable Flag: {Environment.GetEnvironmentVariable("ENABLE_PERFORMANCE_TESTS")}");
        }
        Skip.If(shouldSkip, PerformanceTestSkipMessage);
    }
}