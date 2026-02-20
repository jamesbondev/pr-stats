using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using PrStats.Services;
using PrStats.Visualization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PrStats.Configuration;

public sealed class PrStatsCommand : AsyncCommand<PrStatsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--org <ORG>")]
        [Description("Azure DevOps organization URL (e.g., https://dev.azure.com/myorg)")]
        public string? Organization { get; init; }

        [CommandOption("--project <PROJECT>")]
        [Description("Azure DevOps project name")]
        public string? Project { get; init; }

        [CommandOption("--repo <REPO>")]
        [Description("Repository name (comma-separated for multiple, omit for all)")]
        public string? Repository { get; init; }

        [CommandOption("--days <DAYS>")]
        [Description("Lookback period in days (default: 90)")]
        [DefaultValue(90)]
        public int Days { get; init; } = 90;

        [CommandOption("--output <FILE>")]
        [Description("Output HTML file path (default: pr-report.html)")]
        [DefaultValue("pr-report.html")]
        public string Output { get; init; } = "pr-report.html";

        [CommandOption("--pat <PAT>")]
        [Description("Personal access token (prefer AZDO_PAT env var or appsettings.json)")]
        public string? Pat { get; init; }

        [CommandOption("--bots <NAMES>")]
        [Description("Comma-separated bot display names to exclude (e.g., \"Azure Pipelines,Dependabot\")")]
        public string? Bots { get; init; }

        [CommandOption("--bot-ids <IDS>")]
        [Description("Comma-separated bot user IDs to exclude (e.g., \"guid1,guid2\")")]
        public string? BotIds { get; init; }

        [CommandOption("--authors <NAMES>")]
        [Description("Comma-separated author display names to include (only PRs by these authors)")]
        public string? Authors { get; init; }

        [CommandOption("--author-ids <IDS>")]
        [Description("Comma-separated author user IDs to include (only PRs by these authors)")]
        public string? AuthorIds { get; init; }

        [CommandOption("--no-open")]
        [Description("Skip auto-opening the report in the default browser")]
        [DefaultValue(false)]
        public bool NoOpen { get; init; }

        [CommandOption("--max-prs <N>")]
        [Description("Maximum number of PRs to enrich (default: unlimited)")]
        public int? MaxPrs { get; init; }

        [CommandOption("--no-cache")]
        [Description("Forces full re-enrichment of all PRs, ignoring cached data")]
        [DefaultValue(false)]
        public bool NoCache { get; init; }

        [CommandOption("--clear-cache")]
        [Description("Delete the PR cache and exit")]
        [DefaultValue(false)]
        public bool ClearCache { get; init; }

        [CommandOption("--json")]
        [Description("Export PR data as JSON alongside the HTML report")]
        [DefaultValue(false)]
        public bool Json { get; init; }

        public override ValidationResult Validate()
        {
            if (Days < 1)
                return ValidationResult.Error("--days must be at least 1");

            if (MaxPrs.HasValue && MaxPrs.Value < 1)
                return ValidationResult.Error("--max-prs must be at least 1");

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var fileConfig = LoadAppSettings();

        // PAT resolution: --pat flag > AZDO_PAT env var > appsettings.json
        var pat = settings.Pat;
        if (!string.IsNullOrEmpty(pat))
        {
            AnsiConsole.MarkupLine(
                "[yellow]Warning:[/] PAT provided via command line - consider using AZDO_PAT " +
                "environment variable or appsettings.json instead to avoid exposure in shell history and process listings.");
        }

        if (string.IsNullOrEmpty(pat))
            pat = Environment.GetEnvironmentVariable("AZDO_PAT");

        if (string.IsNullOrEmpty(pat))
            pat = fileConfig?["Pat"];

        if (string.IsNullOrEmpty(pat))
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] No PAT provided. Set AZDO_PAT environment variable, use --pat, " +
                "or add a \"Pat\" key to appsettings.json.");
            return 1;
        }

        // Org/Project resolution: CLI flag > appsettings.json
        var org = settings.Organization ?? fileConfig?["Organization"];
        var project = settings.Project ?? fileConfig?["Project"];

        if (string.IsNullOrWhiteSpace(org))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Organization is required. Use --org or set \"Organization\" in appsettings.json.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Project is required. Use --project or set \"Project\" in appsettings.json.");
            return 1;
        }

        // Repository resolution: CLI flag > appsettings.json (string) > appsettings.json (array)
        var repoRaw = settings.Repository ?? fileConfig?["Repository"];
        var repositories = string.IsNullOrWhiteSpace(repoRaw)
            ? new List<string>()
            : repoRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

        // Also merge array-style "Repositories" from appsettings.json
        if (string.IsNullOrWhiteSpace(settings.Repository))
        {
            var repoSection = fileConfig?.GetSection("Repositories");
            if (repoSection != null)
            {
                foreach (var child in repoSection.GetChildren())
                {
                    if (!string.IsNullOrWhiteSpace(child.Value) &&
                        !repositories.Contains(child.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                        repositories.Add(child.Value.Trim());
                }
            }
        }

        var botNames = string.IsNullOrWhiteSpace(settings.Bots)
            ? new List<string>()
            : settings.Bots.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        var botIds = string.IsNullOrWhiteSpace(settings.BotIds)
            ? new List<string>()
            : settings.BotIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // Also load bot IDs from appsettings.json if present
        var configBotIdsSection = fileConfig?.GetSection("BotIds");
        if (configBotIdsSection != null)
        {
            foreach (var child in configBotIdsSection.GetChildren())
            {
                var id = child.Value;
                if (!string.IsNullOrWhiteSpace(id) && !botIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    botIds.Add(id.Trim());
            }
        }

        var authors = string.IsNullOrWhiteSpace(settings.Authors)
            ? new List<string>()
            : settings.Authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // Also load authors from appsettings.json if present
        var configAuthorsSection = fileConfig?.GetSection("Authors");
        if (configAuthorsSection != null)
        {
            foreach (var child in configAuthorsSection.GetChildren())
            {
                var name = child.Value;
                if (!string.IsNullOrWhiteSpace(name) && !authors.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase))
                    authors.Add(name.Trim());
            }
        }

        var authorIds = string.IsNullOrWhiteSpace(settings.AuthorIds)
            ? new List<string>()
            : settings.AuthorIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // Also load author IDs from appsettings.json if present
        var configAuthorIdsSection = fileConfig?.GetSection("AuthorIds");
        if (configAuthorIdsSection != null)
        {
            foreach (var child in configAuthorIdsSection.GetChildren())
            {
                var id = child.Value;
                if (!string.IsNullOrWhiteSpace(id) && !authorIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    authorIds.Add(id.Trim());
            }
        }

        var appSettings = new AppSettings
        {
            Organization = org.TrimEnd('/'),
            Project = project,
            Repositories = repositories,
            Pat = pat,
            Days = settings.Days,
            Output = settings.Output,
            BotNames = botNames,
            BotIds = botIds,
            NoOpen = settings.NoOpen,
            MaxPrs = settings.MaxPrs,
            NoCache = settings.NoCache,
            ClearCache = settings.ClearCache,
            Authors = authors,
            AuthorIds = authorIds,
            Json = settings.Json,
        };

        // Handle --clear-cache: delete cache and exit
        if (appSettings.ClearCache)
        {
            PrCache.DeleteCache(appSettings.Organization, appSettings.Project);
            AnsiConsole.MarkupLine("[green]Cache deleted.[/]");
            return 0;
        }

        // Print mode indicator
        var modeLabel = appSettings.AllRepositories
            ? "all repositories"
            : appSettings.Repositories.Count == 1
                ? $"repository: {appSettings.Repositories[0]}"
                : $"repositories: {appSettings.RepositoryDisplayName}";
        AnsiConsole.MarkupLine($"[blue]Mode:[/] {Markup.Escape(modeLabel)}");

        try
        {
            Console.Write("Connecting to Azure DevOps...");
            var client = new AzureDevOpsClient(appSettings);

            Console.Write("\rFetching pull requests...                ");
            var pullRequests = await client.FetchPullRequestsAsync();

            if (pullRequests.Count == 0)
            {
                Console.WriteLine($"\rNo pull requests found in the last {appSettings.Days} days.");
                var emptyDashboard = DashboardGenerator.GenerateEmpty(appSettings);
                await File.WriteAllTextAsync(appSettings.Output, emptyDashboard);
                Console.WriteLine($"Empty report written to {appSettings.Output}");
                OpenReport(appSettings);
                return 0;
            }

            Console.Write($"\rCalculating metrics for {pullRequests.Count} PRs...");
            var calculator = new MetricsCalculator();
            var prMetrics = pullRequests
                .Select(calculator.CalculatePerPR)
                .ToList();
            var teamMetrics = calculator.AggregateTeamMetrics(prMetrics, pullRequests);

            Console.Write("\rGenerating dashboard...                     ");
            var html = DashboardGenerator.Generate(appSettings, pullRequests, prMetrics, teamMetrics);
            await File.WriteAllTextAsync(appSettings.Output, html);

            if (appSettings.Json)
            {
                var jsonPath = Path.ChangeExtension(appSettings.Output, ".json");
                await ReportExporter.ExportJsonAsync(jsonPath, appSettings, pullRequests, prMetrics, teamMetrics);
                Console.WriteLine($"\rData exported to {jsonPath}                              ");
            }

            Console.WriteLine($"\rDone! Report written to {appSettings.Output}              ");
            OpenReport(appSettings);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static IConfiguration? LoadAppSettings()
    {
        // Check working directory first, then next to the binary
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path == null)
            return null;

        try
        {
            var dir = Path.GetDirectoryName(path)!;
            return new ConfigurationBuilder()
                .SetBasePath(dir)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();
        }
        catch
        {
            return null;
        }
    }

    private static void OpenReport(AppSettings settings)
    {
        if (settings.NoOpen)
            return;

        try
        {
            var fullPath = Path.GetFullPath(settings.Output);
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore if we can't open the browser
        }
    }
}
