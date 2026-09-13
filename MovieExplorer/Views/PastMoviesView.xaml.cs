using System.Windows.Controls;

namespace MovieExplorer.Views;

public partial class PastMoviesView : UserControl
{
    public PastMoviesView()
    {
        InitializeComponent();
        Pagination.PageChanged += (_, _) => MovieScrollViewer.ScrollToTop();
    }
}
