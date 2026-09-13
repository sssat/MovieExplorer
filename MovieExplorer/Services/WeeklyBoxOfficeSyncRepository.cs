using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class WeeklyBoxOfficeSyncRepository(string connectionString)
{
    public async Task<Dictionary<DateTime, DateTime>> GetWeekSyncDatesAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT WeekEndDate, LastSyncedAt
            FROM dbo.PastMovieSyncWeeks
            WHERE WeekEndDate BETWEEN @FromDate AND @ToDate
            ORDER BY WeekEndDate;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var dates = new Dictionary<DateTime, DateTime>();
        while (await reader.ReadAsync(cancellationToken))
            dates[reader.GetDateTime(0).Date] = reader.GetDateTime(1);

        return dates;
    }

    public async Task<HashSet<string>> GetStoredMovieCodesAsync(
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT KobisMovieCode
            FROM dbo.Movies
            WHERE KobisMovieCode IS NOT NULL;
            """;
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var codes = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
            codes.Add(reader.GetString(0));

        return codes;
    }

    public async Task<List<Movie>> GetMoviesAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            WITH Aggregated AS
            (
                SELECT MovieId,
                       SUM(WeeklyAudience) AS PeriodAudience,
                       MAX(CumulativeAudience) AS CumulativeAudience,
                       MIN(CONVERT(int, Rank)) AS BestRank
                FROM dbo.PastMovieRankings
                WHERE WeekEndDate BETWEEN @FromDate AND @ToDate
                GROUP BY MovieId
            )
            SELECT
                   m.TmdbId,
                   m.KobisMovieCode,
                   m.Title,
                   m.OriginalTitle,
                   m.ReleaseDate,
                   m.PosterUrl,
                   m.Genres,
                   m.Overview,
                   m.VoteAverage,
                   m.VoteCount,
                   a.BestRank,
                   a.PeriodAudience,
                   a.CumulativeAudience
            FROM Aggregated a
            INNER JOIN dbo.Movies m ON m.MovieId = a.MovieId
            ORDER BY a.CumulativeAudience DESC, a.PeriodAudience DESC, m.Title;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        var movies = new List<Movie>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string genres = reader.IsDBNull(6) ? "장르 정보 없음" : reader.GetString(6);
            int bestRank = reader.GetInt32(10);
            long periodAudience = reader.GetInt64(11);
            long cumulativeAudience = reader.GetInt64(12);
            movies.Add(new Movie
            {
                TmdbId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                KobisMovieCode = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Title = reader.GetString(2),
                OriginalTitle = reader.IsDBNull(3) ? "" : reader.GetString(3),
                ReleaseDate = reader.IsDBNull(4)
                    ? "개봉일 미정"
                    : reader.GetDateTime(4).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                PosterUrl = reader.IsDBNull(5) ? null : reader.GetString(5),
                Genres = genres,
                GenreNames = genres == "장르 정보 없음"
                    ? []
                    : genres.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Overview = reader.IsDBNull(7)
                    ? "등록된 줄거리가 없습니다."
                    : reader.GetString(7),
                VoteAverage = reader.IsDBNull(8) ? 0 : Convert.ToDouble(reader.GetDecimal(8)),
                VoteCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                Rank = bestRank,
                DailyAudience = periodAudience,
                CumulativeAudience = cumulativeAudience,
                AudienceContextLabel = $"누적 관객 {cumulativeAudience:N0}명"
            });
        }

        return movies;
    }

    public async Task SyncAsync(
        IReadOnlyList<KobisWeeklyBoxOfficeResult> weeks,
        IReadOnlyList<Movie> enrichedMovies,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        int receivedCount = weeks.Sum(week => week.Movies.Count);
        DateTime? targetDate = weeks.Count == 0 ? null : weeks.Max(week => week.WeekEndDate);
        long logId = await ApiSyncLogRepository.StartAsync(
            connectionString, "지난 영화", targetDate, receivedCount, cancellationToken);
        int insertedCount = 0;
        int updatedCount = 0;
        var enrichedByCode = enrichedMovies
            .Where(movie => !string.IsNullOrWhiteSpace(movie.KobisMovieCode))
            .ToDictionary(movie => movie.KobisMovieCode);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var movieIds = new Dictionary<string, long>();
            foreach (KobisWeeklyBoxOfficeResult week in weeks)
            {
                foreach (KobisWeeklyBoxOfficeMovie weeklyMovie in week.Movies)
                {
                    if (!movieIds.TryGetValue(weeklyMovie.MovieCode, out long movieId))
                    {
                        enrichedByCode.TryGetValue(weeklyMovie.MovieCode, out Movie? enriched);
                        movieId = await UpsertMovieAsync(
                            connection, transaction, weeklyMovie, enriched, cancellationToken);
                        movieIds[weeklyMovie.MovieCode] = movieId;
                    }
                }
            }

            (insertedCount, updatedCount) = await UpsertWeeklyResultsAsync(
                connection, transaction, weeks, movieIds, cancellationToken);
            await UpsertSyncWeeksAsync(connection, transaction, weeks, cancellationToken);

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

    private static async Task<long> UpsertMovieAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        KobisWeeklyBoxOfficeMovie weeklyMovie,
        Movie? enriched,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DECLARE @MovieId bigint =
                (SELECT MovieId FROM dbo.Movies WITH (UPDLOCK, HOLDLOCK) WHERE KobisMovieCode = @KobisMovieCode);
            DECLARE @SafeTmdbId int = @TmdbId;

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

            SELECT @MovieId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@KobisMovieCode", SqlDbType.NVarChar, 20).Value = weeklyMovie.MovieCode;
        command.Parameters.Add("@TmdbId", SqlDbType.Int).Value =
            enriched is { TmdbId: > 0 } ? enriched.TmdbId : DBNull.Value;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = weeklyMovie.Title;
        command.Parameters.Add("@OriginalTitle", SqlDbType.NVarChar, 200).Value =
            string.IsNullOrWhiteSpace(enriched?.OriginalTitle) ? DBNull.Value : enriched.OriginalTitle;
        command.Parameters.Add("@ReleaseDate", SqlDbType.Date).Value = ParseReleaseDate(weeklyMovie.ReleaseDate);
        command.Parameters.Add("@PosterUrl", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(enriched?.PosterUrl) ? DBNull.Value : enriched.PosterUrl;
        command.Parameters.Add("@Genres", SqlDbType.NVarChar, 300).Value =
            string.IsNullOrWhiteSpace(enriched?.Genres) ? DBNull.Value : enriched.Genres;
        command.Parameters.Add("@Overview", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(enriched?.Overview) ? DBNull.Value : enriched.Overview;
        var voteAverage = command.Parameters.Add("@VoteAverage", SqlDbType.Decimal);
        voteAverage.Precision = 3;
        voteAverage.Scale = 1;
        voteAverage.Value = enriched is null ? DBNull.Value : enriched.VoteAverage;
        command.Parameters.Add("@VoteCount", SqlDbType.Int).Value =
            enriched is null ? DBNull.Value : enriched.VoteCount;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<(int InsertedCount, int UpdatedCount)> UpsertWeeklyResultsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<KobisWeeklyBoxOfficeResult> weeks,
        IReadOnlyDictionary<string, long> movieIds,
        CancellationToken cancellationToken)
    {
        const string createTempTableSql = """
            CREATE TABLE #IncomingPastMovieRankings
            (
                MovieId bigint NOT NULL,
                WeekEndDate date NOT NULL,
                Rank tinyint NOT NULL,
                WeeklyAudience bigint NOT NULL,
                CumulativeAudience bigint NOT NULL,
                PRIMARY KEY (WeekEndDate, MovieId)
            );
            """;
        await using (var createTempTable = new SqlCommand(createTempTableSql, connection, transaction))
            await createTempTable.ExecuteNonQueryAsync(cancellationToken);

        var table = new DataTable();
        table.Columns.Add("MovieId", typeof(long));
        table.Columns.Add("WeekEndDate", typeof(DateTime));
        table.Columns.Add("Rank", typeof(byte));
        table.Columns.Add("WeeklyAudience", typeof(long));
        table.Columns.Add("CumulativeAudience", typeof(long));

        foreach (KobisWeeklyBoxOfficeResult week in weeks)
        {
            foreach (KobisWeeklyBoxOfficeMovie movie in week.Movies)
            {
                table.Rows.Add(
                    movieIds[movie.MovieCode], week.WeekEndDate.Date, Convert.ToByte(movie.Rank),
                    movie.WeeklyAudience, movie.CumulativeAudience);
            }
        }

        if (table.Rows.Count > 0)
        {
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "#IncomingPastMovieRankings"
            };
            foreach (DataColumn column in table.Columns)
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }

        const string upsertSql = """
            UPDATE target
            SET Rank = source.Rank,
                WeeklyAudience = source.WeeklyAudience,
                CumulativeAudience = source.CumulativeAudience,
                UpdatedAt = SYSUTCDATETIME()
            FROM dbo.PastMovieRankings target
            INNER JOIN #IncomingPastMovieRankings source
                ON source.WeekEndDate = target.WeekEndDate
               AND source.MovieId = target.MovieId;
            DECLARE @UpdatedCount int = @@ROWCOUNT;

            INSERT dbo.PastMovieRankings
                (MovieId, WeekEndDate, Rank, WeeklyAudience, CumulativeAudience)
            SELECT source.MovieId, source.WeekEndDate, source.Rank,
                   source.WeeklyAudience, source.CumulativeAudience
            FROM #IncomingPastMovieRankings source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.PastMovieRankings target
                WHERE target.WeekEndDate = source.WeekEndDate
                  AND target.MovieId = source.MovieId
            );
            DECLARE @InsertedCount int = @@ROWCOUNT;

            SELECT @InsertedCount, @UpdatedCount;
            """;

        await using var command = new SqlCommand(upsertSql, connection, transaction);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static async Task UpsertSyncWeeksAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<KobisWeeklyBoxOfficeResult> weeks,
        CancellationToken cancellationToken)
    {
        if (weeks.Count == 0)
            return;

        const string createTempTableSql = """
            CREATE TABLE #IncomingPastMovieSyncWeeks
            (
                WeekEndDate date NOT NULL PRIMARY KEY,
                RecordCount int NOT NULL
            );
            """;
        await using (var createTempTable = new SqlCommand(createTempTableSql, connection, transaction))
            await createTempTable.ExecuteNonQueryAsync(cancellationToken);

        var table = new DataTable();
        table.Columns.Add("WeekEndDate", typeof(DateTime));
        table.Columns.Add("RecordCount", typeof(int));
        foreach (KobisWeeklyBoxOfficeResult week in weeks)
            table.Rows.Add(week.WeekEndDate.Date, week.Movies.Count);

        using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
               { DestinationTableName = "#IncomingPastMovieSyncWeeks" })
        {
            bulkCopy.ColumnMappings.Add("WeekEndDate", "WeekEndDate");
            bulkCopy.ColumnMappings.Add("RecordCount", "RecordCount");
            await bulkCopy.WriteToServerAsync(table, cancellationToken);
        }

        const string upsertSql = """
            UPDATE target
            SET RecordCount = source.RecordCount,
                LastSyncedAt = SYSUTCDATETIME()
            FROM dbo.PastMovieSyncWeeks target
            INNER JOIN #IncomingPastMovieSyncWeeks source
                ON source.WeekEndDate = target.WeekEndDate;

            INSERT dbo.PastMovieSyncWeeks (WeekEndDate, RecordCount)
            SELECT source.WeekEndDate, source.RecordCount
            FROM #IncomingPastMovieSyncWeeks source
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM dbo.PastMovieSyncWeeks target
                WHERE target.WeekEndDate = source.WeekEndDate
            );
            """;

        await using var command = new SqlCommand(upsertSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object ParseReleaseDate(string value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date.Date
            : DBNull.Value;
}
