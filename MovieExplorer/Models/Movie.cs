namespace MovieExplorer.Models;

public sealed class Movie
{
    public int TmdbId { get; init; }
    public string Title { get; init; } = "";
    public string OriginalTitle { get; init; } = "";
    public string Genres { get; init; } = "장르 정보 없음";
    public IReadOnlyList<string> GenreNames { get; init; } = [];
    public string Overview { get; init; } = "등록된 줄거리가 없습니다.";
    public string ReleaseDate { get; init; } = "개봉일 미정";
    public double VoteAverage { get; init; }
    public int VoteCount { get; init; }
    public string? PosterUrl { get; init; }
    public string KobisMovieCode { get; init; } = "";
    public int Rank { get; init; }
    public long DailyAudience { get; init; }
    public long CumulativeAudience { get; init; }
    public string? AudienceContextLabel { get; init; }

    public string Metadata => $"{ReleaseDate} · ★ {VoteAverage:0.0} ({VoteCount:N0}명) · {Genres}";
    public string BoxOfficeLabel => !string.IsNullOrWhiteSpace(AudienceContextLabel)
        ? AudienceContextLabel
        : Rank > 0
        ? $"박스오피스 {Rank}위 · 일일 {DailyAudience:N0}명 · 누적 {CumulativeAudience:N0}명"
        : "";
}
