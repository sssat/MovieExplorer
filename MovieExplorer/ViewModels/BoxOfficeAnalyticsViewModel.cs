using System.Collections.ObjectModel;
using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed class BoxOfficeAnalyticsViewModel : ObservableObject
{
    private readonly BoxOfficeAnalyticsRepository repository = new(DatabaseSettings.ConnectionString);
    private DateTime? fromDate = DateTime.Today.AddDays(-1).AddYears(-1);
    private DateTime? toDate = DateTime.Today.AddDays(-1);
    private AnalyticsMovieOption? selectedMovie;
    private MovieAnalyticsResult? analytics;
    private bool loaded;
    private bool isStatusVisible = true;
    private bool isDashboardVisible;
    private string statusMessage = "";
    private string? statusDetails;

    public event EventHandler? AnalyticsChanged;
    public DateTime MaximumDate => DateTime.Today.AddDays(-1);
    public DateTime? FromDate { get => fromDate; set => SetProperty(ref fromDate, value); }
    public DateTime? ToDate { get => toDate; set => SetProperty(ref toDate, value); }
    public ObservableCollection<AnalyticsMovieOption> MovieOptions { get; } = [];
    public AnalyticsMovieOption? SelectedMovie { get => selectedMovie; set => SetProperty(ref selectedMovie, value); }
    public MovieAnalyticsResult? Analytics { get => analytics; private set => SetProperty(ref analytics, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool IsDashboardVisible { get => isDashboardVisible; private set => SetProperty(ref isDashboardVisible, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public string SelectedMovieTitle => Analytics?.Title ?? "";
    public string PeriodAudienceLabel => Analytics is null ? "-" : $"{Analytics.PeriodAudience:N0}명";
    public string PeakAudienceLabel => Analytics is null ? "-" : $"{Analytics.PeakWeeklyAudience:N0}명";
    public string RankSummaryLabel => Analytics is null ? "-" : $"{Analytics.BestRank}위 · 평균 {Analytics.AverageRank:N1}위";
    public string RankChangeLabel => Analytics?.RankChangeLabel ?? "-";
    public string LatestAudienceChangeLabel => Analytics?.LatestAudienceChangeLabel ?? "-";
    public string TopTenDurationLabel => Analytics?.TopTenDurationLabel ?? "-";
    public string PeakWeekLabel => Analytics?.PeakWeekLabel ?? "-";
    public string AudienceRetentionLabel => Analytics?.AudienceRetentionLabel ?? "-";
    public string CoverageLabel => Analytics is null ? "" : $"조회 기간 {FromDate:yyyy.MM.dd}~{ToDate:yyyy.MM.dd} · 집계 {Analytics.TrackedWeeks}주";
    public IReadOnlyList<MovieAnalyticsPoint> WeeklyTrend => Analytics?.WeeklyTrend ?? [];
    public AsyncRelayCommand QueryCommand { get; }

    public BoxOfficeAnalyticsViewModel() => QueryCommand = new AsyncRelayCommand(LoadAsync);
    public Task EnsureLoadedAsync() => loaded ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        DateTime start = (FromDate ?? DateTime.Today.AddYears(-1)).Date;
        DateTime end = (ToDate ?? MaximumDate).Date;
        if (start > end) { ShowStatus("시작일은 종료일보다 늦을 수 없습니다."); return; }
        if (end >= DateTime.Today) { ShowStatus("종료일은 오늘보다 이전 날짜여야 합니다."); return; }

        ShowStatus("분석할 영화와 관객 기록을 불러오는 중입니다…");
        try
        {
            long? previousId = SelectedMovie?.MovieId;
            List<AnalyticsMovieOption> options = await repository.GetMoviesAsync(start, end);
            if (options.Count == 0)
            {
                MovieOptions.Clear();
                ShowStatus("선택한 기간에 분석할 영화가 없습니다.\n지난 영화에서 같은 기간을 먼저 조회해 주세요.");
                return;
            }
            MovieOptions.Clear();
            foreach (AnalyticsMovieOption option in options) MovieOptions.Add(option);
            SelectedMovie = options.FirstOrDefault(movie => movie.MovieId == previousId) ?? options[0];
            Analytics = await repository.GetMovieAsync(SelectedMovie.MovieId, start, end);
            loaded = true;
            if (Analytics is null || Analytics.WeeklyTrend.Count == 0)
            {
                ShowStatus("선택한 영화의 관객 기록이 없습니다.");
                return;
            }
            NotifyAnalyticsProperties();
            IsStatusVisible = false;
            IsDashboardVisible = true;
            AnalyticsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            ShowStatus("영화 분석 정보를 불러오지 못했습니다.\n잠시 후 다시 시도해 주세요.", exception.ToString());
        }
    }

    private void NotifyAnalyticsProperties()
    {
        OnPropertyChanged(nameof(SelectedMovieTitle));
        OnPropertyChanged(nameof(PeriodAudienceLabel));
        OnPropertyChanged(nameof(PeakAudienceLabel));
        OnPropertyChanged(nameof(RankSummaryLabel));
        OnPropertyChanged(nameof(RankChangeLabel));
        OnPropertyChanged(nameof(LatestAudienceChangeLabel));
        OnPropertyChanged(nameof(TopTenDurationLabel));
        OnPropertyChanged(nameof(PeakWeekLabel));
        OnPropertyChanged(nameof(AudienceRetentionLabel));
        OnPropertyChanged(nameof(CoverageLabel));
        OnPropertyChanged(nameof(WeeklyTrend));
    }

    private void ShowStatus(string message, string? details = null)
    {
        IsDashboardVisible = false;
        StatusMessage = message;
        StatusDetails = details;
        IsStatusVisible = true;
    }
}
