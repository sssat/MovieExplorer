using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.Views;

public partial class MovieDetailView : UserControl
{
    private readonly MovieJournalRepository journalRepository =
        new(DatabaseSettings.ConnectionString);
    private Movie? currentMovie;

    public event EventHandler? BackRequested;

    public MovieDetailView()
    {
        InitializeComponent();
        PersonalRatingBox.ItemsSource = RatingOption.All;
        PersonalRatingBox.DisplayMemberPath = nameof(RatingOption.Label);
        PersonalRatingBox.SelectedIndex = 0;
    }

    public async Task ShowMovieAsync(Movie movie)
    {
        currentMovie = movie;
        DataContext = movie;
        FavoriteCheckBox.IsChecked = false;
        PersonalRatingBox.SelectedIndex = 0;
        ReviewTextBox.Clear();
        SetStatus("저장된 기록을 불러오는 중입니다…");
        SaveJournalButton.IsEnabled = false;

        try
        {
            MovieJournal journal = await journalRepository.GetAsync(movie);
            FavoriteCheckBox.IsChecked = journal.IsFavorite;
            PersonalRatingBox.SelectedItem = RatingOption.All
                .First(option => option.Value == journal.PersonalRating);
            ReviewTextBox.Text = journal.Review;
            SetStatus(journal.UpdatedAt is null
                ? "아직 저장된 기록이 없습니다."
                : $"마지막 저장: {journal.UpdatedAt.Value.ToLocalTime():yyyy.MM.dd HH:mm}");
        }
        catch (Exception exception)
        {
            SetStatus("나의 영화 기록을 불러오지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
        }
        finally
        {
            SaveJournalButton.IsEnabled = true;
        }
    }

    private async void SaveJournal(object sender, RoutedEventArgs e)
    {
        if (currentMovie is null)
            return;

        SaveJournalButton.IsEnabled = false;
        SetStatus("저장하는 중입니다…");

        try
        {
            int? rating = (PersonalRatingBox.SelectedItem as RatingOption)?.Value;
            var journal = new MovieJournal
            {
                IsFavorite = FavoriteCheckBox.IsChecked == true,
                PersonalRating = rating,
                Review = ReviewTextBox.Text
            };

            await journalRepository.SaveAsync(currentMovie, journal);
            SetStatus($"저장되었습니다. ({DateTime.Now:yyyy.MM.dd HH:mm})");
        }
        catch (Exception exception)
        {
            SetStatus("기록을 저장하지 못했어요. 잠시 후 다시 시도해 주세요.", true,
                exception.ToString());
        }
        finally
        {
            SaveJournalButton.IsEnabled = true;
        }
    }

    private void GoBack(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string message, bool isError = false, string? details = null)
    {
        JournalStatusText.Text = message;
        JournalStatusText.ToolTip = details;
        JournalStatusText.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(255, 143, 143)
            : Color.FromRgb(163, 170, 185));
    }

    private sealed record RatingOption(int? Value, string Label)
    {
        public static IReadOnlyList<RatingOption> All { get; } =
        [
            new(null, "평점 선택 안 함"),
            new(1, "★ 1점"),
            new(2, "★★ 2점"),
            new(3, "★★★ 3점"),
            new(4, "★★★★ 4점"),
            new(5, "★★★★★ 5점")
        ];
    }
}
