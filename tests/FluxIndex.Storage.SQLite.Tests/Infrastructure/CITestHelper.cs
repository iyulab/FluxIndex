using System;
using System.IO;
using System.Runtime.InteropServices;
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

        // Force skip in CI environments unless explicitly enabled
        if (IsCIEnvironment())
        {
            var enableSqliteVec = Environment.GetEnvironmentVariable("ENABLE_SQLITEVEC_TESTS");
            if (string.IsNullOrEmpty(enableSqliteVec) ||
                (!enableSqliteVec.Equals("true", StringComparison.OrdinalIgnoreCase) && enableSqliteVec != "1"))
            {
                _isSqliteVecAvailable = false;
                return true;
            }
        }

        // Check for explicit environment variable override
        var enableSqliteVecLocal = Environment.GetEnvironmentVariable("ENABLE_SQLITEVEC_TESTS");
        if (!string.IsNullOrEmpty(enableSqliteVecLocal))
        {
            if (enableSqliteVecLocal.Equals("true", StringComparison.OrdinalIgnoreCase) || enableSqliteVecLocal == "1")
            {
                _isSqliteVecAvailable = true;
                return false;
            }
            if (enableSqliteVecLocal.Equals("false", StringComparison.OrdinalIgnoreCase) || enableSqliteVecLocal == "0")
            {
                _isSqliteVecAvailable = false;
                return true;
            }
        }

        // Auto-detect: Enable sqlite-vec tests if native extension file exists
        // NuGet package sqlite-vec provides native binaries in runtimes folder
        _isSqliteVecAvailable = IsSqliteVecExtensionAvailable();
        return !_isSqliteVecAvailable.Value;
    }

    /// <summary>
    /// Check if sqlite-vec native extension is available
    /// </summary>
    private static bool IsSqliteVecExtensionAvailable()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;

            // Try platform-specific paths from output directory
            string[] outputPaths;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                outputPaths = new[]
                {
                    Path.Combine(baseDir, "runtimes", "win-x64", "native", "vec0.dll"),
                    Path.Combine(baseDir, "vec0.dll")
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                outputPaths = new[]
                {
                    Path.Combine(baseDir, "runtimes", "linux-x64", "native", "vec0.so"),
                    Path.Combine(baseDir, "vec0.so")
                };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                outputPaths = new[]
                {
                    Path.Combine(baseDir, "runtimes", "osx-x64", "native", "vec0.dylib"),
                    Path.Combine(baseDir, "runtimes", "osx-arm64", "native", "vec0.dylib"),
                    Path.Combine(baseDir, "vec0.dylib")
                };
            }
            else
            {
                outputPaths = Array.Empty<string>();
            }

            foreach (var path in outputPaths)
            {
                if (File.Exists(path))
                {
                    Console.WriteLine($"[CITestHelper] Found sqlite-vec extension at: {path}");
                    return true;
                }
            }

            // Try NuGet global packages cache (native files aren't auto-copied during build)
            var nugetCachePath = GetNuGetCacheExtensionPath();
            if (!string.IsNullOrEmpty(nugetCachePath) && File.Exists(nugetCachePath))
            {
                Console.WriteLine($"[CITestHelper] Found sqlite-vec extension in NuGet cache: {nugetCachePath}");
                return true;
            }

            Console.WriteLine($"[CITestHelper] sqlite-vec extension not found. Searched in: {baseDir}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CITestHelper] Error checking sqlite-vec availability: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get sqlite-vec extension path from NuGet global packages cache
    /// </summary>
    private static string? GetNuGetCacheExtensionPath()
    {
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var nugetGlobalPackages = Path.Combine(userProfile, ".nuget", "packages", "sqlite-vec");

            if (!Directory.Exists(nugetGlobalPackages))
            {
                return null;
            }

            // Find the latest version folder
            var versionDirs = Directory.GetDirectories(nugetGlobalPackages)
                .OrderByDescending(d => d)
                .ToList();

            foreach (var versionDir in versionDirs)
            {
                string runtimeFolder;
                string fileName;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    runtimeFolder = "win-x64";
                    fileName = "vec0.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    runtimeFolder = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? "linux-arm64" : "linux-x64";
                    fileName = "vec0.so";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    runtimeFolder = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                        ? "osx-arm64" : "osx-x64";
                    fileName = "vec0.dylib";
                }
                else
                {
                    return null;
                }

                var nativePath = Path.Combine(versionDir, "runtimes", runtimeFolder, "native", fileName);
                if (File.Exists(nativePath))
                {
                    return nativePath;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
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
    public const string SqliteVecNotAvailableSkipMessage = "SQLite-vec native extension not found. Ensure sqlite-vec NuGet package is restored, or set ENABLE_SQLITEVEC_TESTS=true to force enable.";

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