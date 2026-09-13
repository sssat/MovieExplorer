using System.Windows;
using System.Windows.Controls;
using MovieExplorer.Configuration;
using MovieExplorer.Models;
using MovieExplorer.Services;

namespace MovieExplorer.Views;

public partial class DataSyncDashboardView : UserControl
{
    private const int LogPageSize = 10;
    private readonly DataSyncDashboardRepository repository =
        new(DatabaseSettings.ConnectionString);
    private bool loaded;
    private int boxOfficeLogPage = 1;
    private int upcomingLogPage = 1;
    private int pastLogPage = 1;

    public DataSyncDashboardView()
    {
        InitializeComponent();
        BoxOfficeLogSection.PageChanged += async (_, page) =>
        {
            boxOfficeLogPage = page;
            await LoadDashboardAsync();
        };
        UpcomingLogSection.PageChanged += async (_, page) =>
        {
            upcomingLogPage = page;
            await LoadDashboardAsync();
        };
        PastLogSection.PageChanged += async (_, page) =>
        {
            pastLogPage = page;
            await LoadDashboardAsync();
        };
    }

    public async Task EnsureLoadedAsync()
    {
        if (!loaded)
            await LoadDashboardAsync();
    }

    private async void RefreshDashboard(object sender, RoutedEventArgs e) =>
        await LoadDashboardAsync();

    private async Task LoadDashboardAsync()
    {
        StatusMessage.Text = "저장된 데이터 현황을 확인하고 있어요…";
        StatusPanel.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            DataSyncDashboard dashboard = await repository.GetAsync(
                boxOfficeLogPage,
                upcomingLogPage,
                pastLogPage,
                LogPageSize);
            ConnectionLabel.Text = dashboard.ConnectionLabel;
            RefreshedAtLabel.Text = dashboard.RefreshedAtLabel;
            MovieCountLabel.Text = dashboard.MovieCountLabel;
            FavoriteCountLabel.Text = dashboard.FavoriteCountLabel;
            MissingPosterCountLabel.Text = dashboard.MissingPosterCountLabel;
            MissingTmdbCountLabel.Text = dashboard.MissingTmdbCountLabel;
            DataSetRows.ItemsSource = dashboard.DataSets;
            BoxOfficeLogSection.SetState(
                "박스오피스",
                "기준일의 KOBIS 일별 TOP 10과 TMDB 상세정보 저장 기록",
                dashboard.BoxOfficeLogs,
                boxOfficeLogPage,
                dashboard.BoxOfficeLogCount,
                LogPageSize);
            UpcomingLogSection.SetState(
                "개봉 예정",
                "향후 6개월 내 개봉 예정 영화 캐시 갱신 기록",
                dashboard.UpcomingLogs,
                upcomingLogPage,
                dashboard.UpcomingLogCount,
                LogPageSize);
            PastLogSection.SetState(
                "지난 영화",
                "선택 기간에 조회한 지난 영화의 KOBIS 데이터 저장 기록",
                dashboard.PastLogs,
                pastLogPage,
                dashboard.PastLogCount,
                LogPageSize);

            loaded = true;
            StatusPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            StatusMessage.Text = $"데이터 현황을 확인하지 못했습니다.\n{exception.Message}";
            StatusMessage.ToolTip = exception.ToString();
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

}
