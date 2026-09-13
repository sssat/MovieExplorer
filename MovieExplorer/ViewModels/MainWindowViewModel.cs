using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public enum AppPage { BoxOffice, Upcoming, Past, Favorites, Analytics, DataStatus, Detail }

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly BoxOfficeSyncRepository repository = new(DatabaseSettings.ConnectionString);
    private List<Movie> movies = [];
    private AppPage currentPage = AppPage.BoxOffice;
    private AppPage returnPage = AppPage.BoxOffice;
    private DateTime? boxOfficeDate = DateTime.Today.AddDays(-1);
    private IReadOnlyList<string> genres = ["전체"];
    private readonly IReadOnlyList<string> sortOptions =
        ["박스오피스 순위", "일일 관객 많은순", "누적 관객 많은순", "평점 높은순", "개봉일 최신순"];
    private string selectedGenre = "전체";
    private string selectedSort = "박스오피스 순위";
    private string searchQuery = "";
    private string syncStatus = "";
    private bool syncFailed;
    private string resultLabel = "";
    private string statusMessage = "영화 정보를 불러오는 중입니다…";
    private string? statusDetails;
    private bool isStatusVisible = true;
    private bool canRetry;

    public UpcomingMoviesViewModel Upcoming { get; }
    public PastMoviesViewModel Past { get; }
    public FavoriteMoviesViewModel Favorites { get; }
    public BoxOfficeAnalyticsViewModel Analytics { get; } = new();
    public DataSyncDashboardViewModel DataStatus { get; } = new();
    public MovieDetailViewModel Detail { get; }
    public ObservableCollection<Movie> BoxOfficeMovies { get; } = [];

    public AppPage CurrentPage
    {
        get => currentPage;
        private set
        {
            if (!SetProperty(ref currentPage, value)) return;
            OnPropertyChanged(nameof(IsBoxOfficePage)); OnPropertyChanged(nameof(IsUpcomingPage));
            OnPropertyChanged(nameof(IsPastPage)); OnPropertyChanged(nameof(IsFavoritesPage));
            OnPropertyChanged(nameof(IsAnalyticsPage)); OnPropertyChanged(nameof(IsDataStatusPage));
            OnPropertyChanged(nameof(IsDetailPage));
        }
    }
    public bool IsBoxOfficePage => CurrentPage == AppPage.BoxOffice;
    public bool IsUpcomingPage => CurrentPage == AppPage.Upcoming;
    public bool IsPastPage => CurrentPage == AppPage.Past;
    public bool IsFavoritesPage => CurrentPage == AppPage.Favorites;
    public bool IsAnalyticsPage => CurrentPage == AppPage.Analytics;
    public bool IsDataStatusPage => CurrentPage == AppPage.DataStatus;
    public bool IsDetailPage => CurrentPage == AppPage.Detail;
    public DateTime? BoxOfficeDate { get => boxOfficeDate; set => SetProperty(ref boxOfficeDate, value); }
    public IReadOnlyList<string> Genres { get => genres; private set => SetProperty(ref genres, value); }
    public IReadOnlyList<string> SortOptions => sortOptions;
    public string SelectedGenre { get => selectedGenre; set => SetProperty(ref selectedGenre, value); }
    public string SelectedSort { get => selectedSort; set => SetProperty(ref selectedSort, value); }
    public string SearchQuery { get => searchQuery; set => SetProperty(ref searchQuery, value); }
    public string SyncStatus { get => syncStatus; private set => SetProperty(ref syncStatus, value); }
    public bool SyncFailed { get => syncFailed; private set => SetProperty(ref syncFailed, value); }
    public string ResultLabel { get => resultLabel; private set => SetProperty(ref resultLabel, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }

    public AsyncRelayCommand QueryBoxOfficeCommand { get; }
    public ICommand OpenMovieCommand { get; }
    public ICommand NavigateCommand { get; }

    public MainWindowViewModel()
    {
        Upcoming = new(movie => _ = OpenDetailAsync(movie, AppPage.Upcoming));
        Past = new(movie => _ = OpenDetailAsync(movie, AppPage.Past));
        Favorites = new(movie => _ = OpenDetailAsync(movie, AppPage.Favorites));
        Detail = new(ReturnFromDetail);
        QueryBoxOfficeCommand = new AsyncRelayCommand(LoadBoxOfficeAsync);
        OpenMovieCommand = new RelayCommand<Movie>(movie => { if (movie is not null) _ = OpenDetailAsync(movie, AppPage.BoxOffice); });
        NavigateCommand = new AsyncRelayCommand<string>(NavigateAsync);
    }

    public Task InitializeAsync() => LoadBoxOfficeAsync();

    private async Task NavigateAsync(string? pageName)
    {
        if (!Enum.TryParse(pageName, out AppPage page)) return;
        CurrentPage = page;
        switch (page)
        {
            case AppPage.Upcoming: await Upcoming.EnsureLoadedAsync(); break;
            case AppPage.Past: await Past.EnsureLoadedAsync(); break;
            case AppPage.Favorites: await Favorites.ReloadAsync(); break;
            case AppPage.Analytics: await Analytics.EnsureLoadedAsync(); break;
            case AppPage.DataStatus: await DataStatus.EnsureLoadedAsync(); break;
        }
    }

    private async Task LoadBoxOfficeAsync()
    {
        ShowStatus("영화 정보를 불러오는 중입니다…");
        SyncStatus = "영화 정보를 확인하고 있어요…";
        SyncFailed = false;
        try
        {
            movies = await LoadKobisMoviesAsync();
            string previousGenre = SelectedGenre;
            Genres = ["전체", .. movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name)];
            SelectedGenre = Genres.Contains(previousGenre) ? previousGenre : "전체";
            ApplyBoxOfficeFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus("영화 정보를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
        catch (TaskCanceledException)
        {
            ShowStatus("영화 정보 서비스의 응답이 늦어지고 있습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus("영화 정보를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
    }

    private async Task<List<Movie>> LoadKobisMoviesAsync()
    {
        DateTime requested = BoxOfficeDate ?? DateTime.Today.AddDays(-1);
        KobisBoxOfficeResult result = await new KobisApiClient(
            MovieViewModelHelpers.RequireSecret("KOBIS_API_KEY")).GetLatestDailyBoxOfficeAsync(requested);
        BoxOfficeDate = result.ShowDate;
        string? token = LocalSecrets.Get("TMDB_READ_ACCESS_TOKEN");
        List<Movie> enriched;
        if (string.IsNullOrWhiteSpace(token))
            enriched = result.Movies.Select(CreateKobisOnlyMovie).ToList();
        else
        {
            var client = new TmdbApiClient(token);
            Movie[] values = await Task.WhenAll(result.Movies.Select(async kobis =>
            {
                try
                {
                    string? year = kobis.ReleaseDate.Length >= 4 ? kobis.ReleaseDate[..4] : null;
                    return Merge(kobis, await client.FindMovieAsync(kobis.Title, year));
                }
                catch { return CreateKobisOnlyMovie(kobis); }
            }));
            enriched = values.OrderBy(movie => movie.Rank).ToList();
        }
        await SynchronizeAsync(result.ShowDate, enriched);
        return enriched;
    }

    private async Task SynchronizeAsync(DateTime date, IReadOnlyList<Movie> loaded)
    {
        SyncStatus = "최신 정보를 반영하고 있어요…";
        try
        {
            await repository.SyncAsync(date, loaded);
            SyncStatus = "최신 정보 반영 완료";
            SyncFailed = false;
        }
        catch (Exception exception)
        {
            SyncStatus = "일부 정보를 반영하지 못했어요.";
            StatusDetails = exception.ToString();
            SyncFailed = true;
        }
    }

    private void ApplyBoxOfficeFilter()
    {
        IEnumerable<Movie> filtered = movies.Where(movie =>
            MovieViewModelHelpers.Matches(movie, SearchQuery.Trim(), SelectedGenre));
        filtered = SelectedSort switch
        {
            "일일 관객 많은순" => filtered.OrderByDescending(movie => movie.DailyAudience),
            "누적 관객 많은순" => filtered.OrderByDescending(movie => movie.CumulativeAudience),
            "평점 높은순" => filtered.OrderByDescending(movie => movie.VoteAverage),
            "개봉일 최신순" => filtered.OrderByDescending(movie => movie.ReleaseDate),
            _ => filtered.OrderBy(movie => movie.Rank)
        };
        BoxOfficeMovies.Clear();
        foreach (Movie movie in filtered) BoxOfficeMovies.Add(movie);
        ResultLabel = $"총 {BoxOfficeMovies.Count}편";
        IsStatusVisible = BoxOfficeMovies.Count == 0;
        StatusMessage = "검색 결과가 없어요. 다른 검색어나 장르를 선택해 보세요.";
        CanRetry = false;
    }

    private async Task OpenDetailAsync(Movie movie, AppPage source)
    {
        returnPage = source;
        CurrentPage = AppPage.Detail;
        await Detail.ShowMovieAsync(movie);
    }

    private async void ReturnFromDetail()
    {
        CurrentPage = returnPage;
        if (returnPage == AppPage.Favorites) await Favorites.ReloadAsync();
    }

    private void ShowStatus(string message, bool retry = false, string? details = null)
    {
        BoxOfficeMovies.Clear();
        ResultLabel = "";
        StatusMessage = message;
        StatusDetails = details;
        CanRetry = retry;
        IsStatusVisible = true;
    }

    private static Movie Merge(KobisBoxOfficeMovie kobis, Movie? tmdb)
    {
        Movie value = tmdb ?? CreateKobisOnlyMovie(kobis);
        return new Movie
        {
            TmdbId = value.TmdbId, Title = kobis.Title, OriginalTitle = value.OriginalTitle,
            Genres = value.Genres, GenreNames = value.GenreNames, Overview = value.Overview,
            ReleaseDate = MovieViewModelHelpers.FormatDate(kobis.ReleaseDate), VoteAverage = value.VoteAverage,
            VoteCount = value.VoteCount, PosterUrl = value.PosterUrl, KobisMovieCode = kobis.MovieCode,
            Rank = kobis.Rank, DailyAudience = kobis.DailyAudience, CumulativeAudience = kobis.CumulativeAudience
        };
    }

    private static Movie CreateKobisOnlyMovie(KobisBoxOfficeMovie kobis) => new()
    {
        Title = kobis.Title, ReleaseDate = MovieViewModelHelpers.FormatDate(kobis.ReleaseDate),
        Overview = "등록된 상세 정보가 없습니다.", KobisMovieCode = kobis.MovieCode,
        Rank = kobis.Rank, DailyAudience = kobis.DailyAudience, CumulativeAudience = kobis.CumulativeAudience
    };
}
