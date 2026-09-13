using System.Net.Http;
using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed class UpcomingMoviesViewModel : PagedViewModel<Movie>
{
    private const int MaximumMovieCount = 100;
    private readonly UpcomingMovieRepository repository = new(DatabaseSettings.ConnectionString);
    private readonly Action<Movie> openMovie;
    private List<Movie> movies = [];
    private IReadOnlyList<string> genres = ["전체"];
    private string selectedGenre = "전체";
    private string searchQuery = "";
    private string statusMessage = "개봉 예정 영화를 불러오는 중입니다…";
    private string? statusDetails;
    private bool isStatusVisible = true;
    private bool canRetry;
    private bool loaded;

    public IReadOnlyList<string> Genres { get => genres; private set => SetProperty(ref genres, value); }
    public string SelectedGenre { get => selectedGenre; set => SetProperty(ref selectedGenre, value); }
    public string SearchQuery { get => searchQuery; set => SetProperty(ref searchQuery, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }

    public ICommand QueryCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public ICommand OpenMovieCommand { get; }

    public UpcomingMoviesViewModel(Action<Movie> openMovie)
    {
        this.openMovie = openMovie;
        QueryCommand = new RelayCommand(() => { CurrentPage = 1; ApplyFilter(); });
        RefreshCommand = new AsyncRelayCommand(LoadMoviesAsync);
        OpenMovieCommand = new RelayCommand<Movie>(movie => { if (movie is not null) this.openMovie(movie); });
    }

    public Task EnsureLoadedAsync() => loaded ? Task.CompletedTask : LoadMoviesAsync();

    private async Task LoadMoviesAsync()
    {
        ShowStatus("곧 개봉할 영화를 찾고 있어요…");
        try
        {
            DateTime fromDate = DateTime.Today;
            DateTime toDate = DateTime.Today.AddMonths(6);
            UpcomingMovieCacheResult cache = await repository.GetAsync(fromDate, toDate);
            if (cache.SnapshotDate == DateTime.Today && cache.Movies.Count > 0)
                movies = cache.Movies;
            else
            {
                var kobisClient = new KobisApiClient(MovieViewModelHelpers.RequireSecret("KOBIS_API_KEY"));
                IReadOnlyList<KobisCatalogMovie> upcoming = await kobisClient.GetUpcomingMoviesAsync(
                    fromDate, toDate, MaximumMovieCount);
                movies = await EnrichWithTmdbAsync(upcoming);
                await repository.ReplaceSnapshotAsync(DateTime.Today, movies);
            }

            loaded = true;
            Genres = ["전체", .. movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name)];
            SelectedGenre = "전체";
            ApplyFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus("개봉 예정 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
        catch (TaskCanceledException)
        {
            ShowStatus("영화 정보 서비스의 응답이 늦어지고 있습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus("개봉 예정 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
    }

    private static async Task<List<Movie>> EnrichWithTmdbAsync(IReadOnlyList<KobisCatalogMovie> source)
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
                string? year = kobis.ReleaseDate.Length >= 4 ? kobis.ReleaseDate[..4] : kobis.ProductionYear;
                return Merge(kobis, await client.FindMovieAsync(kobis.Title, kobis.OriginalTitle, year));
            }
            catch { return Merge(kobis, null); }
            finally { limiter.Release(); }
        }));
        return results.OrderBy(movie => movie.ReleaseDate).ToList();
    }

    private static Movie Merge(KobisCatalogMovie kobis, Movie? tmdb)
    {
        List<string> kobisGenres = kobis.Genres.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return new Movie
        {
            TmdbId = tmdb?.TmdbId ?? 0,
            KobisMovieCode = kobis.MovieCode,
            Title = kobis.Title,
            OriginalTitle = tmdb?.OriginalTitle ?? kobis.OriginalTitle,
            Genres = tmdb?.GenreNames.Count > 0 ? tmdb.Genres : kobisGenres.Count > 0 ? string.Join(" · ", kobisGenres) : "장르 정보 없음",
            GenreNames = tmdb?.GenreNames.Count > 0 ? tmdb.GenreNames : kobisGenres,
            Overview = tmdb?.Overview ?? "등록된 줄거리 정보가 없습니다.",
            ReleaseDate = MovieViewModelHelpers.FormatDate(kobis.ReleaseDate),
            VoteAverage = tmdb?.VoteAverage ?? 0,
            VoteCount = tmdb?.VoteCount ?? 0,
            PosterUrl = tmdb?.PosterUrl
        };
    }

    private void ApplyFilter()
    {
        SetPage(movies.Where(movie => MovieViewModelHelpers.Matches(movie, SearchQuery.Trim(), SelectedGenre)));
        IsStatusVisible = TotalItemCount == 0;
        StatusMessage = "검색 결과가 없어요. 다른 검색어나 장르를 선택해 보세요.";
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
