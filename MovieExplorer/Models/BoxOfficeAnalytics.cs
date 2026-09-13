namespace MovieExplorer.Models;

public sealed class AnalyticsMovieOption
{
    public long MovieId { get; init; }
    public string Title { get; init; } = "";
    public long PeriodAudience { get; init; }
    public string DisplayLabel => $"{Title} · {PeriodAudience:N0}명";
}

public sealed class MovieAnalyticsResult
{
    public long MovieId { get; init; }
    public string Title { get; init; } = "";
    public List<MovieAnalyticsPoint> WeeklyTrend { get; init; } = [];
    public long PeriodAudience => WeeklyTrend.Sum(point => point.WeeklyAudience);
    public long LatestCumulativeAudience => WeeklyTrend.Count == 0 ? 0 : WeeklyTrend[^1].CumulativeAudience;
    public long PeakWeeklyAudience => WeeklyTrend.Count == 0 ? 0 : WeeklyTrend.Max(point => point.WeeklyAudience);
    public int BestRank => WeeklyTrend.Count == 0 ? 0 : WeeklyTrend.Min(point => point.Rank);
    public double AverageRank => WeeklyTrend.Count == 0 ? 0 : WeeklyTrend.Average(point => point.Rank);
    public int TrackedWeeks => WeeklyTrend.Count;
    public string RankChangeLabel
    {
        get
        {
            if (WeeklyTrend.Count == 0)
                return "-";

            int firstRank = WeeklyTrend[0].Rank;
            int lastRank = WeeklyTrend[^1].Rank;
            int change = firstRank - lastRank;
            return change switch
            {
                > 0 => $"{firstRank}위 → {lastRank}위 (▲ {change})",
                < 0 => $"{firstRank}위 → {lastRank}위 (▼ {Math.Abs(change)})",
                _ => $"{firstRank}위 → {lastRank}위 (-)"
            };
        }
    }
}

public sealed class MovieAnalyticsPoint
{
    public DateTime WeekEndDate { get; init; }
    public int Rank { get; init; }
    public long WeeklyAudience { get; init; }
    public long CumulativeAudience { get; init; }
    public string WeekLabel => WeekEndDate.ToString("yyyy.MM.dd");
    public string RankLabel => $"{Rank}위";
    public string WeeklyAudienceLabel => $"{WeeklyAudience:N0}명";
    public string CumulativeAudienceLabel => $"{CumulativeAudience:N0}명";
}
