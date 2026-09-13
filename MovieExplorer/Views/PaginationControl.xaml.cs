using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MovieExplorer.Views;

public partial class PaginationControl : UserControl
{
    public static readonly DependencyProperty CurrentPageProperty = DependencyProperty.Register(
        nameof(CurrentPage), typeof(int), typeof(PaginationControl),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, StateChanged));
    public static readonly DependencyProperty TotalItemCountProperty = DependencyProperty.Register(
        nameof(TotalItemCount), typeof(int), typeof(PaginationControl), new PropertyMetadata(0, StateChanged));
    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize), typeof(int), typeof(PaginationControl), new PropertyMetadata(10, StateChanged));
    public static readonly DependencyProperty PageChangedCommandProperty = DependencyProperty.Register(
        nameof(PageChangedCommand), typeof(ICommand), typeof(PaginationControl));

    public int CurrentPage { get => (int)GetValue(CurrentPageProperty); set => SetValue(CurrentPageProperty, value); }
    public int TotalItemCount { get => (int)GetValue(TotalItemCountProperty); set => SetValue(TotalItemCountProperty, value); }
    public int PageSize { get => (int)GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public ICommand? PageChangedCommand { get => (ICommand?)GetValue(PageChangedCommandProperty); set => SetValue(PageChangedCommandProperty, value); }
    private int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalItemCount / (double)Math.Max(PageSize, 1)));

    public event EventHandler<int>? PageChanged;

    public PaginationControl()
    {
        InitializeComponent();
        UpdateState();
    }

    public void SetState(int page, int totalItemCount, int pageSize)
    {
        PageSize = pageSize;
        TotalItemCount = totalItemCount;
        CurrentPage = Math.Clamp(page, 1, TotalPages);
    }

    private void GoPrevious(object sender, RoutedEventArgs e)
    {
        if (CurrentPage <= 1)
            return;
        RequestPage(CurrentPage - 1);
    }

    private void GoNext(object sender, RoutedEventArgs e)
    {
        if (CurrentPage >= TotalPages)
            return;
        RequestPage(CurrentPage + 1);
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
            PageInput.Text = CurrentPage.ToString();
            return;
        }

        int targetPage = Math.Clamp(requestedPage, 1, TotalPages);
        PageInput.Text = targetPage.ToString();
        if (targetPage != CurrentPage)
            RequestPage(targetPage);
    }

    private void RequestPage(int page)
    {
        if (PageChangedCommand?.CanExecute(page) == true)
            PageChangedCommand.Execute(page);
        PageChanged?.Invoke(this, page);
    }

    private static void StateChanged(DependencyObject source, DependencyPropertyChangedEventArgs e) =>
        ((PaginationControl)source).UpdateState();

    private void UpdateState()
    {
        if (!IsInitialized) return;
        Visibility = TotalItemCount == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageInput.Text = CurrentPage.ToString();
        PageLabel.Text = $"/ {TotalPages} 페이지";
        PreviousButton.IsEnabled = CurrentPage > 1;
        NextButton.IsEnabled = CurrentPage < TotalPages;
    }
}
