using System.Globalization;
using MovieExplorer.Configuration;
using MovieExplorer.Models;

namespace MovieExplorer.ViewModels;

internal static class MovieViewModelHelpers
{
    public static string RequireSecret(string key)
    {
        string? value = LocalSecrets.Get(key);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                "영화 정보를 불러오기 위한 연결 설정이 필요합니다. 앱 설정을 확인해 주세요.");
    }

    public static string FormatDate(string value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date.ToString("yyyy.MM.dd")
            : value;

    public static DateTime? ParseDisplayDate(string value) =>
        DateTime.TryParseExact(value, "yyyy.MM.dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date
            : null;

    public static bool Matches(Movie movie, string query, string genre) =>
        (genre == "전체" || movie.GenreNames.Contains(genre)) &&
        (movie.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         movie.OriginalTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
         movie.Overview.Contains(query, StringComparison.OrdinalIgnoreCase));
}
