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
        StatusMessage.Text = "영화 정보 현황을 확인하고 있어요…";
        StatusMessage.ToolTip = null;
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
            RefreshedAtLabel.Text = dashboard.RefreshedAtLabel;
            MovieCountLabel.Text = dashboard.MovieCountLabel;
            MissingPosterCountLabel.Text = dashboard.MissingPosterCountLabel;
            MissingTmdbCountLabel.Text = dashboard.MissingTmdbCountLabel;
            DataSetRows.ItemsSource = dashboard.DataSets;
            BoxOfficeLogSection.SetState(
                "박스오피스",
                "선택한 날짜의 박스오피스 영화 정보 업데이트 내역",
                dashboard.BoxOfficeLogs,
                boxOfficeLogPage,
                dashboard.BoxOfficeLogCount,
                LogPageSize);
            UpcomingLogSection.SetState(
                "개봉 예정",
                "앞으로 6개월 내 개봉 예정 영화 업데이트 내역",
                dashboard.UpcomingLogs,
                upcomingLogPage,
                dashboard.UpcomingLogCount,
                LogPageSize);
            PastLogSection.SetState(
                "지난 영화",
                "선택한 기간의 지난 영화 업데이트 내역",
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
            StatusMessage.Text = "영화 정보 현황을 확인하지 못했어요.\n잠시 후 다시 시도해 주세요.";
            StatusMessage.ToolTip = exception.ToString();
            RetryButton.Visibility = Visibility.Visible;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

}
