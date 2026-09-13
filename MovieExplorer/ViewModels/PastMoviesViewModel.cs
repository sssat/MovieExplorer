using System.Net.Http;
using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed class PastMoviesViewModel : PagedViewModel<Movie>
{
    private const int MaximumTmdbEnrichmentCount = 100;
    private const int RecentRefreshDays = 28;
    private static readonly TimeSpan RecentRefreshInterval = TimeSpan.FromHours(24);
    private readonly WeeklyBoxOfficeSyncRepository repository = new(DatabaseSettings.ConnectionString);
    private readonly Action<Movie> openMovie;
    private List<Movie> movies = [];
    private IReadOnlyList<string> genres = ["전체"];
    private readonly IReadOnlyList<string> sortOptions =
    [
        "누적 관객 많은순", "개봉일 최신순",
        "개봉일 오래된순", "평점 높은순", "제목순"
    ];
    private DateTime? fromDate = DateTime.Today.AddYears(-1);
    private DateTime? toDate = DateTime.Today.AddDays(-1);
    private DateTime? loadedFromDate;
    private DateTime? loadedToDate;
    private string selectedGenre = "전체";
    private string selectedSort = "누적 관객 많은순";
    private string searchQuery = "";
    private string statusMessage = "지난 영화를 불러오는 중입니다…";
    private string? statusDetails;
    private bool isStatusVisible = true;
    private bool canRetry;
    private bool loaded;

    public DateTime MaximumDate => DateTime.Today.AddDays(-1);
    public DateTime? FromDate { get => fromDate; set => SetProperty(ref fromDate, value); }
    public DateTime? ToDate { get => toDate; set => SetProperty(ref toDate, value); }
    public IReadOnlyList<string> Genres { get => genres; private set => SetProperty(ref genres, value); }
    public IReadOnlyList<string> SortOptions => sortOptions;
    public string SelectedGenre { get => selectedGenre; set => SetProperty(ref selectedGenre, value); }
    public string SelectedSort { get => selectedSort; set => SetProperty(ref selectedSort, value); }
    public string SearchQuery { get => searchQuery; set => SetProperty(ref searchQuery, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }

    public AsyncRelayCommand QueryCommand { get; }
    public ICommand OpenMovieCommand { get; }

    public PastMoviesViewModel(Action<Movie> openMovie)
    {
        this.openMovie = openMovie;
        QueryCommand = new AsyncRelayCommand(QueryAsync);
        OpenMovieCommand = new RelayCommand<Movie>(movie => { if (movie is not null) this.openMovie(movie); });
    }

    public Task EnsureLoadedAsync() => loaded ? Task.CompletedTask : QueryAsync();

    private async Task QueryAsync()
    {
        CurrentPage = 1;
        if (!TryGetRange(out DateTime start, out DateTime end))
            return;

        if (loaded && loadedFromDate == start && loadedToDate == end)
        {
            ApplyFilter();
            return;
        }

        await LoadMoviesAsync(start, end);
    }

    private async Task LoadMoviesAsync(DateTime start, DateTime end)
    {
        ShowStatus("선택한 기간의 영화를 찾고 있어요…");
        try
        {
            List<DateTime> expected = KobisApiClient.GetWeekEndDates(start, end);
            Dictionary<DateTime, DateTime> syncedAt = await repository.GetWeekSyncDatesAsync(start, end);
            DateTime refreshCutoff = DateTime.Today.AddDays(-RecentRefreshDays);
            DateTime staleBefore = DateTime.UtcNow.Subtract(RecentRefreshInterval);
            List<DateTime> missing = expected.Where(date =>
                !syncedAt.TryGetValue(date.Date, out DateTime lastSyncedAt) ||
                date >= refreshCutoff && lastSyncedAt < staleBefore).ToList();

            if (missing.Count > 0)
            {
                ShowStatus("영화 정보를 확인하고 있어요…\n선택한 기간이 길면 조금 더 걸릴 수 있습니다.");
                KobisHistoricalBoxOfficeResult fetched = await new KobisApiClient(
                    MovieViewModelHelpers.RequireSecret("KOBIS_API_KEY"))
                    .GetHistoricalBoxOfficeAsync(missing, MaximumTmdbEnrichmentCount);
                HashSet<string> storedCodes = await repository.GetStoredMovieCodesAsync();
                List<Movie> enriched = await EnrichWithTmdbAsync(
                    fetched.Movies.Where(movie => !storedCodes.Contains(movie.MovieCode)).ToList());
                await repository.SyncAsync(fetched.Weeks, enriched);
            }

            movies = await repository.GetMoviesAsync(start, end);
            loaded = true;
            loadedFromDate = start;
            loadedToDate = end;
            string previousGenre = SelectedGenre;
            Genres = ["전체", .. movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name)];
            SelectedGenre = Genres.Contains(previousGenre) ? previousGenre : "전체";
            ApplyFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus("지난 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
        catch (TaskCanceledException)
        {
            ShowStatus("영화 정보 서비스의 응답이 늦어지고 있습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus("지난 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
    }

    private bool TryGetRange(out DateTime start, out DateTime end)
    {
        start = (FromDate ?? DateTime.Today.AddYears(-1)).Date;
        end = (ToDate ?? MaximumDate).Date;
        if (end >= DateTime.Today)
        {
            ShowStatus("지난 영화의 종료일은 오늘보다 이전 날짜여야 합니다.");
            return false;
        }
        if (start > end)
        {
            ShowStatus("시작일은 종료일보다 늦을 수 없습니다.");
            return false;
        }
        return true;
    }

    private static async Task<List<Movie>> EnrichWithTmdbAsync(IReadOnlyList<KobisHistoricalMovie> source)
    {
        string? token = LocalSecrets.Get("TMDB_READ_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
            return source.Select(movie => Merge(movie, null)).ToList();

        var client = new TmdbApiClient(token);
        using var limiter = new SemaphoreSlim(8);
        Movie[] results = await Task.WhenAll(source.Select(async kobis =>
        {
            await limiter.WaitAsync();
            try
            {
                string? year = kobis.ReleaseDate.Length >= 4 ? kobis.ReleaseDate[..4] : null;
                return Merge(kobis, await client.FindMovieAsync(kobis.Title, year));
            }
            catch { return Merge(kobis, null); }
            finally { limiter.Release(); }
        }));
        return results.OrderByDescending(movie => movie.CumulativeAudience).ToList();
    }

    private static Movie Merge(KobisHistoricalMovie kobis, Movie? tmdb) => new()
    {
        TmdbId = tmdb?.TmdbId ?? 0,
        KobisMovieCode = kobis.MovieCode,
        Title = kobis.Title,
        OriginalTitle = tmdb?.OriginalTitle ?? "",
        Genres = tmdb?.Genres ?? "장르 정보 없음",
        GenreNames = tmdb?.GenreNames ?? [],
        Overview = tmdb?.Overview ?? "등록된 줄거리 정보가 없습니다.",
        ReleaseDate = MovieViewModelHelpers.FormatDate(kobis.ReleaseDate),
        VoteAverage = tmdb?.VoteAverage ?? 0,
        VoteCount = tmdb?.VoteCount ?? 0,
        PosterUrl = tmdb?.PosterUrl,
        Rank = kobis.BestRank,
        DailyAudience = kobis.PeriodAudience,
        CumulativeAudience = kobis.CumulativeAudience,
        AudienceContextLabel = $"누적 관객 {kobis.CumulativeAudience:N0}명"
    };

    private void ApplyFilter()
    {
        IEnumerable<Movie> filtered = movies.Where(movie =>
            MovieViewModelHelpers.Matches(movie, SearchQuery.Trim(), SelectedGenre));
        filtered = SelectedSort switch
        {
            "개봉일 최신순" => filtered.OrderByDescending(movie => MovieViewModelHelpers.ParseDisplayDate(movie.ReleaseDate) ?? DateTime.MinValue),
            "개봉일 오래된순" => filtered.OrderBy(movie => MovieViewModelHelpers.ParseDisplayDate(movie.ReleaseDate) ?? DateTime.MaxValue),
            "평점 높은순" => filtered.OrderByDescending(movie => movie.VoteAverage),
            "제목순" => filtered.OrderBy(movie => movie.Title),
            _ => filtered.OrderByDescending(movie => movie.CumulativeAudience)
        };
        SetPage(filtered);
        IsStatusVisible = TotalItemCount == 0;
        StatusMessage = movies.Count == 0 ? "선택한 기간에 조회된 지난 영화가 없습니다." : "검색 결과가 없습니다.";
        StatusDetails = null;
        CanRetry = false;
    }

    protected override void ApplyCurrentPage() => ApplyFilter();

    private void ShowStatus(string message, bool retry = false, string? details = null)
    {
        Items.Clear();
        TotalItemCount = 0;
        StatusMessage = message;
        StatusDetails = details;
        CanRetry = retry;
        IsStatusVisible = true;
    }
}
