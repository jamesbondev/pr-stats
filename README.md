# PR Statistics Dashboard

A .NET 10 console app that fetches pull request data from Azure DevOps and generates an interactive HTML dashboard with 23 metrics across cycle time, size, quality, collaboration, and process patterns.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure DevOps Personal Access Token (PAT) with **Code (Read)** scope

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
  "Repository": "my-repo"
}
```

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
| `--bots` | | Comma-separated bot display names to exclude |
| `--no-open` | false | Skip auto-opening the report in browser |

## Metrics (23 total)

### Cycle Time
- Total cycle time (creation to close)
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
- Abandoned PR rate
- Self-merged PR rate
- Unreviewed PR rate
- Comment thread resolution rate

### Team & Collaboration
- Review load balance
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
├── Configuration/     # CLI command, settings
├── Models/            # Data models (PR data, metrics, team metrics)
├── Services/          # Azure DevOps client, metrics calculator, bot filter
└── Visualization/     # Dashboard generator, chart builders
tests/PrStats.Tests/
└── Services/          # MetricsCalculator and BotFilter tests
```
