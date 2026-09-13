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
    public string PeakWeekLabel => WeeklyTrend.Count == 0
        ? "-"
        : WeeklyTrend
            .OrderByDescending(point => point.WeeklyAudience)
            .ThenBy(point => point.WeekEndDate)
            .First()
            .WeekEndDate
            .ToString("yyyy.MM.dd");
    public string TopTenDurationLabel => WeeklyTrend.Count == 0 ? "-" : $"{TrackedWeeks}주";
    public string LatestAudienceChangeLabel => WeeklyTrend.Count == 0
        ? "-"
        : WeeklyTrend[^1].WeeklyChangeLabel;
    public string AudienceRetentionLabel
    {
        get
        {
            if (WeeklyTrend.Count == 0 || WeeklyTrend[0].WeeklyAudience <= 0)
                return "-";

            double retention = WeeklyTrend[^1].WeeklyAudience
                / (double)WeeklyTrend[0].WeeklyAudience * 100;
            return $"{retention:N1}%";
        }
    }
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
    public long? PreviousWeeklyAudience { get; init; }
    public DateTime? PreviousWeekEndDate { get; init; }
    public string WeekLabel => WeekEndDate.ToString("yyyy.MM.dd");
    public string RankLabel => $"{Rank}위";
    public string WeeklyAudienceLabel => $"{WeeklyAudience:N0}명";
    public string CumulativeAudienceLabel => $"{CumulativeAudience:N0}명";
    public double? WeeklyChangeRate
    {
        get
        {
            if (PreviousWeeklyAudience is null || PreviousWeeklyAudience == 0)
                return null;
            if (PreviousWeekEndDate is not null
                && (WeekEndDate - PreviousWeekEndDate.Value).TotalDays > 8)
                return null;

            return (WeeklyAudience - PreviousWeeklyAudience.Value)
                / (double)PreviousWeeklyAudience.Value * 100;
        }
    }
    public string WeeklyChangeLabel
    {
        get
        {
            if (PreviousWeeklyAudience is null)
                return "첫 집계";
            if (PreviousWeekEndDate is not null
                && (WeekEndDate - PreviousWeekEndDate.Value).TotalDays > 8)
                return "TOP 10 재진입";
            if (PreviousWeeklyAudience == 0)
                return WeeklyAudience == 0 ? "-" : "신규 진입";

            double changeRate = WeeklyChangeRate ?? 0;
            return changeRate switch
            {
                > 0 => $"▲ {changeRate:N1}%",
                < 0 => $"▼ {Math.Abs(changeRate):N1}%",
                _ => "- 0.0%"
            };
        }
    }
}
