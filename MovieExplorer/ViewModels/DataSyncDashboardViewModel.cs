using System.Collections.ObjectModel;
using System.Windows.Input;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.ViewModels;

public sealed class SyncLogSectionViewModel : ObservableObject
{
    private const int PageSize = 10;
    private readonly Func<int, Task> changePage;
    private int currentPage = 1;
    private int totalCount;

    public string Title { get; }
    public string Description { get; }
    public ObservableCollection<ApiSyncLogItem> Logs { get; } = [];
    public int CurrentPage { get => currentPage; set => SetProperty(ref currentPage, value); }
    public int TotalCount { get => totalCount; set { if (SetProperty(ref totalCount, value)) OnPropertyChanged(nameof(CountLabel)); } }
    public int ItemsPerPage => PageSize;
    public string CountLabel => $"총 {TotalCount:N0}건";
    public bool IsEmpty => Logs.Count == 0;
    public ICommand ChangePageCommand { get; }

    public SyncLogSectionViewModel(string title, string description, Func<int, Task> changePage)
    {
        Title = title;
        Description = description;
        this.changePage = changePage;
        ChangePageCommand = new AsyncRelayCommand<int>(async page =>
        {
            CurrentPage = page;
            await this.changePage(page);
        });
    }

    public void SetLogs(IEnumerable<ApiSyncLogItem> values, int total)
    {
        Logs.Clear();
        foreach (ApiSyncLogItem item in values) Logs.Add(item);
        TotalCount = total;
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class DataSyncDashboardViewModel : ObservableObject
{
    private readonly DataSyncDashboardRepository repository = new(DatabaseSettings.ConnectionString);
    private bool loaded;
    private bool isLoading;
    private bool isStatusVisible = true;
    private bool isDashboardVisible;
    private bool canRetry;
    private string statusMessage = "영화 정보 현황을 확인하고 있어요…";
    private string? statusDetails;
    private string refreshedAtLabel = "";
    private string movieCountLabel = "";
    private string missingPosterCountLabel = "";
    private string missingTmdbCountLabel = "";

    public ObservableCollection<DataSetStatus> DataSets { get; } = [];
    public SyncLogSectionViewModel BoxOfficeLogs { get; }
    public SyncLogSectionViewModel UpcomingLogs { get; }
    public SyncLogSectionViewModel PastLogs { get; }
    public bool IsLoading { get => isLoading; private set => SetProperty(ref isLoading, value); }
    public bool IsStatusVisible { get => isStatusVisible; private set => SetProperty(ref isStatusVisible, value); }
    public bool IsDashboardVisible { get => isDashboardVisible; private set => SetProperty(ref isDashboardVisible, value); }
    public bool CanRetry { get => canRetry; private set => SetProperty(ref canRetry, value); }
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public string? StatusDetails { get => statusDetails; private set => SetProperty(ref statusDetails, value); }
    public string RefreshedAtLabel { get => refreshedAtLabel; private set => SetProperty(ref refreshedAtLabel, value); }
    public string MovieCountLabel { get => movieCountLabel; private set => SetProperty(ref movieCountLabel, value); }
    public string MissingPosterCountLabel { get => missingPosterCountLabel; private set => SetProperty(ref missingPosterCountLabel, value); }
    public string MissingTmdbCountLabel { get => missingTmdbCountLabel; private set => SetProperty(ref missingTmdbCountLabel, value); }
    public AsyncRelayCommand RefreshCommand { get; }

    public DataSyncDashboardViewModel()
    {
        BoxOfficeLogs = new("박스오피스", "선택한 날짜의 박스오피스 영화 정보 업데이트 내역", _ => LoadAsync());
        UpcomingLogs = new("개봉 예정", "앞으로 6개월 내 개봉 예정 영화 업데이트 내역", _ => LoadAsync());
        PastLogs = new("지난 영화", "선택한 기간의 지난 영화 업데이트 내역", _ => LoadAsync());
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public Task EnsureLoadedAsync() => loaded ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        StatusMessage = "영화 정보 현황을 확인하고 있어요…";
        StatusDetails = null;
        IsStatusVisible = true;
        CanRetry = false;
        IsDashboardVisible = false;
        IsLoading = true;
        try
        {
            DataSyncDashboard dashboard = await repository.GetAsync(
                BoxOfficeLogs.CurrentPage, UpcomingLogs.CurrentPage, PastLogs.CurrentPage, 10);
            RefreshedAtLabel = dashboard.RefreshedAtLabel;
            MovieCountLabel = dashboard.MovieCountLabel;
            MissingPosterCountLabel = dashboard.MissingPosterCountLabel;
            MissingTmdbCountLabel = dashboard.MissingTmdbCountLabel;
            DataSets.Clear();
            foreach (DataSetStatus row in dashboard.DataSets) DataSets.Add(row);
            BoxOfficeLogs.SetLogs(dashboard.BoxOfficeLogs, dashboard.BoxOfficeLogCount);
            UpcomingLogs.SetLogs(dashboard.UpcomingLogs, dashboard.UpcomingLogCount);
            PastLogs.SetLogs(dashboard.PastLogs, dashboard.PastLogCount);
            loaded = true;
            IsStatusVisible = false;
            IsDashboardVisible = true;
        }
        catch (Exception exception)
        {
            StatusMessage = "영화 정보 현황을 확인하지 못했어요.\n잠시 후 다시 시도해 주세요.";
            StatusDetails = exception.ToString();
            CanRetry = true;
        }
        finally { IsLoading = false; }
    }
}
