using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class KobisApiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://www.kobis.or.kr/kobisopenapi/webservice/rest/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly string apiKey;

    public KobisApiClient(string apiKey) => this.apiKey = apiKey;

    public async Task<KobisBoxOfficeResult> GetLatestDailyBoxOfficeAsync(
        DateTime startingDate, CancellationToken cancellationToken = default)
    {
        for (int daysBack = 0; daysBack < 7; daysBack++)
        {
            DateTime targetDate = startingDate.Date.AddDays(-daysBack);
            var movies = await GetDailyBoxOfficeAsync(targetDate, cancellationToken);
            if (movies.Count > 0)
                return new KobisBoxOfficeResult(targetDate, movies);
        }

        throw new InvalidOperationException("선택한 날짜 이전 7일 동안 KOBIS 박스오피스 데이터가 없습니다.");
    }

    public async Task<IReadOnlyList<KobisUpcomingMovie>> GetUpcomingMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        int maximumCount = 20,
        CancellationToken cancellationToken = default)
    {
        var collected = new List<KobisUpcomingMovie>();

        for (int page = 1; page <= 10 && collected.Count < maximumCount; page++)
        {
            string startYear = fromDate.ToString("yyyy", CultureInfo.InvariantCulture);
            string endYear = toDate.ToString("yyyy", CultureInfo.InvariantCulture);
            string url = "movie/searchMovieList.json" +
                         $"?key={Uri.EscapeDataString(apiKey)}" +
                         $"&curPage={page}&itemPerPage=100" +
                         $"&openStartDt={startYear}&openEndDt={endYear}";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<KobisMovieListResponse>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            List<KobisMovieListItem> items = payload?.MovieListResult?.MovieList ?? [];
            if (items.Count == 0)
                break;

            collected.AddRange(items
                .Where(item => item.ProductionStatus == "개봉예정"
                               && item.TypeName == "장편"
                               && TryParseKobisDate(item.OpenDate, out DateTime releaseDate)
                               && releaseDate.Date >= fromDate.Date
                               && releaseDate.Date <= toDate.Date)
                .Select(item => new KobisUpcomingMovie
                {
                    MovieCode = item.MovieCode,
                    Title = item.MovieName,
                    OriginalTitle = item.EnglishMovieName,
                    ProductionYear = item.ProductionYear,
                    ReleaseDate = item.OpenDate,
                    TypeName = item.TypeName,
                    ProductionStatus = item.ProductionStatus,
                    Nation = item.RepresentativeNation,
                    Genres = item.Genres
                }));

            int totalCount = payload?.MovieListResult?.TotalCount ?? 0;
            if (page * 100 >= totalCount)
                break;
        }

        return collected
            .Where(movie => TryParseKobisDate(movie.ReleaseDate, out DateTime releaseDate)
                            && releaseDate.Date >= fromDate.Date
                            && releaseDate.Date <= toDate.Date)
            .GroupBy(movie => movie.MovieCode)
            .Select(group => group.First())
            .OrderBy(movie => movie.ReleaseDate)
            .Take(maximumCount)
            .ToList();
    }

    private async Task<IReadOnlyList<KobisBoxOfficeMovie>> GetDailyBoxOfficeAsync(
        DateTime targetDate, CancellationToken cancellationToken)
    {
        string date = targetDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string url = $"boxoffice/searchDailyBoxOfficeList.json?key={Uri.EscapeDataString(apiKey)}&targetDt={date}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<KobisResponse>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        return payload?.BoxOfficeResult?.DailyBoxOfficeList.Select(item => new KobisBoxOfficeMovie
        {
            MovieCode = item.MovieCode,
            Title = item.MovieName,
            Rank = ParseInt(item.Rank),
            ReleaseDate = item.OpenDate,
            DailyAudience = ParseLong(item.AudienceCount),
            CumulativeAudience = ParseLong(item.AudienceAccumulated)
        }).ToList() ?? [];
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;

    private static long ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) ? result : 0;

    private static bool TryParseKobisDate(string value, out DateTime date) =>
        DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private sealed class KobisResponse
    {
        [JsonPropertyName("boxOfficeResult")] public BoxOfficePayload? BoxOfficeResult { get; init; }
    }

    private sealed class BoxOfficePayload
    {
        [JsonPropertyName("dailyBoxOfficeList")] public List<BoxOfficeItem> DailyBoxOfficeList { get; init; } = [];
    }

    private sealed class BoxOfficeItem
    {
        [JsonPropertyName("movieCd")] public string MovieCode { get; init; } = "";
        [JsonPropertyName("movieNm")] public string MovieName { get; init; } = "";
        [JsonPropertyName("rank")] public string Rank { get; init; } = "";
        [JsonPropertyName("openDt")] public string OpenDate { get; init; } = "";
        [JsonPropertyName("audiCnt")] public string AudienceCount { get; init; } = "";
        [JsonPropertyName("audiAcc")] public string AudienceAccumulated { get; init; } = "";
    }

    private sealed class KobisMovieListResponse
    {
        [JsonPropertyName("movieListResult")] public KobisMovieListPayload? MovieListResult { get; init; }
    }

    private sealed class KobisMovieListPayload
    {
        [JsonPropertyName("totCnt")] public int TotalCount { get; init; }
        [JsonPropertyName("movieList")] public List<KobisMovieListItem> MovieList { get; init; } = [];
    }

    private sealed class KobisMovieListItem
    {
        [JsonPropertyName("movieCd")] public string MovieCode { get; init; } = "";
        [JsonPropertyName("movieNm")] public string MovieName { get; init; } = "";
        [JsonPropertyName("movieNmEn")] public string EnglishMovieName { get; init; } = "";
        [JsonPropertyName("prdtYear")] public string ProductionYear { get; init; } = "";
        [JsonPropertyName("openDt")] public string OpenDate { get; init; } = "";
        [JsonPropertyName("typeNm")] public string TypeName { get; init; } = "";
        [JsonPropertyName("prdtStatNm")] public string ProductionStatus { get; init; } = "";
        [JsonPropertyName("repNationNm")] public string RepresentativeNation { get; init; } = "";
        [JsonPropertyName("genreAlt")] public string Genres { get; init; } = "";
    }
}
