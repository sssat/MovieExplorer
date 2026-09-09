using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class TmdbApiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.themoviedb.org/3/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly IReadOnlyDictionary<int, string> GenreMap = new Dictionary<int, string>
    {
        [28] = "액션", [12] = "모험", [16] = "애니메이션", [35] = "코미디",
        [80] = "범죄", [99] = "다큐멘터리", [18] = "드라마", [10751] = "가족",
        [14] = "판타지", [36] = "역사", [27] = "공포", [10402] = "음악",
        [9648] = "미스터리", [10749] = "로맨스", [878] = "SF", [10770] = "TV 영화",
        [53] = "스릴러", [10752] = "전쟁", [37] = "서부"
    };

    private readonly string accessToken;

    public TmdbApiClient(string accessToken) => this.accessToken = accessToken;

    public async Task<IReadOnlyList<Movie>> GetNowPlayingAsync(CancellationToken cancellationToken = default)
        => await GetMovieListAsync("movie/now_playing?language=ko-KR&region=KR&page=1", cancellationToken);

    private async Task<IReadOnlyList<Movie>> GetMovieListAsync(
        string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TmdbMovieListResponse>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        return payload?.Results.Select(MapMovie).ToList() ?? [];
    }

    public async Task<Movie?> FindMovieAsync(
        string title, string? releaseYear, CancellationToken cancellationToken = default)
    {
        string yearQuery = string.IsNullOrWhiteSpace(releaseYear) ? "" : $"&year={Uri.EscapeDataString(releaseYear)}";
        string url = $"search/movie?language=ko-KR&region=KR&query={Uri.EscapeDataString(title)}{yearQuery}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TmdbMovieListResponse>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        return payload?.Results.Count > 0 ? MapMovie(payload.Results[0]) : null;
    }

    private static Movie MapMovie(TmdbMovie dto)
    {
        var genreNames = dto.GenreIds.Where(GenreMap.ContainsKey).Select(id => GenreMap[id]).ToList();
        return new Movie
        {
            TmdbId = dto.Id,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? dto.OriginalTitle : dto.Title,
            OriginalTitle = dto.OriginalTitle,
            GenreNames = genreNames,
            Genres = genreNames.Count == 0 ? "장르 정보 없음" : string.Join(" · ", genreNames),
            Overview = string.IsNullOrWhiteSpace(dto.Overview) ? "등록된 줄거리가 없습니다." : dto.Overview,
            ReleaseDate = DateTime.TryParse(dto.ReleaseDate, out var date) ? date.ToString("yyyy.MM.dd") : "개봉일 미정",
            VoteAverage = dto.VoteAverage,
            VoteCount = dto.VoteCount,
            PosterUrl = string.IsNullOrWhiteSpace(dto.PosterPath) ? null : $"https://image.tmdb.org/t/p/w500{dto.PosterPath}"
        };
    }

    private sealed class TmdbMovieListResponse
    {
        public List<TmdbMovie> Results { get; init; } = [];
    }

    private sealed class TmdbMovie
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        [JsonPropertyName("original_title")] public string OriginalTitle { get; init; } = "";
        public string Overview { get; init; } = "";
        [JsonPropertyName("release_date")] public string ReleaseDate { get; init; } = "";
        [JsonPropertyName("poster_path")] public string? PosterPath { get; init; }
        [JsonPropertyName("genre_ids")] public List<int> GenreIds { get; init; } = [];
        [JsonPropertyName("vote_average")] public double VoteAverage { get; init; }
        [JsonPropertyName("vote_count")] public int VoteCount { get; init; }
    }
}
