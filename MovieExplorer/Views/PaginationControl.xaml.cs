using System.Windows;
using System.Windows.Controls;

namespace MovieExplorer.Views;

public partial class PaginationControl : UserControl
{
    private int currentPage = 1;
    private int totalPages = 1;

    public event EventHandler<int>? PageChanged;

    public PaginationControl()
    {
        InitializeComponent();
        UpdateState();
    }

    public void SetState(int page, int totalItemCount, int pageSize)
    {
        totalPages = Math.Max(1, (int)Math.Ceiling(totalItemCount / (double)pageSize));
        currentPage = Math.Clamp(page, 1, totalPages);
        Visibility = totalItemCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateState();
    }

    private void GoPrevious(object sender, RoutedEventArgs e)
    {
        if (currentPage <= 1)
            return;

        PageChanged?.Invoke(this, currentPage - 1);
    }

    private void GoNext(object sender, RoutedEventArgs e)
    {
        if (currentPage >= totalPages)
            return;

        PageChanged?.Invoke(this, currentPage + 1);
    }

    private void UpdateState()
    {
        PageLabel.Text = $"{currentPage} / {totalPages} 페이지";
        PreviousButton.IsEnabled = currentPage > 1;
        NextButton.IsEnabled = currentPage < totalPages;
    }
}
