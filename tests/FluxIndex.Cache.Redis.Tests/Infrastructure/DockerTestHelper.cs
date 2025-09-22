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
            await process.WaitForExitAsync();

            _isDockerAvailable = process.ExitCode == 0;
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
}