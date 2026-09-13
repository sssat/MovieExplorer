using System.Windows.Controls;

namespace MovieExplorer.Views;

public partial class UpcomingMoviesView : UserControl
{
    public UpcomingMoviesView()
    {
        InitializeComponent();
        Pagination.PageChanged += (_, _) => MovieScrollViewer.ScrollToTop();
    }
}
