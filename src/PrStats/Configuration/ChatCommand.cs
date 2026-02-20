using System.ComponentModel;
using System.Text.Json;
using PrStats.Chat;
using PrStats.Models;
using PrStats.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PrStats.Configuration;

public sealed class ChatCommand : AsyncCommand<ChatCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--data <FILE>")]
        [Description("Path to the JSON data export file (required)")]
        public required string Data { get; init; }

        [CommandOption("--model <MODEL>")]
        [Description("Copilot model name (default: gpt-4.1)")]
        [DefaultValue("gpt-4.1")]
        public string Model { get; init; } = "gpt-4.1";

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Data))
                return ValidationResult.Error("--data is required");

            return ValidationResult.Success();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellation)
    {
        // Validate data file exists
        if (!File.Exists(settings.Data))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(settings.Data)}");
            return 1;
        }

        // Validate GitHub token
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN");

        if (string.IsNullOrEmpty(token))
        {
            AnsiConsole.MarkupLine(
                "[red]Error:[/] GITHUB_TOKEN (or GH_TOKEN) environment variable is required for Copilot SDK authentication. " +
                "Set it with: export GITHUB_TOKEN=your-token");
            return 1;
        }

        // Deserialize report
        PrStatsReport report;
        try
        {
            var json = await File.ReadAllTextAsync(settings.Data, cancellation);
            report = JsonSerializer.Deserialize<PrStatsReport>(json, ReportExporter.JsonOptions)
                     ?? throw new JsonException("Deserialized report was null.");
        }
        catch (JsonException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to parse JSON data: {Markup.Escape(ex.Message)}");
            return 1;
        }

        // Validate schema version
        if (report.SchemaVersion != PrStatsReport.CurrentSchemaVersion)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Warning:[/] Schema version mismatch (file: {report.SchemaVersion}, expected: {PrStatsReport.CurrentSchemaVersion}). " +
                "Results may be unexpected.");
        }

        // Run chat session
        try
        {
            var session = new ChatSession(report, settings.Model);
            await session.RunAsync();
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
