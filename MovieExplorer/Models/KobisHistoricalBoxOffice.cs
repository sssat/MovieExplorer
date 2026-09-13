namespace MovieExplorer.Models;

public sealed class KobisHistoricalMovie
{
    public string MovieCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public long PeriodAudience { get; init; }
    public long CumulativeAudience { get; init; }
    public int BestRank { get; init; }
    public int FirstRank { get; init; }
    public int LastRank { get; init; }
}

public sealed class KobisWeeklyBoxOfficeResult
{
    public DateTime WeekEndDate { get; init; }
    public List<KobisWeeklyBoxOfficeMovie> Movies { get; init; } = [];
}

public sealed class KobisWeeklyBoxOfficeMovie
{
    public string MovieCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public int Rank { get; init; }
    public long WeeklyAudience { get; init; }
    public long CumulativeAudience { get; init; }
}

public sealed class KobisHistoricalBoxOfficeResult
{
    public List<KobisHistoricalMovie> Movies { get; init; } = [];
    public List<KobisWeeklyBoxOfficeResult> Weeks { get; init; } = [];
}
