using System.Windows;
using System.Windows.Controls;
using MovieExplorer.Models;

namespace MovieExplorer.Views;

public partial class SyncLogSection : UserControl
{
    public event EventHandler<int>? PageChanged;

    public SyncLogSection()
    {
        InitializeComponent();
        Pagination.PageChanged += (_, page) => PageChanged?.Invoke(this, page);
    }

    public void SetState(
        string title,
        string description,
        IReadOnlyList<ApiSyncLogItem> logs,
        int currentPage,
        int totalCount,
        int pageSize)
    {
        TitleLabel.Text = title;
        DescriptionLabel.Text = description;
        CountLabel.Text = $"총 {totalCount:N0}건";
        LogRows.ItemsSource = logs;
        EmptyLabel.Visibility = logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Pagination.SetState(currentPage, totalCount, pageSize);
    }
}
