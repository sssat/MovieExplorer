using System.Data;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class BoxOfficeAnalyticsRepository(string connectionString)
{
    public async Task<List<AnalyticsMovieOption>> GetMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT m.MovieId,
                   m.Title,
                   SUM(b.WeeklyAudience) AS PeriodAudience
            FROM dbo.PastMovieRankings b
            INNER JOIN dbo.Movies m ON m.MovieId = b.MovieId
            WHERE b.WeekEndDate BETWEEN @FromDate AND @ToDate
            GROUP BY m.MovieId, m.Title
            ORDER BY PeriodAudience DESC, m.Title;
            """;

        await using var command = CreateDateRangeCommand(connection, sql, fromDate, toDate);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AnalyticsMovieOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AnalyticsMovieOption
            {
                MovieId = reader.GetInt64(0),
                Title = reader.GetString(1),
                PeriodAudience = reader.GetInt64(2)
            });
        }

        return result;
    }

    public async Task<MovieAnalyticsResult?> GetMovieAsync(
        long movieId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT m.Title,
                   b.WeekEndDate,
                   CONVERT(int, b.Rank),
                   b.WeeklyAudience,
                   b.CumulativeAudience
            FROM dbo.PastMovieRankings b
            INNER JOIN dbo.Movies m ON m.MovieId = b.MovieId
            WHERE b.MovieId = @MovieId
              AND b.WeekEndDate BETWEEN @FromDate AND @ToDate
            ORDER BY b.WeekEndDate;
            """;

        await using var command = CreateDateRangeCommand(connection, sql, fromDate, toDate);
        command.Parameters.Add("@MovieId", SqlDbType.BigInt).Value = movieId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        string? title = null;
        var trend = new List<MovieAnalyticsPoint>();
        while (await reader.ReadAsync(cancellationToken))
        {
            title ??= reader.GetString(0);
            trend.Add(new MovieAnalyticsPoint
            {
                WeekEndDate = reader.GetDateTime(1),
                Rank = reader.GetInt32(2),
                WeeklyAudience = reader.GetInt64(3),
                CumulativeAudience = reader.GetInt64(4)
            });
        }

        return title is null
            ? null
            : new MovieAnalyticsResult
            {
                MovieId = movieId,
                Title = title,
                WeeklyTrend = trend
            };
    }

    private static SqlCommand CreateDateRangeCommand(
        SqlConnection connection,
        string sql,
        DateTime fromDate,
        DateTime toDate)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        return command;
    }
}
