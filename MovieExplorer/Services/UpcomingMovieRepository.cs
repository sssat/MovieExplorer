using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class UpcomingMovieRepository(string connectionString)
{
    public async Task<UpcomingMovieCacheResult> GetAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT c.SnapshotDate,
                   m.TmdbId,
                   m.KobisMovieCode,
                   m.Title,
                   m.OriginalTitle,
                   m.ReleaseDate,
                   m.PosterUrl,
                   m.Genres,
                   m.Overview,
                   m.VoteAverage,
                   m.VoteCount
            FROM dbo.UpcomingMovieCache c
            INNER JOIN dbo.Movies m ON m.MovieId = c.MovieId
            WHERE m.ReleaseDate BETWEEN @FromDate AND @ToDate
            ORDER BY m.ReleaseDate, m.Title;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        DateTime? snapshotDate = null;
        var movies = new List<Movie>();
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshotDate ??= reader.GetDateTime(0).Date;
            string genres = reader.IsDBNull(7) ? "장르 정보 없음" : reader.GetString(7);
            movies.Add(new Movie
            {
                TmdbId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                KobisMovieCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Title = reader.GetString(3),
                OriginalTitle = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ReleaseDate = reader.IsDBNull(5)
                    ? "개봉일 미정"
                    : reader.GetDateTime(5).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                PosterUrl = reader.IsDBNull(6) ? null : reader.GetString(6),
                Genres = genres,
                GenreNames = genres == "장르 정보 없음"
                    ? []
                    : genres.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Overview = reader.IsDBNull(8) ? "등록된 줄거리가 없습니다." : reader.GetString(8),
                VoteAverage = reader.IsDBNull(9) ? 0 : Convert.ToDouble(reader.GetDecimal(9)),
                VoteCount = reader.IsDBNull(10) ? 0 : reader.GetInt32(10)
            });
        }

        return new UpcomingMovieCacheResult(snapshotDate, movies);
    }

    public async Task ReplaceSnapshotAsync(
        DateTime snapshotDate,
        IReadOnlyList<Movie> movies,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        long logId = await ApiSyncLogRepository.StartAsync(
            connectionString, "개봉 예정", snapshotDate, movies.Count, cancellationToken);
        int insertedCount = 0;
        int updatedCount = 0;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingMovieCodes = new HashSet<string>(StringComparer.Ordinal);
            await using (var existingCommand = new SqlCommand(
                """
                SELECT m.KobisMovieCode
                FROM dbo.UpcomingMovieCache c
                INNER JOIN dbo.Movies m ON m.MovieId = c.MovieId
                WHERE m.KobisMovieCode IS NOT NULL;
                """, connection, transaction))
            await using (SqlDataReader reader = await existingCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                    existingMovieCodes.Add(reader.GetString(0));
            }

            await using (var clearCommand = new SqlCommand(
                "DELETE FROM dbo.UpcomingMovieCache;", connection, transaction))
            {
                await clearCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (Movie movie in movies)
            {
                await UpsertAsync(connection, transaction, snapshotDate, movie, cancellationToken);
                if (existingMovieCodes.Contains(movie.KobisMovieCode))
                    updatedCount++;
                else
                    insertedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await ApiSyncLogRepository.CompleteAsync(
                connectionString, logId, "Failed", 0, 0, exception.Message, CancellationToken.None);
            throw;
        }

        await ApiSyncLogRepository.CompleteAsync(
            connectionString, logId, "Success", insertedCount, updatedCount, null, cancellationToken);
    }

    private static async Task UpsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DateTime snapshotDate,
        Movie movie,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @MovieId bigint =
                (SELECT MovieId FROM dbo.Movies WITH (UPDLOCK, HOLDLOCK) WHERE KobisMovieCode = @KobisMovieCode);
            DECLARE @SafeTmdbId int = NULLIF(@TmdbId, 0);

            IF @SafeTmdbId IS NOT NULL
               AND EXISTS
               (
                   SELECT 1
                   FROM dbo.Movies WITH (UPDLOCK, HOLDLOCK)
                   WHERE TmdbId = @SafeTmdbId
                     AND (@MovieId IS NULL OR MovieId <> @MovieId)
               )
                SET @SafeTmdbId = NULL;

            IF @MovieId IS NULL
            BEGIN
                INSERT dbo.Movies
                    (KobisMovieCode, TmdbId, Title, OriginalTitle, ReleaseDate, PosterUrl,
                     Genres, Overview, VoteAverage, VoteCount)
                VALUES
                    (@KobisMovieCode, @SafeTmdbId, @Title, @OriginalTitle, @ReleaseDate, @PosterUrl,
                     @Genres, @Overview, @VoteAverage, @VoteCount);
                SET @MovieId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.Movies
                SET Title = @Title,
                    TmdbId = COALESCE(@SafeTmdbId, TmdbId),
                    OriginalTitle = COALESCE(@OriginalTitle, OriginalTitle),
                    ReleaseDate = COALESCE(@ReleaseDate, ReleaseDate),
                    PosterUrl = COALESCE(@PosterUrl, PosterUrl),
                    Genres = COALESCE(@Genres, Genres),
                    Overview = COALESCE(@Overview, Overview),
                    VoteAverage = COALESCE(@VoteAverage, VoteAverage),
                    VoteCount = COALESCE(@VoteCount, VoteCount),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE MovieId = @MovieId;
            END;

            INSERT dbo.UpcomingMovieCache (MovieId, SnapshotDate)
            VALUES (@MovieId, @SnapshotDate);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@KobisMovieCode", SqlDbType.NVarChar, 20).Value = movie.KobisMovieCode;
        command.Parameters.Add("@TmdbId", SqlDbType.Int).Value = movie.TmdbId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = movie.Title;
        command.Parameters.Add("@OriginalTitle", SqlDbType.NVarChar, 200).Value =
            string.IsNullOrWhiteSpace(movie.OriginalTitle) ? DBNull.Value : movie.OriginalTitle;
        command.Parameters.Add("@ReleaseDate", SqlDbType.Date).Value = ParseReleaseDate(movie.ReleaseDate);
        command.Parameters.Add("@PosterUrl", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(movie.PosterUrl) ? DBNull.Value : movie.PosterUrl;
        command.Parameters.Add("@Genres", SqlDbType.NVarChar, 300).Value =
            movie.Genres == "장르 정보 없음" ? DBNull.Value : movie.Genres;
        command.Parameters.Add("@Overview", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(movie.Overview) ? DBNull.Value : movie.Overview;
        var voteAverage = command.Parameters.Add("@VoteAverage", SqlDbType.Decimal);
        voteAverage.Precision = 3;
        voteAverage.Scale = 1;
        voteAverage.Value = movie.VoteAverage;
        command.Parameters.Add("@VoteCount", SqlDbType.Int).Value = movie.VoteCount;
        command.Parameters.Add("@SnapshotDate", SqlDbType.Date).Value = snapshotDate.Date;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ParseReleaseDate(string value) =>
        DateTime.TryParseExact(value, ["yyyy.MM.dd", "yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? date.Date
            : DBNull.Value;
}

public sealed record UpcomingMovieCacheResult(DateTime? SnapshotDate, List<Movie> Movies);
