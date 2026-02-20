using PrStats.Models;

namespace PrStats.Services;

public sealed class MetricsCalculator
{
    public PullRequestMetrics CalculatePerPR(PullRequestData pr, List<BuildInfo>? builds = null)
    {
        var isCompleted = pr.Status == PrStatus.Completed;
        var includeCycleTime = isCompleted && !pr.IsDraft;
        var cycleStart = pr.PublishedDate ?? pr.CreationDate;

        // Cycle time metrics
        TimeSpan? totalCycleTime = null;
        TimeSpan? timeToFirstHumanComment = null;
        TimeSpan? timeToFirstApproval = null;
        TimeSpan? timeFromApprovalToMerge = null;

        if (includeCycleTime && pr.ClosedDate.HasValue)
        {
            totalCycleTime = pr.ClosedDate.Value - cycleStart;

            // Time to first human comment (non-bot, non-author, text comment)
            var firstHumanComment = pr.Threads
                .Where(t => t.CommentType == "text"
                    && !t.IsAuthorBot
                    && t.AuthorId != pr.AuthorId
                    && !t.IsVoteUpdate)
                .OrderBy(t => t.PublishedDate)
                .FirstOrDefault();

            if (firstHumanComment != null)
                timeToFirstHumanComment = firstHumanComment.PublishedDate - cycleStart;

            // Time to first approval (from VoteUpdate threads with vote >= 5)
            var firstApproval = pr.Threads
                .Where(t => t.IsVoteUpdate && t.VoteValue.HasValue && t.VoteValue.Value >= 5)
                .OrderBy(t => t.PublishedDate)
                .FirstOrDefault();

            if (firstApproval != null)
            {
                timeToFirstApproval = firstApproval.PublishedDate - cycleStart;
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

        // Approval reset count (completed PRs only)
        int approvalResetCount = isCompleted ? CalculateApprovalResetCount(pr) : 0;

        // Active age for active PRs
        TimeSpan? activeAge = null;
        if (pr.Status == PrStatus.Active)
            activeAge = DateTime.UtcNow - cycleStart;

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
            PublishedDate = pr.PublishedDate,
            TotalCycleTime = totalCycleTime,
            TimeToFirstHumanComment = timeToFirstHumanComment,
            TimeToFirstApproval = timeToFirstApproval,
            TimeFromApprovalToMerge = timeFromApprovalToMerge,
            FilesChanged = pr.FilesChanged,
            CommitCount = pr.CommitCount,
            IterationCount = pr.Iterations.Count,
            HumanCommentCount = humanCommentCount,
            IsFirstTimeApproval = isFirstTimeApproval,
            ApprovalResetCount = approvalResetCount,
            ResolvableThreadCount = resolvableCount,
            ResolvedThreadCount = resolvedCount,
            ActiveReviewerCount = activeReviewers.Count,
            ActiveReviewers = activeReviewers,
            CreationDayOfWeek = pr.CreationDate.DayOfWeek,
            CreationHourOfDay = pr.CreationDate.Hour,
            ActiveAge = activeAge,
            BuildMetrics = builds is { Count: > 0 } ? CalculateBuildMetrics(builds) : null,
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
        double approvalResetRate = completed.Count > 0
            ? (double)completed.Count(m => m.ApprovalResetCount >= 1) / completed.Count
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

        // Comments per person (threads initiated on others' PRs)
        var commentsPerPerson = prData
            .SelectMany(pr => pr.Threads
                .Where(t => t.CommentType == "text"
                    && !t.IsAuthorBot
                    && t.AuthorId != pr.AuthorId
                    && !t.IsVoteUpdate)
                .Select(t => t.AuthorDisplayName))
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
                    ApprovalResetRate = repoCompleted.Count > 0
                        ? (double)repoCompleted.Count(m => m.ApprovalResetCount >= 1) / repoCompleted.Count
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
            ApprovalResetRate = approvalResetRate,
            ThreadResolutionRate = threadResolutionRate,
            ThroughputByAuthor = throughput,
            ReviewsPerPerson = reviewsPerPerson,
            CommentsPerPerson = commentsPerPerson,
            PrsPerAuthor = prsPerAuthor,
            PairingMatrix = pairingMatrix,
            PerRepositoryBreakdown = perRepo,
            BuildMetrics = AggregateTeamBuildMetrics(prMetrics),
        };
    }

    internal static PrBuildMetrics CalculateBuildMetrics(List<BuildInfo> builds)
    {
        int succeeded = builds.Count(b => b.Result == "Succeeded");
        int failed = builds.Count(b => b.Result == "Failed");
        int canceled = builds.Count(b => b.Result == "Canceled");
        int partiallySucceeded = builds.Count(b => b.Result == "PartiallySucceeded");

        int terminalCount = succeeded + failed + partiallySucceeded;
        double successRate = terminalCount > 0 ? (double)succeeded / terminalCount : 0;

        // Queue time: QueueTime -> StartTime (time waiting for agent)
        var queueTimes = builds
            .Where(b => b.StartTime.HasValue)
            .Select(b => b.StartTime!.Value - b.QueueTime)
            .Where(t => t >= TimeSpan.Zero)
            .ToList();

        // Run time: StartTime -> FinishTime (actual build execution)
        var runTimes = builds
            .Where(b => b.StartTime.HasValue && b.FinishTime.HasValue)
            .Select(b => b.FinishTime!.Value - b.StartTime!.Value)
            .Where(t => t >= TimeSpan.Zero)
            .ToList();

        // Elapsed time: QueueTime -> FinishTime (total wall clock per build)
        var elapsedTimes = builds
            .Where(b => b.FinishTime.HasValue)
            .Select(b => b.FinishTime!.Value - b.QueueTime)
            .Where(t => t >= TimeSpan.Zero)
            .ToList();

        // Per-pipeline breakdown
        var perPipeline = builds
            .GroupBy(b => b.DefinitionName)
            .Select(g =>
            {
                var pipelineRunTimes = g
                    .Where(b => b.StartTime.HasValue && b.FinishTime.HasValue)
                    .Select(b => b.FinishTime!.Value - b.StartTime!.Value)
                    .Where(t => t >= TimeSpan.Zero)
                    .ToList();

                return new PipelineSummary
                {
                    DefinitionName = g.Key,
                    RunCount = g.Count(),
                    SucceededCount = g.Count(b => b.Result == "Succeeded"),
                    FailedCount = g.Count(b => b.Result == "Failed"),
                    AvgDuration = pipelineRunTimes.Count > 0
                        ? TimeSpan.FromTicks((long)pipelineRunTimes.Average(t => t.Ticks))
                        : null,
                };
            })
            .ToList();

        return new PrBuildMetrics
        {
            TotalBuildCount = builds.Count,
            SucceededCount = succeeded,
            FailedCount = failed,
            CanceledCount = canceled,
            PartiallySucceededCount = partiallySucceeded,
            BuildSuccessRate = successRate,
            AvgQueueTime = queueTimes.Count > 0
                ? TimeSpan.FromTicks((long)queueTimes.Average(t => t.Ticks))
                : null,
            AvgRunTime = runTimes.Count > 0
                ? TimeSpan.FromTicks((long)runTimes.Average(t => t.Ticks))
                : null,
            TotalElapsedTime = elapsedTimes.Count > 0
                ? TimeSpan.FromTicks(elapsedTimes.Sum(t => t.Ticks))
                : null,
            TotalRunTime = runTimes.Count > 0
                ? TimeSpan.FromTicks(runTimes.Sum(t => t.Ticks))
                : null,
            PerPipeline = perPipeline,
        };
    }

    private static TeamBuildMetrics? AggregateTeamBuildMetrics(List<PullRequestMetrics> prMetrics)
    {
        var prsWithBuilds = prMetrics
            .Where(m => m.BuildMetrics != null)
            .ToList();

        if (prsWithBuilds.Count == 0)
            return null;

        var buildCounts = prsWithBuilds.Select(m => (double)m.BuildMetrics!.TotalBuildCount).ToList();
        int totalBuilds = prsWithBuilds.Sum(m => m.BuildMetrics!.TotalBuildCount);
        int totalSucceeded = prsWithBuilds.Sum(m => m.BuildMetrics!.SucceededCount);
        int totalFailed = prsWithBuilds.Sum(m => m.BuildMetrics!.FailedCount);
        int totalPartial = prsWithBuilds.Sum(m => m.BuildMetrics!.PartiallySucceededCount);

        int terminalCount = totalSucceeded + totalFailed + totalPartial;
        double overallSuccessRate = terminalCount > 0 ? (double)totalSucceeded / terminalCount : 0;

        // Collect all run times across all PRs for median
        var allRunTimes = prsWithBuilds
            .SelectMany(m => m.BuildMetrics!.PerPipeline)
            .Where(p => p.AvgDuration.HasValue)
            .Select(p => p.AvgDuration!.Value)
            .ToList();

        // Avg and median build run time from per-PR averages
        var prAvgRunTimes = prsWithBuilds
            .Where(m => m.BuildMetrics!.AvgRunTime.HasValue)
            .Select(m => m.BuildMetrics!.AvgRunTime!.Value)
            .ToList();

        // Avg queue time from per-PR averages
        var prAvgQueueTimes = prsWithBuilds
            .Where(m => m.BuildMetrics!.AvgQueueTime.HasValue)
            .Select(m => m.BuildMetrics!.AvgQueueTime!.Value)
            .ToList();

        // CI elapsed time per PR
        var prElapsedTimes = prsWithBuilds
            .Where(m => m.BuildMetrics!.TotalElapsedTime.HasValue)
            .Select(m => m.BuildMetrics!.TotalElapsedTime!.Value)
            .ToList();

        // Median build count
        var sortedCounts = buildCounts.OrderBy(c => c).ToList();
        double medianBuilds = sortedCounts.Count % 2 == 0
            ? (sortedCounts[sortedCounts.Count / 2 - 1] + sortedCounts[sortedCounts.Count / 2]) / 2
            : sortedCounts[sortedCounts.Count / 2];

        // Per-pipeline team summary
        var perPipeline = prsWithBuilds
            .SelectMany(m => m.BuildMetrics!.PerPipeline)
            .GroupBy(p => p.DefinitionName)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    int runs = g.Sum(p => p.RunCount);
                    int succ = g.Sum(p => p.SucceededCount);
                    int fail = g.Sum(p => p.FailedCount);
                    int term = succ + fail;
                    var durations = g
                        .Where(p => p.AvgDuration.HasValue)
                        .Select(p => p.AvgDuration!.Value)
                        .ToList();

                    return new PipelineTeamSummary
                    {
                        TotalRuns = runs,
                        SuccessRate = term > 0 ? (double)succ / term : 0,
                        AvgDuration = durations.Count > 0
                            ? TimeSpan.FromTicks((long)durations.Average(t => t.Ticks))
                            : null,
                    };
                });

        return new TeamBuildMetrics
        {
            TotalBuildsAcrossAllPrs = totalBuilds,
            AvgBuildsPerPr = prsWithBuilds.Count > 0 ? buildCounts.Average() : 0,
            MedianBuildsPerPr = medianBuilds,
            OverallBuildSuccessRate = overallSuccessRate,
            AvgBuildRunTime = prAvgRunTimes.Count > 0
                ? TimeSpan.FromTicks((long)prAvgRunTimes.Average(t => t.Ticks))
                : null,
            MedianBuildRunTime = prAvgRunTimes.Count > 0
                ? Median(prAvgRunTimes)
                : null,
            AvgQueueTime = prAvgQueueTimes.Count > 0
                ? TimeSpan.FromTicks((long)prAvgQueueTimes.Average(t => t.Ticks))
                : null,
            AvgCiElapsedTimePerPr = prElapsedTimes.Count > 0
                ? TimeSpan.FromTicks((long)prElapsedTimes.Average(t => t.Ticks))
                : null,
            TotalCiElapsedTime = prElapsedTimes.Count > 0
                ? TimeSpan.FromTicks(prElapsedTimes.Sum(t => t.Ticks))
                : null,
            PerPipeline = perPipeline,
        };
    }

    private static int CalculateApprovalResetCount(PullRequestData pr)
    {
        // Collect approval votes (VoteValue >= 5)
        var approvals = pr.Threads
            .Where(t => t.IsVoteUpdate && t.VoteValue.HasValue && t.VoteValue.Value >= 5)
            .Select(t => (Timestamp: t.PublishedDate, IsApproval: true));

        // Collect push iterations (Push or ForcePush only)
        var pushes = pr.Iterations
            .Where(i => i.Reason is "Push" or "ForcePush")
            .Select(i => (Timestamp: i.CreatedDate, IsApproval: false));

        // Interleave chronologically; at same timestamp, votes come before pushes (conservative)
        var events = approvals.Concat(pushes)
            .OrderBy(e => e.Timestamp)
            .ThenByDescending(e => e.IsApproval)
            .ToList();

        int resetCount = 0;
        bool hasApproval = false;

        foreach (var evt in events)
        {
            if (evt.IsApproval)
            {
                hasApproval = true;
            }
            else if (hasApproval)
            {
                resetCount++;
                hasApproval = false;
            }
        }

        return resetCount;
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
