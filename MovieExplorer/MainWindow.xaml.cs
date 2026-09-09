using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer;

public partial class MainWindow : Window
{
    private readonly BoxOfficeSyncRepository boxOfficeSyncRepository =
        new(DatabaseSettings.ConnectionString);
    private List<Movie> movies = [];

    public MainWindow()
    {
        InitializeComponent();
        UpcomingPage.MovieSelected += ShowUpcomingMovieDetail;
        MovieDetailPage.BackRequested += ReturnFromMovieDetail;
        BoxOfficeDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
        GenreBox.ItemsSource = new[] { "전체" };
        GenreBox.SelectedIndex = 0;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadMoviesAsync();
    }

    private async void RefreshMovies(object sender, RoutedEventArgs e) => await LoadMoviesAsync();

    private void ShowBoxOfficePage(object sender, RoutedEventArgs e)
    {
        BoxOfficeHeaderPanel.Visibility = Visibility.Visible;
        BoxOfficeContentPanel.Visibility = Visibility.Visible;
        BoxOfficeFooter.Visibility = Visibility.Visible;
        UpcomingPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        SetActiveNavigation(BoxOfficeNavButton);
    }

    private async void ShowUpcomingPage(object sender, RoutedEventArgs e)
    {
        BoxOfficeHeaderPanel.Visibility = Visibility.Collapsed;
        BoxOfficeContentPanel.Visibility = Visibility.Collapsed;
        BoxOfficeFooter.Visibility = Visibility.Collapsed;
        UpcomingPage.Visibility = Visibility.Visible;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        SetActiveNavigation(UpcomingNavButton);
        await UpcomingPage.EnsureLoadedAsync();
    }

    private void SetActiveNavigation(Button activeButton)
    {
        BoxOfficeNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        UpcomingNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        activeButton.Foreground = new SolidColorBrush(Color.FromRgb(221, 246, 107));
    }

    private async Task LoadMoviesAsync()
    {
        ShowStatus("영화 정보를 불러오는 중입니다…");
        SyncStatusLabel.Text = "DB 동기화 대기…";
        SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        try
        {
            movies = await LoadKobisMoviesAsync();

            GenreBox.ItemsSource = new[] { "전체" }
                .Concat(movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name));
            GenreBox.SelectedIndex = 0;
            ApplyFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus($"영화 정보를 불러오지 못했습니다.\n{exception.Message}", true);
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

    private async Task<List<Movie>> LoadKobisMoviesAsync()
    {
        string kobisKey = RequireSecret("KOBIS_API_KEY", "KOBIS 인증키");
        DateTime requestedDate = BoxOfficeDatePicker.SelectedDate ?? DateTime.Today.AddDays(-1);
        KobisBoxOfficeResult result = await new KobisApiClient(kobisKey)
            .GetLatestDailyBoxOfficeAsync(requestedDate);
        BoxOfficeDatePicker.SelectedDate = result.ShowDate;

        string? tmdbToken = LocalSecrets.Get("TMDB_READ_ACCESS_TOKEN");
        List<Movie> enrichedMovies;
        if (string.IsNullOrWhiteSpace(tmdbToken))
        {
            enrichedMovies = result.Movies.Select(CreateKobisOnlyMovie).ToList();
        }
        else
        {
            var tmdbClient = new TmdbApiClient(tmdbToken);
            Movie[] enriched = await Task.WhenAll(result.Movies.Select(async kobisMovie =>
            {
                try
                {
                    string? releaseYear = kobisMovie.ReleaseDate.Length >= 4 ? kobisMovie.ReleaseDate[..4] : null;
                    Movie? tmdbMovie = await tmdbClient.FindMovieAsync(kobisMovie.Title, releaseYear);
                    return Merge(kobisMovie, tmdbMovie);
                }
                catch
                {
                    return CreateKobisOnlyMovie(kobisMovie);
                }
            }));
            enrichedMovies = enriched.OrderBy(movie => movie.Rank).ToList();
        }

        await SynchronizeBoxOfficeAsync(result.ShowDate, enrichedMovies);
        return enrichedMovies;
    }

    private async Task SynchronizeBoxOfficeAsync(DateTime showDate, IReadOnlyList<Movie> loadedMovies)
    {
        SyncStatusLabel.Text = "MSSQL 동기화 중…";
        SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));

        try
        {
            BoxOfficeSyncResult result = await boxOfficeSyncRepository.SyncAsync(showDate, loadedMovies);
            SyncStatusLabel.Text = $"DB 동기화 · 신규 {result.InsertedCount} / 갱신 {result.UpdatedCount}";
            SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(221, 246, 107));
        }
        catch (Exception exception)
        {
            SyncStatusLabel.Text = $"DB 동기화 실패 · {exception.Message}";
            SyncStatusLabel.ToolTip = exception.ToString();
            SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(255, 143, 143));
        }
    }

    private static Movie Merge(KobisBoxOfficeMovie kobis, Movie? tmdb)
    {
        Movie fallback = tmdb ?? CreateKobisOnlyMovie(kobis);
        return new Movie
        {
            TmdbId = fallback.TmdbId,
            Title = kobis.Title,
            OriginalTitle = fallback.OriginalTitle,
            Genres = fallback.Genres,
            GenreNames = fallback.GenreNames,
            Overview = fallback.Overview,
            ReleaseDate = FormatKobisDate(kobis.ReleaseDate),
            VoteAverage = fallback.VoteAverage,
            VoteCount = fallback.VoteCount,
            PosterUrl = fallback.PosterUrl,
            KobisMovieCode = kobis.MovieCode,
            Rank = kobis.Rank,
            DailyAudience = kobis.DailyAudience,
            CumulativeAudience = kobis.CumulativeAudience
        };
    }

    private static Movie CreateKobisOnlyMovie(KobisBoxOfficeMovie kobis) => new()
    {
        Title = kobis.Title,
        ReleaseDate = FormatKobisDate(kobis.ReleaseDate),
        Overview = "TMDB에서 일치하는 영화 상세정보를 찾지 못했습니다.",
        KobisMovieCode = kobis.MovieCode,
        Rank = kobis.Rank,
        DailyAudience = kobis.DailyAudience,
        CumulativeAudience = kobis.CumulativeAudience
    };

    private static string FormatKobisDate(string value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
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

    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (MovieCards is null || GenreBox is null || SearchBox is null || StatusPanel is null)
            return;

        string query = SearchBox.Text.Trim();
        string genre = GenreBox.SelectedItem as string ?? "전체";
        var filtered = movies.Where(movie =>
                (genre == "전체" || movie.GenreNames.Contains(genre))
                && (movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.Overview.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        MovieCards.ItemsSource = filtered;
        ResultLabel.Text = $"총 {filtered.Count}편";
        StatusPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMessage.Text = "검색 결과가 없어요. 다른 검색어나 장르를 선택해 보세요.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ResetFilters(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        GenreBox.SelectedIndex = 0;
    }

    private async void ShowMovie(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Movie movie })
            return;

        await OpenMovieDetailAsync(movie, false);
    }

    private async void ShowUpcomingMovieDetail(object? sender, Movie movie) =>
        await OpenMovieDetailAsync(movie, true);

    private bool returnToUpcomingPage;

    private async Task OpenMovieDetailAsync(Movie movie, bool fromUpcomingPage)
    {
        returnToUpcomingPage = fromUpcomingPage;
        BoxOfficeHeaderPanel.Visibility = Visibility.Collapsed;
        BoxOfficeContentPanel.Visibility = Visibility.Collapsed;
        BoxOfficeFooter.Visibility = Visibility.Collapsed;
        UpcomingPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Visible;
        await MovieDetailPage.ShowMovieAsync(movie);
    }

    private void ReturnFromMovieDetail(object? sender, EventArgs e)
    {
        MovieDetailPage.Visibility = Visibility.Collapsed;

        if (returnToUpcomingPage)
        {
            UpcomingPage.Visibility = Visibility.Visible;
            SetActiveNavigation(UpcomingNavButton);
            return;
        }

        BoxOfficeHeaderPanel.Visibility = Visibility.Visible;
        BoxOfficeContentPanel.Visibility = Visibility.Visible;
        BoxOfficeFooter.Visibility = Visibility.Visible;
        SetActiveNavigation(BoxOfficeNavButton);
    }

    private void ShowStatus(string message, bool canRetry = false)
    {
        MovieCards.ItemsSource = null;
        ResultLabel.Text = "";
        StatusMessage.Text = message;
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
    }
}
