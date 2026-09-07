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
}
