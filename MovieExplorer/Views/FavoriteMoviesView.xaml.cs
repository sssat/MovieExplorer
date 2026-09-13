using System.Windows.Controls;

namespace MovieExplorer.Views;

public partial class FavoriteMoviesView : UserControl
{
    public FavoriteMoviesView()
    {
        InitializeComponent();
        Pagination.PageChanged += (_, _) => MovieScrollViewer.ScrollToTop();
    }
}
