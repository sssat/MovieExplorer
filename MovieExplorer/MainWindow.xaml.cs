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
        PastPage.MovieSelected += ShowPastMovieDetail;
        FavoritesPage.MovieSelected += ShowFavoriteMovieDetail;
        MovieDetailPage.BackRequested += ReturnFromMovieDetail;
        BoxOfficeDatePicker.SelectedDate = DateTime.Today.AddDays(-1);
        GenreBox.ItemsSource = new[] { "전체" };
        GenreBox.SelectedIndex = 0;
        SortBox.ItemsSource = new[]
        {
            "박스오피스 순위",
            "일일 관객 많은순",
            "누적 관객 많은순",
            "평점 높은순",
            "개봉일 최신순"
        };
        SortBox.SelectedIndex = 0;
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
        PastPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        SetActiveNavigation(BoxOfficeNavButton);
    }

    private async void ShowAnalyticsPage(object sender, RoutedEventArgs e)
    {
        HideBoxOfficePage();
        UpcomingPage.Visibility = Visibility.Collapsed;
        PastPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Visible;
        SetActiveNavigation(AnalyticsNavButton);
        await AnalyticsPage.EnsureLoadedAsync();
    }

    private async void ShowUpcomingPage(object sender, RoutedEventArgs e)
    {
        BoxOfficeHeaderPanel.Visibility = Visibility.Collapsed;
        BoxOfficeContentPanel.Visibility = Visibility.Collapsed;
        BoxOfficeFooter.Visibility = Visibility.Collapsed;
        UpcomingPage.Visibility = Visibility.Visible;
        PastPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        SetActiveNavigation(UpcomingNavButton);
        await UpcomingPage.EnsureLoadedAsync();
    }

    private async void ShowPastPage(object sender, RoutedEventArgs e)
    {
        HideBoxOfficePage();
        UpcomingPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        PastPage.Visibility = Visibility.Visible;
        SetActiveNavigation(PastNavButton);
        await PastPage.EnsureLoadedAsync();
    }

    private async void ShowFavoritesPage(object sender, RoutedEventArgs e)
    {
        HideBoxOfficePage();
        UpcomingPage.Visibility = Visibility.Collapsed;
        PastPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Visible;
        SetActiveNavigation(FavoritesNavButton);
        await FavoritesPage.ReloadAsync();
    }

    private async void ShowDataSyncPage(object sender, RoutedEventArgs e)
    {
        HideBoxOfficePage();
        UpcomingPage.Visibility = Visibility.Collapsed;
        PastPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Visible;
        SetActiveNavigation(DataSyncNavButton);
        await DataSyncPage.EnsureLoadedAsync();
    }

    private void SetActiveNavigation(Button activeButton)
    {
        BoxOfficeNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        AnalyticsNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        UpcomingNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        PastNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        FavoritesNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        DataSyncNavButton.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        activeButton.Foreground = new SolidColorBrush(Color.FromRgb(221, 246, 107));
    }

    private async Task LoadMoviesAsync()
    {
        ShowStatus("영화 정보를 불러오는 중입니다…");
        SyncStatusLabel.Text = "영화 정보를 확인하고 있어요…";
        SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));
        try
        {
            movies = await LoadKobisMoviesAsync();

            string selectedGenre = GenreBox.SelectedItem as string ?? "전체";
            List<string> genres = new[] { "전체" }
                .Concat(movies.SelectMany(movie => movie.GenreNames).Distinct().OrderBy(name => name))
                .ToList();
            GenreBox.ItemsSource = genres;
            GenreBox.SelectedItem = genres.Contains(selectedGenre) ? selectedGenre : "전체";
            ApplyFilter();
        }
        catch (HttpRequestException exception)
        {
            ShowStatus("영화 정보를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
        }
        catch (TaskCanceledException)
        {
            ShowStatus("영화 정보 서비스의 응답이 늦어지고 있습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus("영화 정보를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
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
        SyncStatusLabel.Text = "최신 정보를 반영하고 있어요…";
        SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(163, 170, 185));

        try
        {
            BoxOfficeSyncResult result = await boxOfficeSyncRepository.SyncAsync(showDate, loadedMovies);
            SyncStatusLabel.Text = "최신 정보 반영 완료";
            SyncStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(221, 246, 107));
        }
        catch (Exception exception)
        {
            SyncStatusLabel.Text = "일부 정보를 반영하지 못했어요.";
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
        Overview = "등록된 상세 정보가 없습니다.",
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
            "영화 정보를 불러오기 위한 연결 설정이 필요합니다. 앱 설정을 확인해 주세요.");
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

        filteredQuery = (SortBox.SelectedItem as string) switch
        {
            "일일 관객 많은순" => filteredQuery.OrderByDescending(movie => movie.DailyAudience),
            "누적 관객 많은순" => filteredQuery.OrderByDescending(movie => movie.CumulativeAudience),
            "평점 높은순" => filteredQuery.OrderByDescending(movie => movie.VoteAverage),
            "개봉일 최신순" => filteredQuery.OrderByDescending(movie => movie.ReleaseDate),
            _ => filteredQuery.OrderBy(movie => movie.Rank)
        };

        List<Movie> filtered = filteredQuery.ToList();
        MovieCards.ItemsSource = filtered;
        ResultLabel.Text = $"총 {filtered.Count}편";
        StatusPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMessage.Text = "검색 결과가 없어요. 다른 검색어나 장르를 선택해 보세요.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private async void ShowMovie(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Movie movie })
            return;

        await OpenMovieDetailAsync(movie, ReturnPage.BoxOffice);
    }

    private async void ShowUpcomingMovieDetail(object? sender, Movie movie) =>
        await OpenMovieDetailAsync(movie, ReturnPage.Upcoming);

    private async void ShowPastMovieDetail(object? sender, Movie movie) =>
        await OpenMovieDetailAsync(movie, ReturnPage.Past);

    private async void ShowFavoriteMovieDetail(object? sender, Movie movie) =>
        await OpenMovieDetailAsync(movie, ReturnPage.Favorites);

    private ReturnPage returnPage;

    private async Task OpenMovieDetailAsync(Movie movie, ReturnPage destination)
    {
        returnPage = destination;
        HideBoxOfficePage();
        UpcomingPage.Visibility = Visibility.Collapsed;
        PastPage.Visibility = Visibility.Collapsed;
        FavoritesPage.Visibility = Visibility.Collapsed;
        DataSyncPage.Visibility = Visibility.Collapsed;
        AnalyticsPage.Visibility = Visibility.Collapsed;
        MovieDetailPage.Visibility = Visibility.Visible;
        await MovieDetailPage.ShowMovieAsync(movie);
    }

    private async void ReturnFromMovieDetail(object? sender, EventArgs e)
    {
        MovieDetailPage.Visibility = Visibility.Collapsed;

        if (returnPage == ReturnPage.Upcoming)
        {
            UpcomingPage.Visibility = Visibility.Visible;
            SetActiveNavigation(UpcomingNavButton);
            return;
        }

        if (returnPage == ReturnPage.Favorites)
        {
            FavoritesPage.Visibility = Visibility.Visible;
            SetActiveNavigation(FavoritesNavButton);
            await FavoritesPage.ReloadAsync();
            return;
        }

        if (returnPage == ReturnPage.Past)
        {
            PastPage.Visibility = Visibility.Visible;
            SetActiveNavigation(PastNavButton);
            return;
        }

        BoxOfficeHeaderPanel.Visibility = Visibility.Visible;
        BoxOfficeContentPanel.Visibility = Visibility.Visible;
        BoxOfficeFooter.Visibility = Visibility.Visible;
        SetActiveNavigation(BoxOfficeNavButton);
    }

    private void HideBoxOfficePage()
    {
        BoxOfficeHeaderPanel.Visibility = Visibility.Collapsed;
        BoxOfficeContentPanel.Visibility = Visibility.Collapsed;
        BoxOfficeFooter.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool canRetry = false, string? details = null)
    {
        MovieCards.ItemsSource = null;
        ResultLabel.Text = "";
        StatusMessage.Text = message;
        StatusMessage.ToolTip = details;
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
    }

    private enum ReturnPage
    {
        BoxOffice,
        Upcoming,
        Past,
        Favorites
    }
}
