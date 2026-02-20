# PR Statistics Dashboard

A .NET 10 console app that fetches pull request data from Azure DevOps and generates an interactive HTML dashboard with 26 metrics across cycle time, size, quality, collaboration, and process patterns. Includes an AI chat feature for natural-language exploration of PR data using GitHub Copilot.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure DevOps Personal Access Token (PAT) with **Code (Read)** scope
- (For chat feature) A GitHub PAT with Copilot access (`GITHUB_TOKEN` or `GH_TOKEN` environment variable)

## Quick Start

```bash
# Build
dotnet build

# Run (PAT via environment variable)
export AZDO_PAT=your-pat-token
dotnet run --project src/PrStats -- \
    --org https://dev.azure.com/myorg \
    --project MyProject \
    --repo my-repo

# Or use appsettings.json (see Configuration below)
```

The report auto-opens in your default browser.

## Configuration

### appsettings.json

All required options can be configured in `appsettings.json` (in the working directory or next to the binary), so you can run with just `dotnet run --project src/PrStats`:

```json
{
  "Pat": "your-pat-token-here",
  "Organization": "https://dev.azure.com/myorg",
  "Project": "MyProject",
  "Repository": "my-repo",
  "Authors": ["Alice Smith", "Bob Jones"],
  "AuthorIds": ["guid1", "guid2"]
}
```

`Authors` and `AuthorIds` are optional — omit them to include all PR authors. When set, only PRs created by matching authors are included (matched case-insensitively by name or ID).

This file is excluded from git via `.gitignore`.

CLI flags override appsettings.json values when both are provided. For PAT specifically, the resolution order is:

1. `--pat` CLI flag (warns about shell history exposure)
2. `AZDO_PAT` environment variable
3. `appsettings.json`

### CLI Options

| Option | Default | Description |
|--------|---------|-------------|
| `--org` | | Azure DevOps organization URL |
| `--project` | | Azure DevOps project name |
| `--repo` | | Repository name |
| `--days` | 90 | Lookback period in days |
| `--output` | pr-report.html | Output HTML file path |
| `--pat` | | PAT (prefer env var or appsettings.json) |
| `--authors` | | Comma-separated author display names to include (only PRs by these authors) |
| `--author-ids` | | Comma-separated author user IDs to include (only PRs by these authors) |
| `--bots` | | Comma-separated bot display names to exclude |
| `--bot-ids` | | Comma-separated bot user IDs to exclude |
| `--no-open` | false | Skip auto-opening the report in browser |
| `--max-prs` | unlimited | Maximum number of PRs to enrich |
| `--no-cache` | false | Forces full re-enrichment of all PRs, ignoring cached data |
| `--clear-cache` | false | Delete the PR cache and exit |
| `--json` | false | Export PR data as JSON alongside the HTML report |

## AI Chat

The `chat` subcommand lets you ask natural-language questions about your PR data using GitHub Copilot. First generate a JSON export with `--json`, then start an interactive chat session.

### Usage

```bash
# 1. Generate the report with JSON export
dotnet run --project src/PrStats -- --org https://dev.azure.com/myorg --project MyProject --json

# 2. Start the chat session
export GITHUB_TOKEN=your-github-pat
dotnet run --project src/PrStats -- chat --data pr-report.json

# With a different model
dotnet run --project src/PrStats -- chat --data pr-report.json --model gpt-4.1
```

### Chat Options

| Option | Default | Description |
|--------|---------|-------------|
| `--data` | (required) | Path to the JSON data export file |
| `--model` | gpt-4.1 | Copilot model name |

### Authentication

The chat feature requires a GitHub Personal Access Token with Copilot access. Set it via the `GITHUB_TOKEN` or `GH_TOKEN` environment variable:

```bash
export GITHUB_TOKEN=ghp_your_token_here
```

### Example Questions

- "Show me the team summary"
- "Which PR has the longest cycle time?"
- "Show PRs by Alice with cycle time over 100 hours"
- "Tell me about PR #123"
- "Who does the most code reviews?"
- "What's the repository breakdown?"

Type `exit` or `quit` to end the session. Press `Ctrl+C` for a clean exit.

## Caching

Enrichment data (threads, iterations, file changes) is cached locally so subsequent runs skip API calls for completed and abandoned PRs. Active PRs are always re-enriched.

- **Cache location:** `{LocalApplicationData}/PrStats/cache/` (e.g. `~/.local/share/PrStats/cache/` on Linux)
- **Eviction:** PRs older than 180 days are automatically pruned on each write
- **Bypass:** Use `--no-cache` to force full re-enrichment (still writes cache afterwards)
- **Clear:** Use `--clear-cache --org <ORG> --project <PROJECT>` to delete the cache file and exit

## Draft PR Handling

When a PR is created as a draft and later published, cycle time starts from the **published date** (when the PR became ready for review), not the creation date. This avoids inflating cycle time with development/draft time and matches DORA/LinearB best practices. If no published date is detected, the tool falls back to the creation date.

## Metrics (26 total)

### Cycle Time
- Total cycle time (published date to close, or creation to close if never draft)
- Time to first human comment
- Time to first approval
- Time from approval to merge

### Size & Throughput
- Files changed per PR
- PR throughput (merged per week, per author)
- Commits per PR
- Iteration count (push count)

### Quality & Review
- Review depth (human comments per PR)
- First-time approval rate
- Approval reset count (times approvals were invalidated by new pushes)
- Approval reset rate
- Abandoned PR rate
- Self-merged PR rate
- Unreviewed PR rate
- Comment thread resolution rate

### Team & Collaboration
- Review load balance
- Comment thread activity per person
- Top PR creators
- Top reviewers
- Reviewer-author pairing matrix
- Active reviewer count per PR

### Process Patterns
- PR status distribution
- PRs by day of week / hour of day
- Merge strategy distribution
- PR age distribution (active PRs)

## Dashboard Sections

1. **Executive Summary** - KPI cards with color-coded thresholds
2. **Cycle Time Analysis** - Box plots and scatter trend
3. **PR Size Distribution** - Histogram and size vs review time
4. **Throughput** - Weekly bar chart
5. **Review Activity** - Reviewer bar charts and comment depth
6. **Team Collaboration** - Reviewer-author heatmap
7. **Quality Indicators** - Pie charts for self-merge, unreviewed, first-time approval rates
8. **Temporal Patterns** - PRs by day of week and hour of day

## Development

```bash
# Run tests
dotnet test

# Build
dotnet build
```

## Project Structure

```
src/PrStats/
├── Chat/              # AI chat session, PR data tools
├── Configuration/     # CLI commands, settings
├── Models/            # Data models (PR data, metrics, team metrics, report)
├── Services/          # Azure DevOps client, metrics calculator, bot filter, report exporter
└── Visualization/     # Dashboard generator, chart builders
tests/PrStats.Tests/
├── Chat/              # PrDataTools tests
└── Services/          # MetricsCalculator, BotFilter, ReportExporter tests
```
