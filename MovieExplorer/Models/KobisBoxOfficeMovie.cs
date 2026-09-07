namespace MovieExplorer.Models;

public sealed class KobisBoxOfficeMovie
{
    public string MovieCode { get; init; } = "";
    public string Title { get; init; } = "";
    public int Rank { get; init; }
    public string ReleaseDate { get; init; } = "";
    public long DailyAudience { get; init; }
    public long CumulativeAudience { get; init; }
}

public sealed record KobisBoxOfficeResult(DateTime ShowDate, IReadOnlyList<KobisBoxOfficeMovie> Movies);
