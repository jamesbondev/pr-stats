using System.Net;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Polly;
using Polly.Retry;
using PrStats.Configuration;
using PrStats.Models;

namespace PrStats.Services;

public sealed class AzureDevOpsClient
{
    private readonly AppSettings _settings;
    private readonly GitHttpClient _gitClient;
    private readonly BotFilter _botFilter;
    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly SemaphoreSlim _semaphore = new(5, 5);

    public AzureDevOpsClient(AppSettings settings)
    {
        _settings = settings;
        _botFilter = new BotFilter(settings.BotNames, settings.BotIds);

        var credentials = new VssBasicCredential(string.Empty, settings.Pat);
        var connection = new VssConnection(new Uri(settings.Organization), credentials);
        _gitClient = connection.GetClient<GitHttpClient>();

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

    public async Task<List<PullRequestData>> FetchPullRequestsAsync()
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_settings.Days);
        var allPrs = new List<GitPullRequest>();

        // Always use project-level fetch for all modes
        await FetchByStatusProjectAsync(PullRequestStatus.Completed, cutoffDate, allPrs);
        await FetchByStatusProjectAsync(PullRequestStatus.Abandoned, cutoffDate, allPrs);
        await FetchActiveProjectAsync(allPrs);

        // Client-side repo filtering
        if (!_settings.AllRepositories)
        {
            var requestedRepos = new HashSet<string>(_settings.Repositories, StringComparer.OrdinalIgnoreCase);
            var foundRepos = allPrs
                .Where(pr => pr.Repository?.Name != null)
                .Select(pr => pr.Repository.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Warn for requested repos with 0 PRs
            foreach (var repo in _settings.Repositories)
            {
                if (!foundRepos.Contains(repo))
                    Console.WriteLine($"\rWarning: No PRs found for repository '{repo}' — verify the name exists in the project.");
            }

            allPrs = allPrs
                .Where(pr => pr.Repository?.Name != null && requestedRepos.Contains(pr.Repository.Name))
                .ToList();
        }

        // Scaling safeguard: print summary
        var repoCount = allPrs
            .Where(pr => pr.Repository?.Name != null)
            .Select(pr => pr.Repository.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var estimatedCalls = allPrs.Count * 3; // threads + iterations + iteration changes
        Console.WriteLine($"\rFound {allPrs.Count:N0} PRs across {repoCount} {(repoCount == 1 ? "repository" : "repositories")}. Enrichment requires ~{estimatedCalls:N0} API calls.");

        // Apply --max-prs cap
        if (_settings.MaxPrs.HasValue && allPrs.Count > _settings.MaxPrs.Value)
        {
            Console.WriteLine($"Capping to {_settings.MaxPrs.Value} PRs (use --max-prs to adjust).");
            allPrs = allPrs.Take(_settings.MaxPrs.Value).ToList();
        }

        // Cache integration
        var cachedPrs = new Dictionary<int, PullRequestData>();
        if (!_settings.NoCache)
            cachedPrs = await PrCache.LoadAsync(_settings.Organization, _settings.Project);

        // Partition: use cache for completed/abandoned PRs that are already cached
        var fromCache = new List<PullRequestData>();
        var toEnrich = new List<GitPullRequest>();

        foreach (var pr in allPrs)
        {
            if (!_settings.NoCache &&
                cachedPrs.TryGetValue(pr.PullRequestId, out var cached) &&
                pr.Status is PullRequestStatus.Completed or PullRequestStatus.Abandoned)
            {
                fromCache.Add(cached);
            }
            else
            {
                toEnrich.Add(pr);
            }
        }

        // Enrich only PRs not served from cache
        var enriched = await EnrichPullRequestsAsync(toEnrich);

        Console.WriteLine($"Cache: {fromCache.Count} hit, {toEnrich.Count} enriched");

        // Build this run's result
        var thisRunResults = new List<PullRequestData>(fromCache.Count + enriched.Count);
        thisRunResults.AddRange(fromCache);
        thisRunResults.AddRange(enriched);

        // Build save set: start with full cached dictionary, overlay this run's results
        foreach (var pr in thisRunResults)
            cachedPrs[pr.PullRequestId] = pr;

        await PrCache.SaveAsync(_settings.Organization, _settings.Project, cachedPrs);

        return thisRunResults;
    }

    private async Task FetchByStatusProjectAsync(
        PullRequestStatus status, DateTime cutoffDate, List<GitPullRequest> results)
    {
        int skip = 0;
        const int top = 100;

        while (true)
        {
            var searchCriteria = new GitPullRequestSearchCriteria
            {
                Status = status,
                QueryTimeRangeType = PullRequestTimeRangeType.Closed,
                MinTime = cutoffDate,
            };

            var batch = await ExecuteWithRetryAsync(() =>
                _gitClient.GetPullRequestsByProjectAsync(
                    _settings.Project,
                    searchCriteria,
                    skip: skip,
                    top: top));

            if (batch.Count == 0)
                break;

            results.AddRange(batch);
            skip += batch.Count;

            if (batch.Count < top)
                break;
        }
    }

    private async Task FetchActiveProjectAsync(List<GitPullRequest> results)
    {
        int skip = 0;
        const int top = 100;

        while (true)
        {
            var searchCriteria = new GitPullRequestSearchCriteria
            {
                Status = PullRequestStatus.Active,
            };

            var batch = await ExecuteWithRetryAsync(() =>
                _gitClient.GetPullRequestsByProjectAsync(
                    _settings.Project,
                    searchCriteria,
                    skip: skip,
                    top: top));

            if (batch.Count == 0)
                break;

            results.AddRange(batch);
            skip += batch.Count;

            if (batch.Count < top)
                break;
        }
    }

    private async Task<List<PullRequestData>> EnrichPullRequestsAsync(List<GitPullRequest> prs)
    {
        var results = new List<PullRequestData>(prs.Count);
        int completed = 0;
        int total = prs.Count;

        var tasks = prs.Select(async pr =>
        {
            await _semaphore.WaitAsync();
            try
            {
                var data = await EnrichSinglePrAsync(pr);
                lock (results)
                {
                    results.Add(data);
                    completed++;
                    Console.Write($"\rEnriching PRs: {completed}/{total}...");
                }
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        Console.WriteLine($"\rEnriching PRs: {total}/{total} done.          ");
        return results;
    }

    private async Task<PullRequestData> EnrichSinglePrAsync(GitPullRequest pr)
    {
        // Use repository ID from the PR object for enrichment calls
        var repoId = pr.Repository.Id;

        var threadsTask = ExecuteWithRetryAsync(() =>
            _gitClient.GetThreadsAsync(_settings.Project, repoId, pr.PullRequestId));

        var iterationsTask = ExecuteWithRetryAsync(() =>
            _gitClient.GetPullRequestIterationsAsync(
                _settings.Project, repoId, pr.PullRequestId, includeCommits: true));

        await Task.WhenAll(threadsTask, iterationsTask);

        var threads = threadsTask.Result;
        var iterations = iterationsTask.Result;

        // Get file changes from last iteration diffed against merge base
        int filesChanged = 0;
        if (iterations.Count > 0)
        {
            var lastIterationId = iterations[^1].Id ?? 0;
            if (lastIterationId > 0)
            {
                try
                {
                    var changes = await ExecuteWithRetryAsync(() =>
                        _gitClient.GetPullRequestIterationChangesAsync(
                            _settings.Project, repoId, pr.PullRequestId,
                            lastIterationId, compareTo: 0));
                    if (changes.ChangeEntries != null)
                        filesChanged = changes.ChangeEntries.Count();
                }
                catch
                {
                    // Some PRs may not have iteration changes available
                }
            }
        }

        // Count distinct commits across all iterations
        var commitShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var iteration in iterations)
        {
            if (iteration.Commits != null)
            {
                foreach (var commit in iteration.Commits)
                {
                    if (!string.IsNullOrEmpty(commit.CommitId))
                        commitShas.Add(commit.CommitId);
                }
            }
        }

        return MapToPullRequestData(pr, threads, iterations, filesChanged, commitShas.Count);
    }

    private PullRequestData MapToPullRequestData(
        GitPullRequest pr,
        List<GitPullRequestCommentThread> threads,
        List<GitPullRequestIteration> iterations,
        int filesChanged,
        int commitCount)
    {
        var status = pr.Status switch
        {
            PullRequestStatus.Completed => PrStatus.Completed,
            PullRequestStatus.Abandoned => PrStatus.Abandoned,
            _ => PrStatus.Active,
        };

        var reviewers = (pr.Reviewers ?? []).Select(r => new ReviewerInfo
        {
            DisplayName = r.DisplayName ?? "Unknown",
            Id = r.Id?.ToString() ?? "",
            Vote = r.Vote,
            IsContainer = r.IsContainer,
            IsRequired = r.IsRequired,
        }).ToList();

        var threadInfos = threads.Select(t =>
        {
            var firstComment = t.Comments?.FirstOrDefault();
            var authorName = firstComment?.Author?.DisplayName ?? "Unknown";
            var authorId = firstComment?.Author?.Id?.ToString() ?? "";
            var commentType = t.Properties?.GetValue<string>("CodeReviewThreadType", "") ?? "";
            var isVoteUpdate = string.Equals(commentType, "VoteUpdate", StringComparison.OrdinalIgnoreCase);

            int? voteValue = null;
            if (isVoteUpdate)
            {
                voteValue = t.Properties?.GetValue<int>("CodeReviewVoteResult", 0);
            }

            var isBot = _botFilter.IsBot(authorName, false, authorId);

            var statusStr = t.Status switch
            {
                CommentThreadStatus.Fixed => "fixed",
                CommentThreadStatus.Closed => "closed",
                CommentThreadStatus.WontFix => "wontFix",
                CommentThreadStatus.ByDesign => "byDesign",
                CommentThreadStatus.Active => "active",
                CommentThreadStatus.Pending => "pending",
                _ => "unknown",
            };

            var threadCommentType = t.Comments?.FirstOrDefault()?.CommentType switch
            {
                CommentType.Text => "text",
                CommentType.System => "system",
                CommentType.CodeChange => "codeChange",
                _ => "unknown",
            };

            // If it's a system thread type (VoteUpdate, StatusUpdate, etc.), mark as system
            if (!string.IsNullOrEmpty(commentType) && commentType != "Text")
                threadCommentType = "system";

            return new ThreadInfo
            {
                ThreadId = t.Id,
                CommentType = threadCommentType,
                PublishedDate = t.PublishedDate,
                AuthorDisplayName = authorName,
                AuthorId = authorId,
                IsAuthorBot = isBot,
                Status = statusStr,
                CommentCount = t.Comments?.Count ?? 0,
                IsVoteUpdate = isVoteUpdate,
                VoteValue = voteValue,
            };
        }).ToList();

        var iterationInfos = iterations.Select(i => new IterationInfo
        {
            IterationId = i.Id ?? 0,
            CreatedDate = i.CreatedDate ?? DateTime.MinValue,
            Reason = i.Reason.ToString(),
        }).ToList();

        var prAuthorName = pr.CreatedBy?.DisplayName ?? "Unknown";
        var prAuthorId = pr.CreatedBy?.Id?.ToString() ?? "";

        return new PullRequestData
        {
            PullRequestId = pr.PullRequestId,
            Title = pr.Title ?? "",
            RepositoryName = pr.Repository?.Name ?? "Unknown",
            Status = status,
            IsDraft = pr.IsDraft ?? false,
            CreationDate = pr.CreationDate,
            ClosedDate = pr.ClosedDate,
            AuthorDisplayName = prAuthorName,
            AuthorId = prAuthorId,
            IsAuthorBot = _botFilter.IsBot(prAuthorName, false, prAuthorId),
            ClosedByDisplayName = pr.ClosedBy?.DisplayName,
            ClosedById = pr.ClosedBy?.Id?.ToString(),
            Reviewers = reviewers,
            Threads = threadInfos,
            Iterations = iterationInfos,
            FilesChanged = filesChanged,
            CommitCount = commitCount,
        };
    }

    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        return await _resiliencePipeline.ExecuteAsync(
            async _ => await action(),
            CancellationToken.None);
    }
}
