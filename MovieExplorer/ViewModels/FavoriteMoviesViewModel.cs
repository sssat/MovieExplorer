using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed class FavoriteMoviesViewModel : PagedViewModel<FavoriteMovie>
{
    private readonly MovieJournalRepository repository = new(DatabaseSettings.ConnectionString);
    private readonly Action<Movie> openMovie;
    private List<FavoriteMovie> favorites = [];
    private string searchQuery = "";
    private string selectedRating = "전체 평점";
    private string selectedSort = "최근 저장순";
    private string statusMessage = "관심 영화를 불러오는 중입니다…";
    private string? statusDetails;
    private bool isStatusVisible = true;
    private bool canRetry;

    public IReadOnlyList<string> RatingOptions { get; } =
        ["전체 평점", "5점", "4점", "3점", "2점", "1점", "평점 없음"];
    public IReadOnlyList<string> SortOptions { get; } = ["최근 저장순", "평점 높은순", "제목순"];
    public string SearchQuery { get => searchQuery; set => SetProperty(ref searchQuery, value); }
    public string SelectedRating { get => selectedRating; set => SetProperty(ref selectedRating, value); }
    public string SelectedSort { get => selectedSort; set => SetProperty(ref selectedSort, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }

    public ICommand QueryCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public ICommand OpenMovieCommand { get; }

    public FavoriteMoviesViewModel(Action<Movie> openMovie)
    {
        this.openMovie = openMovie;
        QueryCommand = new RelayCommand(() => { CurrentPage = 1; ApplyFilter(); });
        RefreshCommand = new AsyncRelayCommand(ReloadAsync);
        OpenMovieCommand = new RelayCommand<Movie>(movie => { if (movie is not null) this.openMovie(movie); });
    }

    public async Task ReloadAsync()
    {
        ShowStatus("관심 영화를 불러오는 중입니다…");
        try
        {
            favorites = (await repository.GetFavoritesAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception exception)
        {
            ShowStatus("관심 영화를 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
    }

    private void ApplyFilter()
    {
        string query = SearchQuery.Trim();
        IEnumerable<FavoriteMovie> filtered = favorites.Where(item =>
            item.Movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Movie.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Review.Contains(query, StringComparison.OrdinalIgnoreCase));

        filtered = SelectedRating switch
        {
            "평점 없음" => filtered.Where(item => item.PersonalRating is null),
            "5점" => filtered.Where(item => item.PersonalRating == 5),
            "4점" => filtered.Where(item => item.PersonalRating == 4),
            "3점" => filtered.Where(item => item.PersonalRating == 3),
            "2점" => filtered.Where(item => item.PersonalRating == 2),
            "1점" => filtered.Where(item => item.PersonalRating == 1),
            _ => filtered
        };
        filtered = SelectedSort switch
        {
            "평점 높은순" => filtered.OrderByDescending(item => item.PersonalRating ?? 0).ThenByDescending(item => item.UpdatedAt),
            "제목순" => filtered.OrderBy(item => item.Movie.Title),
            _ => filtered.OrderByDescending(item => item.UpdatedAt)
        };

        SetPage(filtered);
        IsStatusVisible = TotalItemCount == 0;
        StatusMessage = favorites.Count == 0
            ? "아직 관심 영화가 없습니다.\n영화 상세 화면에서 관심 영화로 저장해 보세요."
            : "검색 결과가 없습니다.";
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
