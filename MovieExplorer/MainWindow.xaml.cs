using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MovieExplorer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            GenreBox.ItemsSource = new[] { "전체", "SF", "로맨스", "스릴러", "코미디", "드라마", "액션" };
            GenreBox.SelectedIndex = 0;
        }

        private readonly List<Movie> movies = new()
        {
            new("별이 머무는 밤", "A NIGHT AMONG STARS", "SF", 128, "12세 이상", "사라진 별의 신호를 따라 두 탐사자가 우주 끝의 정거장으로 향한다.", "#293B66", "01"),
            new("여름의 편지", "LETTERS FROM SUMMER", "로맨스", 112, "12세 이상", "오래된 우체통에서 발견한 편지 한 통. 잊었던 여름이 다시 시작된다.", "#476C62", "02"),
            new("마지막 플랫폼", "THE LAST PLATFORM", "스릴러", 119, "15세 이상", "막차가 떠난 역에 남겨진 다섯 사람. 전광판에 낯선 목적지가 나타난다.", "#553C59", "03"),
            new("우리 동네 히어로", "THE EVERYDAY HERO", "코미디", 104, "전체 관람가", "평범한 이웃들이 동네 축제를 지키기 위해 특별한 작전을 시작한다.", "#866036", "04"),
            new("파도의 기억", "MEMORIES OF THE SEA", "드라마", 121, "전체 관람가", "고향 바다로 돌아온 사진가가 아버지의 필름 속에서 새로운 이야기를 발견한다.", "#30576B", "05"),
            new("제로 아워", "ZERO HOUR", "액션", 132, "15세 이상", "도시의 모든 시계가 멈춘 순간, 마지막 임무를 맡은 요원의 추격이 시작된다.", "#713F40", "06")
        };

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            if (MovieCards is null || GenreBox is null || SearchBox is null) return;
            string query = SearchBox.Text.Trim();
            string genre = GenreBox.SelectedItem as string ?? "전체";
            var filtered = movies.Where(movie => (genre == "전체" || movie.Genre == genre)
                && (movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.EnglishTitle.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || movie.Synopsis.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            MovieCards.ItemsSource = filtered;
            ResultLabel.Text = $"총 {filtered.Count}편";
            EmptyMessage.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetFilters(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            GenreBox.SelectedIndex = 0;
        }

        private void ShowMovie(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Movie movie })
                MessageBox.Show(this, $"{movie.Metadata}\n\n{movie.Synopsis}\n\n화면 시연용 가상 영화입니다. 실제 영화 정보는 API 연동 후 제공됩니다.", movie.Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public sealed record Movie(string Title, string EnglishTitle, string Genre, int Runtime,
        string Rating, string Synopsis, string PosterColor, string Number)
    {
        public string Metadata => $"{Rating} · {Genre} · {Runtime}분";
    }
}
