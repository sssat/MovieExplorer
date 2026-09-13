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

        throw new InvalidOperationException("선택한 날짜와 가까운 기간에 확인할 수 있는 박스오피스 정보가 없습니다.");
    }

    public Task<IReadOnlyList<KobisCatalogMovie>> GetUpcomingMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        int maximumCount = 100,
        CancellationToken cancellationToken = default) =>
        GetCatalogMoviesAsync(fromDate, toDate, "개봉예정", maximumCount, false, false, cancellationToken);

    public Task<IReadOnlyList<KobisCatalogMovie>> GetPastMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        int maximumCount = 100,
        bool newestFirst = true,
        CancellationToken cancellationToken = default) =>
        GetCatalogMoviesAsync(
            fromDate, toDate, "개봉", maximumCount, newestFirst, !newestFirst, cancellationToken);

    public async Task<KobisHistoricalBoxOfficeResult> GetHistoricalBoxOfficeAsync(
        DateTime fromDate,
        DateTime toDate,
        int maximumCount = 100,
        CancellationToken cancellationToken = default) =>
        await GetHistoricalBoxOfficeAsync(
            GetWeekEndDates(fromDate.Date, toDate.Date), maximumCount, cancellationToken);

    public async Task<KobisHistoricalBoxOfficeResult> GetHistoricalBoxOfficeAsync(
        IReadOnlyList<DateTime> weekEndDates,
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        using var requestLimiter = new SemaphoreSlim(4);
        KobisWeeklyBoxOfficeResult[] weeks = await Task.WhenAll(weekEndDates.Select(async weekEndDate =>
        {
            await requestLimiter.WaitAsync(cancellationToken);
            try
            {
                return await GetWeeklyBoxOfficeAsync(weekEndDate, cancellationToken);
            }
            finally
            {
                requestLimiter.Release();
            }
        }));

        List<KobisWeeklyBoxOfficeResult> orderedWeeks = weeks
            .OrderBy(week => week.WeekEndDate)
            .ToList();

        return BuildHistoricalBoxOfficeResult(orderedWeeks, maximumCount);
    }

    public static KobisHistoricalBoxOfficeResult BuildHistoricalBoxOfficeResult(
        IReadOnlyList<KobisWeeklyBoxOfficeResult> weeks,
        int maximumCount = 100)
    {
        List<KobisWeeklyBoxOfficeResult> orderedWeeks = weeks
            .OrderBy(week => week.WeekEndDate)
            .ToList();
        List<KobisHistoricalMovie> movies = orderedWeeks
            .SelectMany(week => week.Movies.Select(movie => new { week.WeekEndDate, Movie = movie }))
            .GroupBy(item => item.Movie.MovieCode)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.WeekEndDate).ToList();
                return new KobisHistoricalMovie
                {
                    MovieCode = group.Key,
                    Title = ordered[0].Movie.Title,
                    ReleaseDate = ordered.Select(item => item.Movie.ReleaseDate)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
                    PeriodAudience = ordered.Sum(item => item.Movie.WeeklyAudience),
                    CumulativeAudience = ordered.Max(item => item.Movie.CumulativeAudience),
                    BestRank = ordered.Min(item => item.Movie.Rank),
                    FirstRank = ordered[0].Movie.Rank,
                    LastRank = ordered[^1].Movie.Rank
                };
            })
            .OrderByDescending(movie => movie.CumulativeAudience)
            .ThenByDescending(movie => movie.PeriodAudience)
            .Take(maximumCount)
            .ToList();

        return new KobisHistoricalBoxOfficeResult { Movies = movies, Weeks = orderedWeeks };
    }

    private async Task<KobisWeeklyBoxOfficeResult> GetWeeklyBoxOfficeAsync(
        DateTime weekEndDate,
        CancellationToken cancellationToken)
    {
        string date = weekEndDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string url = "boxoffice/searchWeeklyBoxOfficeList.json" +
                     $"?key={Uri.EscapeDataString(apiKey)}&targetDt={date}&weekGb=0";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<KobisWeeklyResponse>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        return new KobisWeeklyBoxOfficeResult
        {
            WeekEndDate = weekEndDate,
            Movies = payload?.BoxOfficeResult?.WeeklyBoxOfficeList.Select(item =>
                new KobisWeeklyBoxOfficeMovie
                {
                    MovieCode = item.MovieCode,
                    Title = item.MovieName,
                    ReleaseDate = item.OpenDate,
                    Rank = ParseInt(item.Rank),
                    WeeklyAudience = ParseLong(item.AudienceCount),
                    CumulativeAudience = ParseLong(item.AudienceAccumulated)
                }).ToList() ?? []
        };
    }

    public static List<DateTime> GetWeekEndDates(DateTime fromDate, DateTime toDate)
    {
        DateTime cursor = toDate;
        while (cursor.DayOfWeek != DayOfWeek.Sunday)
            cursor = cursor.AddDays(-1);

        var dates = new List<DateTime>();
        while (cursor >= fromDate)
        {
            dates.Add(cursor);
            cursor = cursor.AddDays(-7);
        }

        if (dates.Count == 0)
            dates.Add(toDate);

        return dates;
    }

    private async Task<IReadOnlyList<KobisCatalogMovie>> GetCatalogMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        string productionStatus,
        int maximumCount,
        bool newestFirst,
        bool scanEntireRange,
        CancellationToken cancellationToken)
    {
        var collected = new List<KobisCatalogMovie>();

        const int itemCountPerRequest = 100;
        const int maximumApiPages = 50;

        for (int page = 1;
             page <= maximumApiPages && (scanEntireRange || collected.Count < maximumCount);
             page++)
        {
            string startYear = fromDate.ToString("yyyy", CultureInfo.InvariantCulture);
            string endYear = toDate.ToString("yyyy", CultureInfo.InvariantCulture);
            string url = "movie/searchMovieList.json" +
                         $"?key={Uri.EscapeDataString(apiKey)}" +
                         $"&curPage={page}&itemPerPage={itemCountPerRequest}" +
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
                .Where(item => item.ProductionStatus == productionStatus
                               && item.TypeName == "장편"
                               && TryParseKobisDate(item.OpenDate, out DateTime releaseDate)
                               && releaseDate.Date >= fromDate.Date
                               && releaseDate.Date <= toDate.Date)
                .Select(item => new KobisCatalogMovie
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
            if (page * itemCountPerRequest >= totalCount)
                break;
        }

        IEnumerable<KobisCatalogMovie> distinctMovies = collected
            .Where(movie => TryParseKobisDate(movie.ReleaseDate, out DateTime releaseDate)
                            && releaseDate.Date >= fromDate.Date
                            && releaseDate.Date <= toDate.Date)
            .GroupBy(movie => movie.MovieCode)
            .Select(group => group.First());

        distinctMovies = newestFirst
            ? distinctMovies.OrderByDescending(movie => movie.ReleaseDate)
            : distinctMovies.OrderBy(movie => movie.ReleaseDate);

        return distinctMovies
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

    private sealed class KobisWeeklyResponse
    {
        [JsonPropertyName("boxOfficeResult")] public WeeklyBoxOfficePayload? BoxOfficeResult { get; init; }
    }

    private sealed class WeeklyBoxOfficePayload
    {
        [JsonPropertyName("weeklyBoxOfficeList")]
        public List<BoxOfficeItem> WeeklyBoxOfficeList { get; init; } = [];
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
