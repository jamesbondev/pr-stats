using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class ReviewActivityCharts
{
    public static IEnumerable<GenericChart> Create(
        List<PullRequestMetrics> metrics, TeamMetrics teamMetrics)
    {
        // Reviews per person bar chart
        if (teamMetrics.ReviewsPerPerson.Count > 0)
            yield return CreateReviewsPerPersonBar(teamMetrics);

        // Comment depth distribution
        if (metrics.Any(m => m.HumanCommentCount > 0))
            yield return CreateCommentDepthHistogram(metrics);
    }

    private static GenericChart CreateReviewsPerPersonBar(TeamMetrics teamMetrics)
    {
        var sorted = teamMetrics.ReviewsPerPerson
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .ToList();

        var names = sorted.Select(kv => kv.Key).ToArray();
        var counts = sorted.Select(kv => kv.Value).ToArray();

        return CSharpChart.Bar<int, string, string>(
                values: counts,
                Keys: names,
                Name: "Reviews")
            .WithMarkerStyle(Color: Color.fromHex("#f59e0b"))
            .WithTitle("Top Reviewers")
            .WithXAxisStyle(Title.init("Reviews"))
            .WithYAxisStyle(Title.init("Reviewer"));
    }

    private static GenericChart CreateCommentDepthHistogram(List<PullRequestMetrics> metrics)
    {
        var counts = metrics.Select(m => (double)m.HumanCommentCount).ToArray();

        return CSharpChart.Histogram<double, double, string>(X: counts)
            .WithMarkerStyle(Color: Color.fromHex("#f59e0b"))
            .WithTitle("Review Depth (Human Comments per PR)")
            .WithXAxisStyle(Title.init("Comment Count"))
            .WithYAxisStyle(Title.init("Number of PRs"));
    }
}
