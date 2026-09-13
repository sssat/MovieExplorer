using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

    private void AllowNumbersOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = e.Text.Any(character => !char.IsDigit(character));

    private void GoToPageOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        MoveToInputPage();
        e.Handled = true;
    }

    private void GoToPage(object sender, RoutedEventArgs e) => MoveToInputPage();

    private void MoveToInputPage()
    {
        if (!int.TryParse(PageInput.Text, out int requestedPage))
        {
            PageInput.Text = currentPage.ToString();
            return;
        }

        int targetPage = Math.Clamp(requestedPage, 1, totalPages);
        PageInput.Text = targetPage.ToString();
        if (targetPage != currentPage)
            PageChanged?.Invoke(this, targetPage);
    }

    private void UpdateState()
    {
        PageInput.Text = currentPage.ToString();
        PageLabel.Text = $"/ {totalPages} 페이지";
        PreviousButton.IsEnabled = currentPage > 1;
        NextButton.IsEnabled = currentPage < totalPages;
    }
}
