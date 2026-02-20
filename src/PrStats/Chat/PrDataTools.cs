using System.ComponentModel;
using System.Text;
using PrStats.Models;

namespace PrStats.Chat;

public class PrDataTools
{
    private readonly PrStatsReport _report;

    public PrDataTools(PrStatsReport report) => _report = report;

    [Description("Get an overview of the team metrics: PR counts, average/median cycle time, quality rates, and author/reviewer counts. Call this first to understand the overall context.")]
    public Task<string> GetTeamSummary()
    {
        var tm = _report.TeamMetrics;
        var sb = new StringBuilder();

        sb.AppendLine("## Team Summary");
        sb.AppendLine($"- Organization: {_report.Organization}");
        sb.AppendLine($"- Project: {_report.Project}");
        sb.AppendLine($"- Repository: {_report.RepositoryDisplayName}");
        sb.AppendLine($"- Period: last {_report.Days} days");
        sb.AppendLine($"- Report generated: {_report.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("### PR Counts");
        sb.AppendLine($"- Total: {tm.TotalPrCount}");
        sb.AppendLine($"- Completed: {tm.CompletedPrCount}");
        sb.AppendLine($"- Abandoned: {tm.AbandonedPrCount}");
        sb.AppendLine($"- Active: {tm.ActivePrCount}");
        sb.AppendLine();
        sb.AppendLine("### Cycle Time");
        sb.AppendLine($"- Average: {FormatDuration(tm.AvgCycleTime)}");
        sb.AppendLine($"- Median: {FormatDuration(tm.MedianCycleTime)}");
        sb.AppendLine($"- Avg time to first comment: {FormatDuration(tm.AvgTimeToFirstComment)}");
        sb.AppendLine($"- Avg time to first approval: {FormatDuration(tm.AvgTimeToFirstApproval)}");
        sb.AppendLine();
        sb.AppendLine("### Quality Rates");
        sb.AppendLine($"- Abandoned rate: {tm.AbandonedRate:P1}");
        sb.AppendLine($"- First-time approval rate: {tm.FirstTimeApprovalRate:P1}");
        sb.AppendLine($"- Thread resolution rate: {tm.ThreadResolutionRate:P1}");
        sb.AppendLine();
        sb.AppendLine("### Size");
        sb.AppendLine($"- Avg files changed: {tm.AvgFilesChanged:F1}");
        sb.AppendLine($"- Avg commits per PR: {tm.AvgCommitsPerPr:F1}");
        sb.AppendLine();
        sb.AppendLine($"### Authors ({tm.PrsPerAuthor.Count})");
        foreach (var (author, count) in tm.PrsPerAuthor.OrderByDescending(x => x.Value).Take(10))
            sb.AppendLine($"- {author}: {count} PRs");

        sb.AppendLine();
        sb.AppendLine($"### Reviewers ({tm.ReviewsPerPerson.Count})");
        foreach (var (reviewer, count) in tm.ReviewsPerPerson.OrderByDescending(x => x.Value).Take(10))
            sb.AppendLine($"- {reviewer}: {count} reviews");

        return Task.FromResult(sb.ToString());
    }

    [Description("Search and filter pull requests by author, repository, status, cycle time range, or title text. Returns a summary table of matching PRs.")]
    public Task<string> SearchPullRequests(
        [Description("Filter by author display name (partial, case-insensitive match)")] string? author = null,
        [Description("Filter by repository name (partial, case-insensitive match)")] string? repo = null,
        [Description("Filter by PR status: active, completed, or abandoned")] string? status = null,
        [Description("Minimum cycle time in hours")] double? minCycleTimeHours = null,
        [Description("Maximum cycle time in hours")] double? maxCycleTimeHours = null,
        [Description("Filter by title text (case-insensitive contains)")] string? titleContains = null,
        [Description("Maximum number of results to return (default 20, max 50)")] int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);

        var query = _report.Metrics.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(author))
            query = query.Where(m => m.AuthorDisplayName.Contains(author, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(repo))
            query = query.Where(m => m.RepositoryName.Contains(repo, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PrStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(m => m.Status == parsedStatus);

        if (minCycleTimeHours.HasValue)
            query = query.Where(m => m.TotalCycleTime.HasValue && m.TotalCycleTime.Value.TotalHours >= minCycleTimeHours.Value);

        if (maxCycleTimeHours.HasValue)
            query = query.Where(m => m.TotalCycleTime.HasValue && m.TotalCycleTime.Value.TotalHours <= maxCycleTimeHours.Value);

        if (!string.IsNullOrWhiteSpace(titleContains))
            query = query.Where(m => m.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

        var results = query.Take(maxResults).ToList();

        if (results.Count == 0)
            return Task.FromResult("No pull requests match the specified filters.");

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} matching PRs:");
        sb.AppendLine();
        sb.AppendLine("| PR ID | Title | Author | Repo | Status | Cycle Time | Files | Comments |");
        sb.AppendLine("|-------|-------|--------|------|--------|------------|-------|----------|");

        foreach (var m in results)
        {
            var title = m.Title.Length > 50 ? m.Title[..47] + "..." : m.Title;
            sb.AppendLine($"| {m.PullRequestId} | {title} | {m.AuthorDisplayName} | {m.RepositoryName} | {m.Status} | {FormatDuration(m.TotalCycleTime)} | {m.FilesChanged} | {m.HumanCommentCount} |");
        }

        return Task.FromResult(sb.ToString());
    }

    [Description("Get full detail for a specific pull request including all metrics, reviewers, threads, and iterations.")]
    public Task<string> GetPullRequestDetail(
        [Description("The pull request ID to look up")] int pullRequestId)
    {
        var metrics = _report.Metrics.FirstOrDefault(m => m.PullRequestId == pullRequestId);
        var pr = _report.PullRequests.FirstOrDefault(p => p.PullRequestId == pullRequestId);

        if (metrics is null || pr is null)
            return Task.FromResult($"Pull request {pullRequestId} not found in the dataset.");

        var sb = new StringBuilder();
        sb.AppendLine($"## PR #{pr.PullRequestId}: {pr.Title}");
        sb.AppendLine();
        sb.AppendLine("### Basic Info");
        sb.AppendLine($"- Repository: {pr.RepositoryName}");
        sb.AppendLine($"- Author: {pr.AuthorDisplayName}");
        sb.AppendLine($"- Status: {pr.Status}");
        sb.AppendLine($"- Draft: {pr.IsDraft}");
        sb.AppendLine($"- Created: {pr.CreationDate:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- Closed: {(pr.ClosedDate.HasValue ? pr.ClosedDate.Value.ToString("yyyy-MM-dd HH:mm") + " UTC" : "N/A")}");
        if (pr.ClosedByDisplayName is not null)
            sb.AppendLine($"- Closed by: {pr.ClosedByDisplayName}");
        sb.AppendLine();

        sb.AppendLine("### Metrics");
        sb.AppendLine($"- Total cycle time: {FormatDuration(metrics.TotalCycleTime)}");
        sb.AppendLine($"- Time to first human comment: {FormatDuration(metrics.TimeToFirstHumanComment)}");
        sb.AppendLine($"- Time to first approval: {FormatDuration(metrics.TimeToFirstApproval)}");
        sb.AppendLine($"- Time from approval to merge: {FormatDuration(metrics.TimeFromApprovalToMerge)}");
        sb.AppendLine($"- Files changed: {metrics.FilesChanged}");
        sb.AppendLine($"- Commits: {metrics.CommitCount}");
        sb.AppendLine($"- Iterations (pushes): {metrics.IterationCount}");
        sb.AppendLine($"- Human comments: {metrics.HumanCommentCount}");
        sb.AppendLine($"- First-time approval: {metrics.IsFirstTimeApproval}");
        sb.AppendLine($"- Resolvable threads: {metrics.ResolvableThreadCount}");
        sb.AppendLine($"- Resolved threads: {metrics.ResolvedThreadCount}");
        sb.AppendLine($"- Active reviewers: {metrics.ActiveReviewerCount}");
        if (metrics.ActiveAge.HasValue)
            sb.AppendLine($"- Active age: {FormatDuration(metrics.ActiveAge)}");
        sb.AppendLine();

        sb.AppendLine($"### Reviewers ({pr.Reviewers.Count})");
        foreach (var r in pr.Reviewers)
        {
            var voteLabel = r.Vote switch
            {
                10 => "Approved",
                5 => "Approved with suggestions",
                0 => "No vote",
                -5 => "Waiting for author",
                -10 => "Rejected",
                _ => $"Vote: {r.Vote}",
            };
            sb.AppendLine($"- {r.DisplayName}: {voteLabel}{(r.IsRequired ? " (required)" : "")}{(r.IsContainer ? " (group)" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine($"### Threads ({pr.Threads.Count})");
        var humanThreads = pr.Threads.Where(t => t.CommentType == "text" && !t.IsAuthorBot).Take(20).ToList();
        foreach (var t in humanThreads)
            sb.AppendLine($"- [{t.Status}] by {t.AuthorDisplayName} at {t.PublishedDate:yyyy-MM-dd HH:mm} ({t.CommentCount} comments)");

        sb.AppendLine();
        sb.AppendLine($"### Iterations ({pr.Iterations.Count})");
        foreach (var i in pr.Iterations)
            sb.AppendLine($"- #{i.IterationId}: {i.CreatedDate:yyyy-MM-dd HH:mm} ({i.Reason})");

        return Task.FromResult(sb.ToString());
    }

    [Description("Get statistics for a specific author: PR count, average cycle time, first-time approval rate, and their PRs.")]
    public Task<string> GetAuthorStats(
        [Description("Author display name (partial, case-insensitive match)")] string author,
        [Description("Maximum number of PRs to list (default 20, max 50)")] int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);

        var authorMetrics = _report.Metrics
            .Where(m => m.AuthorDisplayName.Contains(author, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (authorMetrics.Count == 0)
            return Task.FromResult($"No PRs found for author matching \"{author}\".");

        var actualName = authorMetrics[0].AuthorDisplayName;
        var completed = authorMetrics.Where(m => m.Status == PrStatus.Completed && !m.IsDraft).ToList();
        var cycleTimes = completed.Where(m => m.TotalCycleTime.HasValue).Select(m => m.TotalCycleTime!.Value).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"## Author: {actualName}");
        sb.AppendLine();
        sb.AppendLine($"- Total PRs: {authorMetrics.Count}");
        sb.AppendLine($"- Completed: {authorMetrics.Count(m => m.Status == PrStatus.Completed)}");
        sb.AppendLine($"- Active: {authorMetrics.Count(m => m.Status == PrStatus.Active)}");
        sb.AppendLine($"- Abandoned: {authorMetrics.Count(m => m.Status == PrStatus.Abandoned)}");

        if (cycleTimes.Count > 0)
        {
            var avg = TimeSpan.FromTicks((long)cycleTimes.Average(t => t.Ticks));
            var sorted = cycleTimes.OrderBy(t => t).ToList();
            var median = sorted[sorted.Count / 2];
            sb.AppendLine($"- Avg cycle time: {FormatDuration(avg)}");
            sb.AppendLine($"- Median cycle time: {FormatDuration(median)}");
        }

        if (completed.Count > 0)
        {
            var ftaRate = (double)completed.Count(m => m.IsFirstTimeApproval) / completed.Count;
            sb.AppendLine($"- First-time approval rate: {ftaRate:P1}");
        }

        sb.AppendLine($"- Avg files changed: {authorMetrics.Average(m => m.FilesChanged):F1}");
        sb.AppendLine();

        sb.AppendLine($"### PRs (showing {Math.Min(maxResults, authorMetrics.Count)} of {authorMetrics.Count})");
        sb.AppendLine("| PR ID | Title | Status | Cycle Time | Files |");
        sb.AppendLine("|-------|-------|--------|------------|-------|");
        foreach (var m in authorMetrics.Take(maxResults))
        {
            var title = m.Title.Length > 50 ? m.Title[..47] + "..." : m.Title;
            sb.AppendLine($"| {m.PullRequestId} | {title} | {m.Status} | {FormatDuration(m.TotalCycleTime)} | {m.FilesChanged} |");
        }

        return Task.FromResult(sb.ToString());
    }

    [Description("Get statistics for a specific reviewer: review count, who they review most, and their review activity.")]
    public Task<string> GetReviewerStats(
        [Description("Reviewer display name (partial, case-insensitive match)")] string reviewer,
        [Description("Maximum number of entries to list (default 20, max 50)")] int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);

        // Find reviewer in ReviewsPerPerson
        var matchingReviewers = _report.TeamMetrics.ReviewsPerPerson
            .Where(kvp => kvp.Key.Contains(reviewer, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingReviewers.Count == 0)
            return Task.FromResult($"No reviewer found matching \"{reviewer}\".");

        var sb = new StringBuilder();

        foreach (var (name, reviewCount) in matchingReviewers)
        {
            sb.AppendLine($"## Reviewer: {name}");
            sb.AppendLine($"- Total reviews: {reviewCount}");
            sb.AppendLine();

            // Find pairing info
            var pairings = _report.TeamMetrics.PairingMatrix
                .Where(p => p.Reviewer.Contains(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Count)
                .Take(maxResults)
                .ToList();

            if (pairings.Count > 0)
            {
                sb.AppendLine("### Top Authors Reviewed");
                sb.AppendLine("| Author | Review Count |");
                sb.AppendLine("|--------|-------------|");
                foreach (var p in pairings)
                    sb.AppendLine($"| {p.Author} | {p.Count} |");
            }

            // Find PRs they reviewed
            var reviewedPrs = _report.PullRequests
                .Where(pr => pr.Reviewers.Any(r =>
                    r.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase) && r.Vote != 0))
                .Take(maxResults)
                .ToList();

            if (reviewedPrs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"### Recent PRs Reviewed (showing {reviewedPrs.Count})");
                sb.AppendLine("| PR ID | Title | Author | Status |");
                sb.AppendLine("|-------|-------|--------|--------|");
                foreach (var pr in reviewedPrs)
                {
                    var title = pr.Title.Length > 50 ? pr.Title[..47] + "..." : pr.Title;
                    sb.AppendLine($"| {pr.PullRequestId} | {title} | {pr.AuthorDisplayName} | {pr.Status} |");
                }
            }

            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    [Description("Get a per-repository breakdown showing PR counts, cycle time, and quality rates for each repository.")]
    public Task<string> GetRepositoryBreakdown()
    {
        var breakdown = _report.TeamMetrics.PerRepositoryBreakdown;

        if (breakdown.Count == 0)
            return Task.FromResult("No per-repository breakdown available (single repository or no data).");

        var sb = new StringBuilder();
        sb.AppendLine("## Repository Breakdown");
        sb.AppendLine();
        sb.AppendLine("| Repository | Total | Completed | Abandoned | Active | Abandoned Rate | Avg Cycle Time | Median Cycle Time | FTA Rate | Avg Files |");
        sb.AppendLine("|------------|-------|-----------|-----------|--------|---------------|----------------|-------------------|----------|-----------|");

        foreach (var (repo, b) in breakdown.OrderByDescending(x => x.Value.TotalPrCount))
        {
            sb.AppendLine($"| {repo} | {b.TotalPrCount} | {b.CompletedPrCount} | {b.AbandonedPrCount} | {b.ActivePrCount} | {b.AbandonedRate:P1} | {FormatDuration(b.AvgCycleTime)} | {FormatDuration(b.MedianCycleTime)} | {b.FirstTimeApprovalRate:P1} | {b.AvgFilesChanged:F1} |");
        }

        return Task.FromResult(sb.ToString());
    }

    [Description("Get the slowest pull requests ranked by total cycle time (longest first). Useful for finding outliers.")]
    public Task<string> GetSlowestPullRequests(
        [Description("Number of results to return (default 10, max 50)")] int count = 10,
        [Description("Filter by author display name (partial, case-insensitive match)")] string? author = null,
        [Description("Filter by repository name (partial, case-insensitive match)")] string? repo = null)
    {
        count = Math.Clamp(count, 1, 50);

        var query = _report.Metrics
            .Where(m => m.TotalCycleTime.HasValue)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(author))
            query = query.Where(m => m.AuthorDisplayName.Contains(author, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(repo))
            query = query.Where(m => m.RepositoryName.Contains(repo, StringComparison.OrdinalIgnoreCase));

        var results = query
            .OrderByDescending(m => m.TotalCycleTime!.Value)
            .Take(count)
            .ToList();

        if (results.Count == 0)
            return Task.FromResult("No completed PRs with cycle time data found matching the filters.");

        var sb = new StringBuilder();
        sb.AppendLine($"## Slowest {results.Count} PRs (by cycle time)");
        sb.AppendLine();
        sb.AppendLine("| Rank | PR ID | Title | Author | Repo | Cycle Time | Files | Comments |");
        sb.AppendLine("|------|-------|-------|--------|------|------------|-------|----------|");

        for (var i = 0; i < results.Count; i++)
        {
            var m = results[i];
            var title = m.Title.Length > 40 ? m.Title[..37] + "..." : m.Title;
            sb.AppendLine($"| {i + 1} | {m.PullRequestId} | {title} | {m.AuthorDisplayName} | {m.RepositoryName} | {FormatDuration(m.TotalCycleTime)} | {m.FilesChanged} | {m.HumanCommentCount} |");
        }

        return Task.FromResult(sb.ToString());
    }

    [Description("Get the fastest pull requests ranked by total cycle time (shortest first). Useful for understanding best-case patterns.")]
    public Task<string> GetFastestPullRequests(
        [Description("Number of results to return (default 10, max 50)")] int count = 10,
        [Description("Filter by author display name (partial, case-insensitive match)")] string? author = null,
        [Description("Filter by repository name (partial, case-insensitive match)")] string? repo = null)
    {
        count = Math.Clamp(count, 1, 50);

        var query = _report.Metrics
            .Where(m => m.TotalCycleTime.HasValue)
            .AsEnumerable();

        if (!string.IsNullOrWhiteSpace(author))
            query = query.Where(m => m.AuthorDisplayName.Contains(author, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(repo))
            query = query.Where(m => m.RepositoryName.Contains(repo, StringComparison.OrdinalIgnoreCase));

        var results = query
            .OrderBy(m => m.TotalCycleTime!.Value)
            .Take(count)
            .ToList();

        if (results.Count == 0)
            return Task.FromResult("No completed PRs with cycle time data found matching the filters.");

        var sb = new StringBuilder();
        sb.AppendLine($"## Fastest {results.Count} PRs (by cycle time)");
        sb.AppendLine();
        sb.AppendLine("| Rank | PR ID | Title | Author | Repo | Cycle Time | Files | Comments |");
        sb.AppendLine("|------|-------|-------|--------|------|------------|-------|----------|");

        for (var i = 0; i < results.Count; i++)
        {
            var m = results[i];
            var title = m.Title.Length > 40 ? m.Title[..37] + "..." : m.Title;
            sb.AppendLine($"| {i + 1} | {m.PullRequestId} | {title} | {m.AuthorDisplayName} | {m.RepositoryName} | {FormatDuration(m.TotalCycleTime)} | {m.FilesChanged} | {m.HumanCommentCount} |");
        }

        return Task.FromResult(sb.ToString());
    }

    internal static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
            return "N/A";

        var ts = duration.Value;
        if (ts.TotalDays >= 1)
            return $"{ts.TotalHours:F0} hours ({ts.TotalDays:F1} days)";
        if (ts.TotalHours >= 1)
            return $"{ts.TotalHours:F1} hours";
        return $"{ts.TotalMinutes:F0} minutes";
    }
}
