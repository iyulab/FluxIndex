using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace FluxIndex.Cache.Redis.Tests.Infrastructure;

/// <summary>
/// Helper class for Docker-dependent tests
/// </summary>
public static class DockerTestHelper
{
    private static bool? _isDockerAvailable;

    /// <summary>
    /// Check if Docker is available and running
    /// </summary>
    public static async Task<bool> IsDockerAvailableAsync()
    {
        if (_isDockerAvailable.HasValue)
            return _isDockerAvailable.Value;

        // Check for environment variables that indicate Docker should be skipped
        var skipDocker = Environment.GetEnvironmentVariable("SKIP_DOCKER_TESTS");
        if (!string.IsNullOrEmpty(skipDocker) && (skipDocker.Equals("true", StringComparison.OrdinalIgnoreCase) || skipDocker == "1"))
        {
            _isDockerAvailable = false;
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Add a timeout to prevent hanging in CI environments
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await process.WaitForExitAsync(cts.Token);
                _isDockerAvailable = process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                _isDockerAvailable = false;
            }

            return _isDockerAvailable.Value;
        }
        catch (Exception)
        {
            _isDockerAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Skip message for Docker-dependent tests
    /// </summary>
    public const string DockerNotAvailableSkipMessage = "Docker is not available. This test requires Docker to run Redis container.";

    /// <summary>
    /// Environment variable to force skip Docker tests (set to 'true' or '1' to skip)
    /// </summary>
    public const string SkipDockerEnvironmentVariable = "SKIP_DOCKER_TESTS";
}