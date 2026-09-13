using System.Net.Http;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.Views;

public partial class UpcomingMoviesView : UserControl
{
    private const int PageSize = 10;
    private const int MaximumMovieCount = 100;
    private readonly UpcomingMovieRepository repository = new(DatabaseSettings.ConnectionString);
    public event EventHandler<Movie>? MovieSelected;

    private List<Movie> movies = [];
    private bool loaded;
    private int currentPage = 1;

    public UpcomingMoviesView()
    {
        InitializeComponent();
        GenreBox.ItemsSource = new[] { "전체" };
        GenreBox.SelectedIndex = 0;
        Pagination.PageChanged += (_, page) =>
        {
            currentPage = page;
            ApplyFilter();
            MovieScrollViewer.ScrollToTop();
        };
    }

    public async Task EnsureLoadedAsync()
    {
        if (loaded)
            return;

        await LoadMoviesAsync();
    }

    private async void RefreshMovies(object sender, RoutedEventArgs e) => await LoadMoviesAsync();

    private void QueryMovies(object sender, RoutedEventArgs e)
    {
        currentPage = 1;
        ApplyFilter();
    }

    private async Task LoadMoviesAsync()
    {
        ShowStatus("곧 개봉할 영화를 찾고 있어요…");

        try
        {
            DateTime fromDate = DateTime.Today;
            DateTime toDate = DateTime.Today.AddMonths(6);
            UpcomingMovieCacheResult cache = await repository.GetAsync(fromDate, toDate);
            if (cache.SnapshotDate == DateTime.Today && cache.Movies.Count > 0)
            {
                movies = cache.Movies;
            }
            else
            {
                string kobisKey = RequireSecret("KOBIS_API_KEY", "KOBIS 인증키");
                var kobisClient = new KobisApiClient(kobisKey);
                IReadOnlyList<KobisCatalogMovie> upcomingMovies = await kobisClient.GetUpcomingMoviesAsync(
                    fromDate,
                    toDate,
                    MaximumMovieCount);

                movies = await EnrichWithTmdbAsync(upcomingMovies);
                await repository.ReplaceSnapshotAsync(DateTime.Today, movies);
            }
            loaded = true;

            GenreBox.ItemsSource = new[] { "전체" }
                .Concat(movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name));
            GenreBox.SelectedIndex = 0;
            ApplyFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus($"개봉 예정 영화를 불러오지 못했습니다.\n{exception.Message}", true);
        }
        catch (TaskCanceledException)
        {
            ShowStatus("API 응답 시간이 초과되었습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus(exception.Message, true);
        }
    }

    private static async Task<List<Movie>> EnrichWithTmdbAsync(
        IReadOnlyList<KobisCatalogMovie> kobisMovies)
    {
        string? tmdbToken = LocalSecrets.Get("TMDB_READ_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(tmdbToken))
            return kobisMovies.Select(movie => Merge(movie, null)).ToList();

        var tmdbClient = new TmdbApiClient(tmdbToken);
        using var requestLimiter = new SemaphoreSlim(8);
        Movie[] enriched = await Task.WhenAll(kobisMovies.Select(async kobisMovie =>
        {
            await requestLimiter.WaitAsync();
            try
            {
                string? releaseYear = kobisMovie.ReleaseDate.Length >= 4
                    ? kobisMovie.ReleaseDate[..4]
                    : kobisMovie.ProductionYear;
                Movie? tmdbMovie = await tmdbClient.FindMovieAsync(kobisMovie.Title, releaseYear);
                return Merge(kobisMovie, tmdbMovie);
            }
            catch
            {
                return Merge(kobisMovie, null);
            }
            finally
            {
                requestLimiter.Release();
            }
        }));

        return enriched.OrderBy(movie => movie.ReleaseDate).ToList();
    }

    private static Movie Merge(KobisCatalogMovie kobis, Movie? tmdb)
    {
        List<string> kobisGenres = kobis.Genres
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        return new Movie
        {
            TmdbId = tmdb?.TmdbId ?? 0,
            KobisMovieCode = kobis.MovieCode,
            Title = kobis.Title,
            OriginalTitle = tmdb?.OriginalTitle ?? kobis.OriginalTitle,
            Genres = tmdb?.GenreNames.Count > 0
                ? tmdb.Genres
                : kobisGenres.Count > 0 ? string.Join(" · ", kobisGenres) : "장르 정보 없음",
            GenreNames = tmdb?.GenreNames.Count > 0 ? tmdb.GenreNames : kobisGenres,
            Overview = tmdb?.Overview ?? "TMDB에서 일치하는 영화 줄거리를 찾지 못했습니다.",
            ReleaseDate = FormatKobisDate(kobis.ReleaseDate),
            VoteAverage = tmdb?.VoteAverage ?? 0,
            VoteCount = tmdb?.VoteCount ?? 0,
            PosterUrl = tmdb?.PosterUrl
        };
    }

    private static string FormatKobisDate(string value) =>
        DateTime.TryParseExact(
            value,
            ["yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime date)
            ? date.ToString("yyyy.MM.dd")
            : value;

    private static string RequireSecret(string key, string displayName)
    {
        string? value = LocalSecrets.Get(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        throw new InvalidOperationException(
            $"{displayName}가 설정되지 않았습니다.\nMovieExplorer 프로젝트의 .env 파일에 {key}를 입력해 주세요.");
    }

    private void ApplyFilter()
    {
        if (MovieCards is null || GenreBox is null || SearchBox is null || StatusPanel is null)
            return;

        string query = SearchBox.Text.Trim();
        string genre = GenreBox.SelectedItem as string ?? "전체";
        IEnumerable<Movie> filteredQuery = movies.Where(movie =>
                (genre == "전체" || movie.GenreNames.Contains(genre))
                && (movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.Overview.Contains(query, StringComparison.OrdinalIgnoreCase)));

        List<Movie> filtered = filteredQuery.ToList();
        int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        MovieCards.ItemsSource = filtered.Skip((currentPage - 1) * PageSize).Take(PageSize).ToList();
        ResultLabel.Text = $"총 {filtered.Count}편 · {currentPage}/{totalPages} 페이지";
        Pagination.SetState(currentPage, filtered.Count, PageSize);
        StatusPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMessage.Text = "검색 결과가 없어요. 다른 검색어나 장르를 선택해 보세요.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ShowMovie(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Movie movie })
            return;

        MovieSelected?.Invoke(this, movie);
    }

    private void ShowStatus(string message, bool canRetry = false)
    {
        MovieCards.ItemsSource = null;
        ResultLabel.Text = "";
        StatusMessage.Text = message;
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        Pagination.SetState(1, 0, PageSize);
    }
}
