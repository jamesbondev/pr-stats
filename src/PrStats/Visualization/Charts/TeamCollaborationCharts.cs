using Plotly.NET;
using Plotly.NET.LayoutObjects;
using PrStats.Models;
using CSharpChart = Plotly.NET.CSharp.Chart;
using static Plotly.NET.CSharp.GenericChartExtensions;
using GenericChart = Plotly.NET.GenericChart;

namespace PrStats.Visualization.Charts;

public static class TeamCollaborationCharts
{
    public static IEnumerable<GenericChart> Create(TeamMetrics teamMetrics)
    {
        if (teamMetrics.PairingMatrix.Count == 0)
            yield break;

        yield return CreatePairingHeatmap(teamMetrics);
    }

    private static GenericChart CreatePairingHeatmap(TeamMetrics teamMetrics)
    {
        var authors = teamMetrics.PairingMatrix.Keys
            .Select(k => k.Author)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        var reviewers = teamMetrics.PairingMatrix.Keys
            .Select(k => k.Reviewer)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

        var z = new List<IEnumerable<int>>();
        foreach (var author in authors)
        {
            var row = new List<int>();
            foreach (var reviewer in reviewers)
            {
                var key = new ReviewerAuthorPair(author, reviewer);
                row.Add(teamMetrics.PairingMatrix.TryGetValue(key, out var count) ? count : 0);
            }
            z.Add(row);
        }

        return CSharpChart.Heatmap<int, string, string, string>(
                zData: z,
                X: reviewers,
                Y: authors,
                Name: "Reviews",
                ShowScale: true)
            .WithTitle("Reviewer-Author Pairing Matrix")
            .WithXAxisStyle(Title.init("Reviewer"))
            .WithYAxisStyle(Title.init("Author"));
    }
}
