using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using PrStats.Models;
using PrStats.Services;
using PrStats.Visualization;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PrStats.Configuration;

public sealed class CompareCommand : AsyncCommand<CompareCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--file <FILE>")]
        [Description("Path to a JSON report file (specify 2-5 times)")]
        public string[]? Files { get; init; }

        [CommandOption("--labels <LABELS>")]
        [Description("Comma-separated team labels (default: repository names from reports)")]
        public string? Labels { get; init; }

        [CommandOption("--output <FILE>")]
        [Description("Output HTML file path")]
        [DefaultValue("pr-comparison.html")]
        public string Output { get; init; } = "pr-comparison.html";

        [CommandOption("--no-open")]
        [Description("Skip auto-opening the report in the default browser")]
        [DefaultValue(false)]
        public bool NoOpen { get; init; }

        public override ValidationResult Validate()
        {
            if (Files == null || Files.Length < 2)
                return ValidationResult.Error("At least 2 --file options are required");

            if (Files.Length > 5)
                return ValidationResult.Error("At most 5 --file options are supported");

            if (!string.IsNullOrWhiteSpace(Labels))
            {
                var labelCount = Labels.Split(',').Length;
                if (labelCount != Files.Length)
                    return ValidationResult.Error(
                        $"--labels count ({labelCount}) must match --file count ({Files.Length})");
            }

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        var files = settings.Files!;

        // Validate all files exist
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(file)}");
                return 1;
            }
        }

        // Deserialize reports
        var reports = new List<PrStatsReport>();
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var report = JsonSerializer.Deserialize<PrStatsReport>(json, ReportExporter.JsonOptions)
                             ?? throw new JsonException("Deserialized report was null.");
                reports.Add(report);
            }
            catch (JsonException ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red]Error:[/] Failed to parse {Markup.Escape(file)}: {Markup.Escape(ex.Message)}");
                return 1;
            }
        }

        // Validate schema versions
        foreach (var report in reports)
        {
            if (report.SchemaVersion != PrStatsReport.CurrentSchemaVersion)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Warning:[/] Schema version mismatch (file: {report.SchemaVersion}, " +
                    $"expected: {PrStatsReport.CurrentSchemaVersion}). Results may be unexpected.");
            }
        }

        // Resolve labels
        var labels = ResolveLabels(settings, reports);

        // Detect time-window mismatches
        var dayValues = reports.Select(r => r.Days).Distinct().ToList();
        if (dayValues.Count > 1)
        {
            var minDays = dayValues.Min();
            var maxDays = dayValues.Max();
            if ((maxDays - minDays) / (double)minDays > 0.20)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]Warning:[/] Report time periods differ significantly " +
                    $"({minDays} days vs {maxDays} days). Metrics may not be directly comparable.");
            }
        }

        // Analyze
        var entries = ComparisonAnalyzer.Analyze(reports, labels);

        // Generate dashboard
        var html = ComparisonDashboardGenerator.Generate(entries);
        await File.WriteAllTextAsync(settings.Output, html);

        AnsiConsole.MarkupLine($"[green]Comparison report saved to:[/] {Markup.Escape(settings.Output)}");

        // Open browser
        if (!settings.NoOpen)
        {
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

        return 0;
    }

    private static List<string> ResolveLabels(Settings settings, List<PrStatsReport> reports)
    {
        List<string> labels;

        if (!string.IsNullOrWhiteSpace(settings.Labels))
        {
            labels = settings.Labels.Split(',').Select(l => l.Trim()).ToList();
        }
        else
        {
            labels = reports.Select(r => r.RepositoryDisplayName).ToList();
        }

        // Disambiguate duplicate labels by appending generation date
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (seen.TryGetValue(label, out _))
            {
                // Go back and fix the first occurrence too
                for (var j = 0; j < i; j++)
                {
                    if (string.Equals(labels[j], label, StringComparison.OrdinalIgnoreCase)
                        && !labels[j].Contains('('))
                    {
                        labels[j] = $"{labels[j]} ({reports[j].GeneratedAtUtc:yyyy-MM-dd})";
                    }
                }
                labels[i] = $"{label} ({reports[i].GeneratedAtUtc:yyyy-MM-dd})";
            }
            seen[label] = i;
        }

        return labels;
    }
}
