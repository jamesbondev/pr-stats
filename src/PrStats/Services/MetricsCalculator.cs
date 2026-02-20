using PrStats.Models;

namespace PrStats.Services;

public sealed class MetricsCalculator
{
    public PullRequestMetrics CalculatePerPR(PullRequestData pr)
    {
        var isCompleted = pr.Status == PrStatus.Completed;
        var includeCycleTime = isCompleted && !pr.IsDraft;

        // Cycle time metrics
        TimeSpan? totalCycleTime = null;
        TimeSpan? timeToFirstHumanComment = null;
        TimeSpan? timeToFirstApproval = null;
        TimeSpan? timeFromApprovalToMerge = null;

        if (includeCycleTime && pr.ClosedDate.HasValue)
        {
            totalCycleTime = pr.ClosedDate.Value - pr.CreationDate;

            // Time to first human comment (non-bot, non-author, text comment)
            var firstHumanComment = pr.Threads
                .Where(t => t.CommentType == "text"
                    && !t.IsAuthorBot
                    && t.AuthorId != pr.AuthorId
                    && !t.IsVoteUpdate)
                .OrderBy(t => t.PublishedDate)
                .FirstOrDefault();

            if (firstHumanComment != null)
                timeToFirstHumanComment = firstHumanComment.PublishedDate - pr.CreationDate;

            // Time to first approval (from VoteUpdate threads with vote >= 5)
            var firstApproval = pr.Threads
                .Where(t => t.IsVoteUpdate && t.VoteValue.HasValue && t.VoteValue.Value >= 5)
                .OrderBy(t => t.PublishedDate)
                .FirstOrDefault();

            if (firstApproval != null)
            {
                timeToFirstApproval = firstApproval.PublishedDate - pr.CreationDate;
                timeFromApprovalToMerge = pr.ClosedDate.Value - firstApproval.PublishedDate;
            }
        }

        // Human comment count (non-system, non-bot, non-self)
        int humanCommentCount = pr.Threads
            .Count(t => t.CommentType == "text"
                && !t.IsAuthorBot
                && t.AuthorId != pr.AuthorId);

        // First-time approval: first approve vote before second iteration push
        bool isFirstTimeApproval = false;
        if (isCompleted)
        {
            var firstApprovalThread = pr.Threads
                .Where(t => t.IsVoteUpdate && t.VoteValue.HasValue && t.VoteValue.Value >= 5)
                .OrderBy(t => t.PublishedDate)
                .FirstOrDefault();

            if (firstApprovalThread != null)
            {
                if (pr.Iterations.Count <= 1)
                {
                    // Only 1 iteration with an approval = first-time approval
                    isFirstTimeApproval = true;
                }
                else
                {
                    var secondIteration = pr.Iterations.OrderBy(i => i.CreatedDate).ElementAtOrDefault(1);
                    if (secondIteration != null)
                        isFirstTimeApproval = firstApprovalThread.PublishedDate < secondIteration.CreatedDate;
                }
            }
        }

        // Thread resolution rate (text threads from non-bots)
        var resolvableThreads = pr.Threads
            .Where(t => t.CommentType == "text" && !t.IsAuthorBot)
            .ToList();
        int resolvableCount = resolvableThreads.Count;
        int resolvedCount = resolvableThreads
            .Count(t => t.Status is "fixed" or "closed" or "wontFix" or "byDesign");

        // Active reviewers (non-container reviewers who actually voted)
        var activeReviewers = pr.Reviewers
            .Where(r => !r.IsContainer && r.Vote != 0)
            .Select(r => r.DisplayName)
            .ToList();

        // Active age for active PRs
        TimeSpan? activeAge = null;
        if (pr.Status == PrStatus.Active)
            activeAge = DateTime.UtcNow - pr.CreationDate;

        return new PullRequestMetrics
        {
            PullRequestId = pr.PullRequestId,
            Title = pr.Title,
            RepositoryName = pr.RepositoryName,
            Status = pr.Status,
            IsDraft = pr.IsDraft,
            AuthorDisplayName = pr.AuthorDisplayName,
            IsAuthorBot = pr.IsAuthorBot,
            CreationDate = pr.CreationDate,
            ClosedDate = pr.ClosedDate,
            TotalCycleTime = totalCycleTime,
            TimeToFirstHumanComment = timeToFirstHumanComment,
            TimeToFirstApproval = timeToFirstApproval,
            TimeFromApprovalToMerge = timeFromApprovalToMerge,
            FilesChanged = pr.FilesChanged,
            CommitCount = pr.CommitCount,
            IterationCount = pr.Iterations.Count,
            HumanCommentCount = humanCommentCount,
            IsFirstTimeApproval = isFirstTimeApproval,
            ResolvableThreadCount = resolvableCount,
            ResolvedThreadCount = resolvedCount,
            ActiveReviewerCount = activeReviewers.Count,
            ActiveReviewers = activeReviewers,
            CreationDayOfWeek = pr.CreationDate.DayOfWeek,
            CreationHourOfDay = pr.CreationDate.Hour,
            ActiveAge = activeAge,
        };
    }

    public TeamMetrics AggregateTeamMetrics(
        List<PullRequestMetrics> prMetrics, List<PullRequestData> prData)
    {
        var completed = prMetrics.Where(m => m.Status == PrStatus.Completed).ToList();
        var completedNonDraft = completed.Where(m => !m.IsDraft).ToList();

        // Cycle time aggregates
        TimeSpan? avgCycleTime = null;
        TimeSpan? medianCycleTime = null;
        TimeSpan? avgTimeToFirstComment = null;
        TimeSpan? avgTimeToFirstApproval = null;

        var cycleTimes = completedNonDraft
            .Where(m => m.TotalCycleTime.HasValue)
            .Select(m => m.TotalCycleTime!.Value)
            .ToList();

        if (cycleTimes.Count > 0)
        {
            avgCycleTime = TimeSpan.FromTicks((long)cycleTimes.Average(t => t.Ticks));
            medianCycleTime = Median(cycleTimes);
        }

        var firstCommentTimes = completedNonDraft
            .Where(m => m.TimeToFirstHumanComment.HasValue)
            .Select(m => m.TimeToFirstHumanComment!.Value)
            .ToList();

        if (firstCommentTimes.Count > 0)
            avgTimeToFirstComment = TimeSpan.FromTicks((long)firstCommentTimes.Average(t => t.Ticks));

        var firstApprovalTimes = completedNonDraft
            .Where(m => m.TimeToFirstApproval.HasValue)
            .Select(m => m.TimeToFirstApproval!.Value)
            .ToList();

        if (firstApprovalTimes.Count > 0)
            avgTimeToFirstApproval = TimeSpan.FromTicks((long)firstApprovalTimes.Average(t => t.Ticks));

        // Size aggregates
        double avgFiles = prMetrics.Count > 0
            ? prMetrics.Average(m => m.FilesChanged)
            : 0;
        double avgCommits = prMetrics.Count > 0
            ? prMetrics.Average(m => m.CommitCount)
            : 0;

        // Quality rates (based on completed PRs)
        double abandonedRate = prMetrics.Count > 0
            ? (double)prMetrics.Count(m => m.Status == PrStatus.Abandoned) / prMetrics.Count
            : 0;
        double firstTimeApprovalRate = completed.Count > 0
            ? (double)completed.Count(m => m.IsFirstTimeApproval) / completed.Count
            : 0;

        // Thread resolution rate
        int totalResolvable = prMetrics.Sum(m => m.ResolvableThreadCount);
        int totalResolved = prMetrics.Sum(m => m.ResolvedThreadCount);
        double threadResolutionRate = totalResolvable > 0
            ? (double)totalResolved / totalResolvable
            : 0;

        // Throughput: PRs merged per week, per author
        var throughput = completed
            .GroupBy(m => m.AuthorDisplayName)
            .ToDictionary(
                g => g.Key,
                g => g.Where(m => m.ClosedDate.HasValue)
                    .GroupBy(m => StartOfWeek(m.ClosedDate!.Value))
                    .Select(wg => new WeeklyCount
                    {
                        WeekStart = wg.Key,
                        Count = wg.Count(),
                    })
                    .OrderBy(w => w.WeekStart)
                    .ToList());

        // Reviews per person
        var reviewsPerPerson = prData
            .SelectMany(pr => pr.Reviewers
                .Where(r => !r.IsContainer && r.Vote != 0 && r.Id != pr.AuthorId)
                .Select(r => r.DisplayName))
            .GroupBy(name => name)
            .ToDictionary(g => g.Key, g => g.Count());

        // PRs per author
        var prsPerAuthor = prMetrics
            .GroupBy(m => m.AuthorDisplayName)
            .ToDictionary(g => g.Key, g => g.Count());

        // Pairing matrix
        var pairingMatrix = prData
            .SelectMany(pr => pr.Reviewers
                .Where(r => !r.IsContainer && r.Vote != 0 && r.Id != pr.AuthorId)
                .Select(r => new ReviewerAuthorPair(pr.AuthorDisplayName, r.DisplayName)))
            .GroupBy(pair => pair)
            .ToDictionary(g => g.Key, g => g.Count());

        // Per-repository breakdown
        var perRepo = prMetrics
            .GroupBy(m => m.RepositoryName)
            .ToDictionary(g => g.Key, g =>
            {
                var repoPrs = g.ToList();
                var repoCompleted = repoPrs.Where(m => m.Status == PrStatus.Completed).ToList();
                var repoCompletedNonDraft = repoCompleted.Where(m => !m.IsDraft).ToList();
                var repoCycleTimes = repoCompletedNonDraft
                    .Where(m => m.TotalCycleTime.HasValue)
                    .Select(m => m.TotalCycleTime!.Value)
                    .ToList();

                return new RepositoryBreakdown
                {
                    TotalPrCount = repoPrs.Count,
                    CompletedPrCount = repoCompleted.Count,
                    AbandonedPrCount = repoPrs.Count(m => m.Status == PrStatus.Abandoned),
                    ActivePrCount = repoPrs.Count(m => m.Status == PrStatus.Active),
                    AbandonedRate = repoPrs.Count > 0
                        ? (double)repoPrs.Count(m => m.Status == PrStatus.Abandoned) / repoPrs.Count
                        : 0,
                    AvgCycleTime = repoCycleTimes.Count > 0
                        ? TimeSpan.FromTicks((long)repoCycleTimes.Average(t => t.Ticks))
                        : null,
                    MedianCycleTime = repoCycleTimes.Count > 0
                        ? Median(repoCycleTimes)
                        : null,
                    AvgFilesChanged = repoPrs.Count > 0
                        ? repoPrs.Average(m => m.FilesChanged)
                        : 0,
                    FirstTimeApprovalRate = repoCompleted.Count > 0
                        ? (double)repoCompleted.Count(m => m.IsFirstTimeApproval) / repoCompleted.Count
                        : 0,
                };
            });

        return new TeamMetrics
        {
            TotalPrCount = prMetrics.Count,
            CompletedPrCount = completed.Count,
            AbandonedPrCount = prMetrics.Count(m => m.Status == PrStatus.Abandoned),
            ActivePrCount = prMetrics.Count(m => m.Status == PrStatus.Active),
            AvgCycleTime = avgCycleTime,
            MedianCycleTime = medianCycleTime,
            AvgTimeToFirstComment = avgTimeToFirstComment,
            AvgTimeToFirstApproval = avgTimeToFirstApproval,
            AvgFilesChanged = avgFiles,
            AvgCommitsPerPr = avgCommits,
            AbandonedRate = abandonedRate,
            FirstTimeApprovalRate = firstTimeApprovalRate,
            ThreadResolutionRate = threadResolutionRate,
            ThroughputByAuthor = throughput,
            ReviewsPerPerson = reviewsPerPerson,
            PrsPerAuthor = prsPerAuthor,
            PairingMatrix = pairingMatrix,
            PerRepositoryBreakdown = perRepo,
        };
    }

    private static TimeSpan Median(List<TimeSpan> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
            return TimeSpan.FromTicks((sorted[mid - 1].Ticks + sorted[mid].Ticks) / 2);
        return sorted[mid];
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}
