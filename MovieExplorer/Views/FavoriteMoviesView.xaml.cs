using System.Windows;
using System.Windows.Controls;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.Views;

public partial class FavoriteMoviesView : UserControl
{
    private const int PageSize = 10;
    private readonly MovieJournalRepository journalRepository =
        new(DatabaseSettings.ConnectionString);
    private List<FavoriteMovie> favorites = [];
    private int currentPage = 1;

    public event EventHandler<Movie>? MovieSelected;

    public FavoriteMoviesView()
    {
        InitializeComponent();
        RatingBox.ItemsSource = new[] { "전체 평점", "5점", "4점", "3점", "2점", "1점", "평점 없음" };
        RatingBox.SelectedIndex = 0;
        SortBox.ItemsSource = new[] { "최근 저장순", "평점 높은순", "제목순" };
        SortBox.SelectedIndex = 0;
        Pagination.PageChanged += (_, page) =>
        {
            currentPage = page;
            ApplyFilter();
            MovieScrollViewer.ScrollToTop();
        };
    }

    public async Task ReloadAsync()
    {
        ShowStatus("관심 영화를 불러오는 중입니다…");

        try
        {
            favorites = (await journalRepository.GetFavoritesAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception exception)
        {
            ShowStatus($"관심 영화를 불러오지 못했습니다.\n{exception.Message}", true);
        }
    }

    private async void RefreshFavorites(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        currentPage = 1;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (MovieCards is null || SearchBox is null || StatusPanel is null)
            return;

        string query = SearchBox.Text.Trim();
        string rating = RatingBox.SelectedItem as string ?? "전체 평점";
        IEnumerable<FavoriteMovie> filteredQuery = favorites.Where(item =>
                item.Movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Movie.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Review.Contains(query, StringComparison.OrdinalIgnoreCase));

        filteredQuery = rating switch
        {
            "평점 없음" => filteredQuery.Where(item => item.PersonalRating is null),
            "5점" => filteredQuery.Where(item => item.PersonalRating == 5),
            "4점" => filteredQuery.Where(item => item.PersonalRating == 4),
            "3점" => filteredQuery.Where(item => item.PersonalRating == 3),
            "2점" => filteredQuery.Where(item => item.PersonalRating == 2),
            "1점" => filteredQuery.Where(item => item.PersonalRating == 1),
            _ => filteredQuery
        };

        filteredQuery = (SortBox.SelectedItem as string) switch
        {
            "평점 높은순" => filteredQuery
                .OrderByDescending(item => item.PersonalRating ?? 0)
                .ThenByDescending(item => item.UpdatedAt),
            "제목순" => filteredQuery.OrderBy(item => item.Movie.Title),
            _ => filteredQuery.OrderByDescending(item => item.UpdatedAt)
        };

        List<FavoriteMovie> filtered = filteredQuery.ToList();
        int totalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        currentPage = Math.Clamp(currentPage, 1, totalPages);

        MovieCards.ItemsSource = filtered.Skip((currentPage - 1) * PageSize).Take(PageSize).ToList();
        ResultLabel.Text = $"총 {filtered.Count}편 · {currentPage}/{totalPages} 페이지";
        Pagination.SetState(currentPage, filtered.Count, PageSize);
        StatusPanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusMessage.Text = favorites.Count == 0
            ? "아직 관심 영화가 없습니다.\n영화 상세 화면에서 관심 영화로 저장해 보세요."
            : "검색 결과가 없습니다.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ResetFilter(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        RatingBox.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
        currentPage = 1;
    }

    private void ShowMovie(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Movie movie })
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
