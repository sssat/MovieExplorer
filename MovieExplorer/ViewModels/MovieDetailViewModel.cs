using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed record RatingOption(int? Value, string Label)
{
    public static IReadOnlyList<RatingOption> All { get; } =
    [
        new(null, "평점 선택 안 함"), new(1, "★ 1점"), new(2, "★★ 2점"),
        new(3, "★★★ 3점"), new(4, "★★★★ 4점"), new(5, "★★★★★ 5점")
    ];
}

public sealed class MovieDetailViewModel : ObservableObject
{
    private readonly MovieJournalRepository repository = new(DatabaseSettings.ConnectionString);
    private readonly Action goBack;
    private Movie? movie;
    private bool isFavorite;
    private RatingOption selectedRating = RatingOption.All[0];
    private string review = "";
    private string statusMessage = "";
    private string? statusDetails;
    private bool isError;
    private bool isSaving;

    public Movie? Movie { get => movie; private set => SetProperty(ref movie, value); }
    public bool IsFavorite { get => isFavorite; set => SetProperty(ref isFavorite, value); }
    public IReadOnlyList<RatingOption> RatingOptions => RatingOption.All;
    public RatingOption SelectedRating { get => selectedRating; set => SetProperty(ref selectedRating, value); }
    public string Review { get => review; set => SetProperty(ref review, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public bool IsError { get => isError; private set => SetProperty(ref isError, value); }
    public bool IsSaving { get => isSaving; private set { if (SetProperty(ref isSaving, value)) SaveCommand.NotifyCanExecuteChanged(); } }

    public ICommand BackCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }

    public MovieDetailViewModel(Action goBack)
    {
        this.goBack = goBack;
        BackCommand = new RelayCommand(this.goBack);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => Movie is not null && !IsSaving);
    }

    public async Task ShowMovieAsync(Movie value)
    {
        Movie = value;
        SaveCommand.NotifyCanExecuteChanged();
        IsFavorite = false;
        SelectedRating = RatingOption.All[0];
        Review = "";
        SetStatus("저장된 기록을 불러오는 중입니다…");
        IsSaving = true;
        try
        {
            MovieJournal journal = await repository.GetAsync(value);
            IsFavorite = journal.IsFavorite;
            SelectedRating = RatingOption.All.First(option => option.Value == journal.PersonalRating);
            Review = journal.Review;
            SetStatus(journal.UpdatedAt is null
                ? "아직 저장된 기록이 없습니다."
                : $"마지막 저장: {journal.UpdatedAt.Value.ToLocalTime():yyyy.MM.dd HH:mm}");
        }
        catch (Exception exception)
        {
            SetStatus("나의 영화 기록을 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
        finally { IsSaving = false; }
    }

    private async Task SaveAsync()
    {
        if (Movie is null) return;
        IsSaving = true;
        SetStatus("저장하는 중입니다…");
        try
        {
            await repository.SaveAsync(Movie, new MovieJournal
            {
                IsFavorite = IsFavorite,
                PersonalRating = SelectedRating.Value,
                Review = Review
            });
            SetStatus($"저장되었습니다. ({DateTime.Now:yyyy.MM.dd HH:mm})");
        }
        catch (Exception exception)
        {
            SetStatus("기록을 저장하지 못했어요. 잠시 후 다시 시도해 주세요.", true, exception.ToString());
        }
        finally { IsSaving = false; }
    }

    private void SetStatus(string message, bool error = false, string? details = null)
    {
        StatusMessage = message;
        StatusDetails = details;
        IsError = error;
    }
}
