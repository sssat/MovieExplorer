using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    public Task<Movie?> FindMovieAsync(
        string title, string? releaseYear, CancellationToken cancellationToken = default) =>
        FindMovieAsync(title, null, releaseYear, cancellationToken);

    public async Task<Movie?> FindMovieAsync(
        string title,
        string? originalTitle,
        string? releaseYear,
        CancellationToken cancellationToken = default)
    {
        List<TmdbMovie> candidates = await SearchMoviesAsync(title, releaseYear, cancellationToken);
        TmdbMovie? match = SelectBestMatch(candidates, title, originalTitle, releaseYear);
        if (match is not null)
            return MapMovie(match);

        if (!string.IsNullOrWhiteSpace(releaseYear))
        {
            candidates = await SearchMoviesAsync(title, null, cancellationToken);
            match = SelectBestMatch(candidates, title, originalTitle, releaseYear);
            if (match is not null)
                return MapMovie(match);
        }

        if (!string.IsNullOrWhiteSpace(originalTitle)
            && NormalizeTitle(originalTitle) != NormalizeTitle(title))
        {
            candidates = await SearchMoviesAsync(originalTitle, releaseYear, cancellationToken);
            match = SelectBestMatch(candidates, title, originalTitle, releaseYear);
            if (match is not null)
                return MapMovie(match);
        }

        return null;
    }

    private async Task<List<TmdbMovie>> SearchMoviesAsync(
        string title, string? releaseYear, CancellationToken cancellationToken)
    {
        string yearQuery = string.IsNullOrWhiteSpace(releaseYear)
            ? ""
            : $"&year={Uri.EscapeDataString(releaseYear)}";
        string url = $"search/movie?language=ko-KR&region=KR&include_adult=false&query={Uri.EscapeDataString(title)}{yearQuery}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TmdbMovieListResponse>(
            stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);

        return payload?.Results ?? [];
    }

    private static TmdbMovie? SelectBestMatch(
        IReadOnlyList<TmdbMovie> candidates,
        string title,
        string? originalTitle,
        string? releaseYear)
    {
        string[] sourceTitles = [title, originalTitle ?? ""];
        int? sourceYear = int.TryParse(releaseYear, out int parsedYear) ? parsedYear : null;

        var ranked = candidates
            .Select(candidate =>
            {
                double titleSimilarity = sourceTitles
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .SelectMany(source => new[]
                    {
                        CalculateTitleSimilarity(source, candidate.Title),
                        CalculateTitleSimilarity(source, candidate.OriginalTitle)
                    })
                    .DefaultIfEmpty(0)
                    .Max();
                int? candidateYear = DateTime.TryParse(candidate.ReleaseDate, out DateTime releaseDate)
                    ? releaseDate.Year
                    : null;
                double yearScore = CalculateYearScore(sourceYear, candidateYear);
                double reliabilityScore = Math.Min(8, Math.Log10(candidate.VoteCount + 1) * 2);
                return new
                {
                    Movie = candidate,
                    TitleSimilarity = titleSimilarity,
                    Score = titleSimilarity * 100 + yearScore + reliabilityScore
                };
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Movie.VoteCount)
            .FirstOrDefault();

        return ranked is { TitleSimilarity: >= 0.72, Score: >= 80 }
            ? ranked.Movie
            : null;
    }

    private static double CalculateYearScore(int? sourceYear, int? candidateYear)
    {
        if (sourceYear is null || candidateYear is null)
            return 0;

        return Math.Abs(sourceYear.Value - candidateYear.Value) switch
        {
            0 => 25,
            1 => 15,
            2 => 8,
            _ => -10
        };
    }

    private static double CalculateTitleSimilarity(string left, string right)
    {
        string normalizedLeft = NormalizeTitle(left);
        string normalizedRight = NormalizeTitle(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
            return 0;
        if (normalizedLeft == normalizedRight)
            return 1;
        if (Math.Min(normalizedLeft.Length, normalizedRight.Length) >= 4
            && (normalizedLeft.Contains(normalizedRight) || normalizedRight.Contains(normalizedLeft)))
            return 0.88;

        int distance = CalculateEditDistance(normalizedLeft, normalizedRight);
        return 1d - distance / (double)Math.Max(normalizedLeft.Length, normalizedRight.Length);
    }

    private static string NormalizeTitle(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormKC);
        return new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static int CalculateEditDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];

        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                int substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                current[rightIndex] = Math.Min(
                    Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1),
                    previous[rightIndex - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
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
