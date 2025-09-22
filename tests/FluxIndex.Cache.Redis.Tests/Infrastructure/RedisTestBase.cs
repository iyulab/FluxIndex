using FluxIndex.Cache.Redis.Tests.Infrastructure;
using System;
using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;
using Xunit.Abstractions;

namespace FluxIndex.Cache.Redis.Tests.Infrastructure;

/// <summary>
/// Base class for Redis tests that require Docker
/// </summary>
public abstract class RedisTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected RedisContainer? RedisContainer;
    protected bool DockerAvailable { get; private set; }
    protected string ConnectionString { get; private set; } = string.Empty;

    protected RedisTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    public async Task InitializeAsync()
    {
        DockerAvailable = await DockerTestHelper.IsDockerAvailableAsync();

        if (!DockerAvailable)
        {
            Output.WriteLine("Docker is not available - Redis tests will be skipped");
            return;
        }

        try
        {
            // Create container only when Docker is available
            RedisContainer = new RedisBuilder()
                .WithImage("redis:7-alpine")
                .WithPortBinding(6379, true)
                .Build();

            await RedisContainer.StartAsync();
            ConnectionString = RedisContainer.GetConnectionString();
            Output.WriteLine($"Redis container started: {ConnectionString}");

            await OnDockerInitializedAsync();
        }
        catch (Exception ex)
        {
            Output.WriteLine($"Failed to start Redis container: {ex.Message}");
            DockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (DockerAvailable && RedisContainer != null)
        {
            try
            {
                await OnDockerDisposingAsync();
                await RedisContainer.DisposeAsync();
            }
            catch (Exception ex)
            {
                Output.WriteLine($"Error disposing Redis container: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Called after Docker container is initialized
    /// </summary>
    protected virtual Task OnDockerInitializedAsync() => Task.CompletedTask;

    /// <summary>
    /// Called before Docker container is disposed
    /// </summary>
    protected virtual Task OnDockerDisposingAsync() => Task.CompletedTask;

    /// <summary>
    /// Helper method to skip test if Docker is not available
    /// </summary>
    protected void SkipIfDockerNotAvailable()
    {
        Skip.If(!DockerAvailable, DockerTestHelper.DockerNotAvailableSkipMessage);
    }
}