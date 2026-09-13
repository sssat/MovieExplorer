using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class WeeklyBoxOfficeSyncRepository(string connectionString)
{
    public async Task<HashSet<DateTime>> GetStoredWeekEndDatesAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT WeekEndDate
            FROM dbo.PastMovieRankings
            WHERE WeekEndDate BETWEEN @FromDate AND @ToDate
            GROUP BY WeekEndDate
            HAVING COUNT(*) >= 10;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.Date;
        command.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.Date;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var dates = new HashSet<DateTime>();
        while (await reader.ReadAsync(cancellationToken))
            dates.Add(reader.GetDateTime(0).Date);

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
                AudienceContextLabel =
                    $"최고 {bestRank}위 · 기간 {periodAudience:N0}명 · 누적 {cumulativeAudience:N0}명"
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

                    bool inserted = await UpsertWeeklyResultAsync(
                        connection, transaction, movieId, week.WeekEndDate, weeklyMovie, cancellationToken);
                    if (inserted)
                        insertedCount++;
                    else
                        updatedCount++;
                }
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

    private static async Task<bool> UpsertWeeklyResultAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long movieId,
        DateTime weekEndDate,
        KobisWeeklyBoxOfficeMovie movie,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.PastMovieRankings WHERE WeekEndDate = @WeekEndDate AND MovieId = @MovieId)
            BEGIN
                UPDATE dbo.PastMovieRankings
                SET Rank = @Rank,
                    WeeklyAudience = @WeeklyAudience,
                    CumulativeAudience = @CumulativeAudience,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE WeekEndDate = @WeekEndDate AND MovieId = @MovieId;
                SELECT CAST(0 AS bit);
            END
            ELSE
            BEGIN
                INSERT dbo.PastMovieRankings
                    (MovieId, WeekEndDate, Rank, WeeklyAudience, CumulativeAudience)
                VALUES
                    (@MovieId, @WeekEndDate, @Rank, @WeeklyAudience, @CumulativeAudience);
                SELECT CAST(1 AS bit);
            END
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@MovieId", SqlDbType.BigInt).Value = movieId;
        command.Parameters.Add("@WeekEndDate", SqlDbType.Date).Value = weekEndDate.Date;
        command.Parameters.Add("@Rank", SqlDbType.TinyInt).Value = movie.Rank;
        command.Parameters.Add("@WeeklyAudience", SqlDbType.BigInt).Value = movie.WeeklyAudience;
        command.Parameters.Add("@CumulativeAudience", SqlDbType.BigInt).Value = movie.CumulativeAudience;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static object ParseReleaseDate(string value) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date.Date
            : DBNull.Value;
}
