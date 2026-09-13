using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.Views;

public partial class PastMoviesView : UserControl
{
    private const int PageSize = 10;
    private const int MaximumTmdbEnrichmentCount = 100;
    private const int RecentRefreshDays = 28;
    private readonly WeeklyBoxOfficeSyncRepository syncRepository =
        new(DatabaseSettings.ConnectionString);
    private List<Movie> movies = [];
    private bool loaded;
    private int currentPage = 1;
    private DateTime? loadedFromDate;
    private DateTime? loadedToDate;

    public event EventHandler<Movie>? MovieSelected;

    public PastMoviesView()
    {
        InitializeComponent();
        DateTime yesterday = DateTime.Today.AddDays(-1);
        FromDatePicker.DisplayDateEnd = yesterday;
        ToDatePicker.DisplayDateEnd = yesterday;
        FromDatePicker.SelectedDate = DateTime.Today.AddYears(-1);
        ToDatePicker.SelectedDate = yesterday;
        GenreBox.ItemsSource = new[] { "전체" };
        GenreBox.SelectedIndex = 0;
        SortBox.ItemsSource = new[]
        {
            "누적 관객 많은순",
            "기간 관객 많은순",
            "개봉일 최신순",
            "개봉일 오래된순",
            "평점 높은순",
            "제목순"
        };
        SortBox.SelectedIndex = 0;
        Pagination.PageChanged += (_, page) =>
        {
            currentPage = page;
            ApplyFilter();
            MovieScrollViewer.ScrollToTop();
        };
    }

    public async Task EnsureLoadedAsync()
    {
        if (!loaded)
            await LoadMoviesAsync();
    }

    private async void RefreshMovies(object sender, RoutedEventArgs e)
    {
        currentPage = 1;
        if (!TryGetSelectedDateRange(out DateTime fromDate, out DateTime toDate))
            return;

        if (loaded && loadedFromDate == fromDate && loadedToDate == toDate)
        {
            ApplyFilter();
            MovieScrollViewer.ScrollToTop();
            return;
        }

        await LoadMoviesAsync(fromDate, toDate);
    }

    private async Task LoadMoviesAsync()
    {
        if (!TryGetSelectedDateRange(out DateTime fromDate, out DateTime toDate))
            return;

        await LoadMoviesAsync(fromDate, toDate);
    }

    private async Task LoadMoviesAsync(DateTime fromDate, DateTime toDate)
    {
        ShowStatus("선택한 기간의 영화를 찾고 있어요…");
        try
        {
            List<DateTime> expectedWeekDates = KobisApiClient.GetWeekEndDates(fromDate, toDate);
            HashSet<DateTime> storedWeekDates = await syncRepository
                .GetStoredWeekEndDatesAsync(fromDate, toDate);
            DateTime recentRefreshCutoff = DateTime.Today.AddDays(-RecentRefreshDays);
            List<DateTime> missingWeekDates = expectedWeekDates
                .Where(date => !storedWeekDates.Contains(date.Date) || date >= recentRefreshCutoff)
                .ToList();

            if (missingWeekDates.Count > 0)
            {
                ShowStatus(
                    "영화 정보를 확인하고 있어요…\n" +
                    "선택한 기간이 길면 조금 더 걸릴 수 있습니다.");
                string kobisKey = RequireSecret("KOBIS_API_KEY", "KOBIS 인증키");
                KobisHistoricalBoxOfficeResult fetched = await new KobisApiClient(kobisKey)
                    .GetHistoricalBoxOfficeAsync(missingWeekDates, MaximumTmdbEnrichmentCount);
                HashSet<string> storedMovieCodes = await syncRepository.GetStoredMovieCodesAsync();
                List<KobisHistoricalMovie> newMovies = fetched.Movies
                    .Where(movie => !storedMovieCodes.Contains(movie.MovieCode))
                    .ToList();
                List<Movie> enrichedMovies = await EnrichWithTmdbAsync(newMovies);
                await syncRepository.SyncAsync(fetched.Weeks, enrichedMovies);
            }

            movies = await syncRepository.GetMoviesAsync(fromDate, toDate);
            loaded = true;
            loadedFromDate = fromDate;
            loadedToDate = toDate;
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
            ShowStatus("지난 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
        }
        catch (TaskCanceledException)
        {
            ShowStatus("영화 정보 서비스의 응답이 늦어지고 있습니다. 잠시 후 다시 시도해 주세요.", true);
        }
        catch (Exception exception)
        {
            ShowStatus("지난 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
        }
    }

    private bool TryGetSelectedDateRange(out DateTime fromDate, out DateTime toDate)
    {
        fromDate = (FromDatePicker.SelectedDate ?? DateTime.Today.AddYears(-1)).Date;
        toDate = (ToDatePicker.SelectedDate ?? DateTime.Today.AddDays(-1)).Date;

        if (toDate >= DateTime.Today)
        {
            ShowStatus("지난 영화의 종료일은 오늘보다 이전 날짜여야 합니다.");
            return false;
        }

        if (fromDate > toDate)
        {
            ShowStatus("시작일은 종료일보다 늦을 수 없습니다.");
            return false;
        }

        return true;
    }

    private static async Task<List<Movie>> EnrichWithTmdbAsync(
        IReadOnlyList<KobisHistoricalMovie> kobisMovies)
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
                    : null;
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

        return enriched.OrderByDescending(movie => movie.CumulativeAudience).ToList();
    }

    private static Movie Merge(KobisHistoricalMovie kobis, Movie? tmdb)
    {
        return new Movie
        {
            TmdbId = tmdb?.TmdbId ?? 0,
            KobisMovieCode = kobis.MovieCode,
            Title = kobis.Title,
            OriginalTitle = tmdb?.OriginalTitle ?? "",
            Genres = tmdb?.Genres ?? "장르 정보 없음",
            GenreNames = tmdb?.GenreNames ?? [],
            Overview = tmdb?.Overview ?? "등록된 줄거리 정보가 없습니다.",
            ReleaseDate = FormatKobisDate(kobis.ReleaseDate),
            VoteAverage = tmdb?.VoteAverage ?? 0,
            VoteCount = tmdb?.VoteCount ?? 0,
            PosterUrl = tmdb?.PosterUrl,
            Rank = kobis.BestRank,
            DailyAudience = kobis.PeriodAudience,
            CumulativeAudience = kobis.CumulativeAudience,
            AudienceContextLabel =
                $"최고 {kobis.BestRank}위 · 기간 {kobis.PeriodAudience:N0}명 · 누적 {kobis.CumulativeAudience:N0}명"
        };
    }

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
            "기간 관객 많은순" => filteredQuery.OrderByDescending(movie => movie.DailyAudience),
            "개봉일 최신순" => filteredQuery
                .OrderByDescending(movie => ParseReleaseDateForSort(movie.ReleaseDate) ?? DateTime.MinValue),
            "개봉일 오래된순" => filteredQuery
                .OrderBy(movie => ParseReleaseDateForSort(movie.ReleaseDate) ?? DateTime.MaxValue),
            "평점 높은순" => filteredQuery.OrderByDescending(movie => movie.VoteAverage),
            "제목순" => filteredQuery.OrderBy(movie => movie.Title),
            _ => filteredQuery.OrderByDescending(movie => movie.CumulativeAudience)
        };

        List<Movie> filtered = filteredQuery.ToList();
        int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        MovieCards.ItemsSource = filtered.Skip((currentPage - 1) * PageSize).Take(PageSize).ToList();
        ResultLabel.Text = $"총 {filtered.Count}편 · {currentPage}/{totalPages} 페이지";
        Pagination.SetState(currentPage, filtered.Count, PageSize);
        StatusPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMessage.Text = movies.Count == 0
            ? "선택한 기간에 조회된 지난 영화가 없습니다."
            : "검색 결과가 없습니다.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private static DateTime? ParseReleaseDateForSort(string value) =>
        DateTime.TryParseExact(value, "yyyy.MM.dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date
            : null;

    private void ShowMovie(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Movie movie })
            MovieSelected?.Invoke(this, movie);
    }

    private void ShowStatus(string message, bool canRetry = false, string? details = null)
    {
        MovieCards.ItemsSource = null;
        ResultLabel.Text = "";
        StatusMessage.Text = message;
        StatusMessage.ToolTip = details;
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = canRetry ? Visibility.Visible : Visibility.Collapsed;
        Pagination.SetState(1, 0, PageSize);
    }
}
