using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using PrStats.Models;

namespace PrStats.Chat;

public class ChatSession
{
    private readonly PrStatsReport _report;
    private readonly string _model;

    private const string SystemPrompt = """
        You are a PR metrics analyst. You have access to pull request data from Azure DevOps
        including raw PR details and calculated metrics.

        ## Metric Definitions (match exactly how the data was calculated)

        - **Total Cycle Time**: ClosedDate minus CreationDate. Only calculated for completed,
          non-draft PRs. Null for active, abandoned, and draft PRs.
        - **Time to First Human Comment**: Time from PR creation to the first thread where
          CommentType is "text", the author is not a bot (IsAuthorBot=false), the author is
          not the PR author (AuthorId != PR.AuthorId), and it's not a vote update. Null if
          no such comment exists.
        - **Time to First Approval**: Time from PR creation to the first VoteUpdate thread
          where VoteValue >= 5 (Approved or ApprovedWithSuggestions). Null if no approval.
        - **Time from Approval to Merge**: ClosedDate minus the first approval date. Null if
          no approval.
        - **Human Comment Count**: Count of threads where CommentType is "text", author is not
          a bot, and author is not the PR author. Includes all statuses.
        - **First-Time Approval**: True if the PR was approved (VoteValue >= 5) before the
          second iteration (push), or if there was only one iteration with an approval.
          Only calculated for completed PRs.
        - **Resolvable Threads**: Text threads from non-bot authors.
        - **Resolved Threads**: Resolvable threads with status "fixed", "closed", "wontFix",
          or "byDesign".
        - **Active Reviewers**: Non-container reviewers who voted (Vote != 0).
        - **Active Age**: For active PRs only: current time minus CreationDate.
        - **Abandoned Rate**: Abandoned PRs / Total PRs (all statuses).
        - **First-Time Approval Rate**: Completed PRs with first-time approval / All completed PRs.
        - **Thread Resolution Rate**: Resolved threads / Resolvable threads (across all PRs).

        ## Tool Usage Guidelines

        1. Call GetTeamSummary first to understand the overall context
        2. Use SearchPullRequests for filtering and finding specific PRs
        3. Use GetPullRequestDetail for deep dives into individual PRs
        4. Use GetAuthorStats/GetReviewerStats for person-specific questions
        5. Use GetSlowestPullRequests/GetFastestPullRequests for ranking questions

        ## Formatting Rules

        - Format durations naturally: "3.2 days" for multi-day, "4.5 hours" for sub-day,
          "45 minutes" for sub-hour
        - Always include PR ID when referencing specific PRs
        - For large values also show the alternative unit: "773 hours (32.2 days)"
        - Use tables for comparing multiple items
        - This is a point-in-time snapshot, not time-series data — note this for trend questions
        """;

    public ChatSession(PrStatsReport report, string model)
    {
        _report = report;
        _model = model;
    }

    public async Task RunAsync()
    {
        var cancelled = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancelled = true;
            Console.WriteLine();
            Console.WriteLine("Bye!");
        };

        var tools = CreateTools();

        await using var client = new CopilotClient();
        await using var session = await client.CreateSessionAsync(new SessionConfig
        {
            Model = _model,
            Tools = tools,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = SystemPrompt,
            },
        });

        using var eventSub = session.On(evt =>
        {
            switch (evt)
            {
                case AssistantReasoningEvent reasoning:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(reasoning.Data.Content);
                    Console.ResetColor();
                    break;
                case ToolExecutionStartEvent tool:
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine($"  [{tool.Data.ToolName}]");
                    Console.ResetColor();
                    break;
            }
        });

        // Welcome banner
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"PR Data Chat — {_report.TeamMetrics.TotalPrCount} PRs from {_report.RepositoryDisplayName}");
        Console.WriteLine($"Model: {_model} | Type 'exit' to quit");
        Console.ResetColor();
        Console.WriteLine();

        while (!cancelled)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("You: ");
            Console.ResetColor();

            var input = Console.ReadLine()?.Trim();
            if (cancelled)
                break;

            if (string.IsNullOrEmpty(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            Console.WriteLine();

            try
            {
                var reply = await session.SendAndWaitAsync(
                    new MessageOptions { Prompt = input },
                    timeout: TimeSpan.FromMinutes(3));

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(reply?.Data.Content ?? "No response received.");
                Console.ResetColor();
                Console.WriteLine();
            }
            catch (OperationCanceledException) when (cancelled)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine();
            }
        }
    }

    private IList<AIFunction> CreateTools()
    {
        var tools = new PrDataTools(_report);

        return
        [
            AIFunctionFactory.Create(tools.GetTeamSummary),
            AIFunctionFactory.Create(tools.SearchPullRequests),
            AIFunctionFactory.Create(tools.GetPullRequestDetail),
            AIFunctionFactory.Create(tools.GetAuthorStats),
            AIFunctionFactory.Create(tools.GetReviewerStats),
            AIFunctionFactory.Create(tools.GetRepositoryBreakdown),
            AIFunctionFactory.Create(tools.GetSlowestPullRequests),
            AIFunctionFactory.Create(tools.GetFastestPullRequests),
        ];
    }
}
