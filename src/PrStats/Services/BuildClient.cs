using System.Net;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Polly;
using Polly.Retry;
using PrStats.Configuration;
using PrStats.Models;

namespace PrStats.Services;

public sealed class BuildClient
{
    private readonly AppSettings _settings;
    private readonly BuildHttpClient _buildClient;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly SemaphoreSlim _semaphore = new(5, 5);

    public BuildClient(AppSettings settings)
    {
        _settings = settings;

        var credentials = new VssBasicCredential(string.Empty, settings.Pat);
        var connection = new VssConnection(new Uri(settings.Organization), credentials);
        _buildClient = connection.GetClient<BuildHttpClient>();

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                ShouldHandle = new PredicateBuilder()
                    .Handle<VssServiceResponseException>(ex =>
                        ex.HttpStatusCode == HttpStatusCode.TooManyRequests ||
                        (int)ex.HttpStatusCode >= 500)
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                OnRetry = args =>
                {
                    Console.Write($"\r  Retrying after {args.RetryDelay.TotalSeconds:F1}s...");
                    return default;
                },
            })
            .Build();
    }

    public async Task<Dictionary<int, List<BuildInfo>>> FetchBuildsForPrsAsync(
        List<PullRequestData> prs)
    {
        var results = new Dictionary<int, List<BuildInfo>>();
        int completed = 0;
        int total = prs.Count;
        var lockObj = new object();

        var tasks = prs.Select(async pr =>
        {
            await _semaphore.WaitAsync();
            try
            {
                var builds = await FetchBuildsForSinglePrAsync(pr.PullRequestId);
                if (builds.Count > 0)
                {
                    lock (lockObj)
                    {
                        results[pr.PullRequestId] = builds;
                    }
                }

                lock (lockObj)
                {
                    completed++;
                    Console.Write($"\rFetching builds: {completed}/{total}...");
                }
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"\rFetching builds: {total}/{total} done.          ");
        return results;
    }

    private async Task<List<BuildInfo>> FetchBuildsForSinglePrAsync(int prId)
    {
        var branchName = $"refs/pull/{prId}/merge";

        var builds = await ExecuteWithRetryAsync(() =>
            _buildClient.GetBuildsAsync(
                project: _settings.Project,
                branchName: branchName));

        return builds.Select(MapBuild).ToList();
    }

    internal static BuildInfo MapBuild(Build build)
    {
        return new BuildInfo
        {
            BuildId = build.Id,
            DefinitionName = build.Definition?.Name ?? "Unknown",
            DefinitionId = build.Definition?.Id ?? 0,
            Status = build.Status?.ToString() ?? "Unknown",
            Result = build.Result?.ToString(),
            QueueTime = build.QueueTime ?? DateTime.MinValue,
            StartTime = build.StartTime,
            FinishTime = build.FinishTime,
            SourceVersion = build.SourceVersion,
        };
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        return await _resiliencePipeline.ExecuteAsync(
            async _ => await action(),
            CancellationToken.None);
    }
}
