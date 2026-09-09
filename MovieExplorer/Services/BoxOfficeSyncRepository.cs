using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class BoxOfficeSyncRepository(string connectionString)
{
    public async Task<BoxOfficeSyncResult> SyncAsync(
        DateTime showDate,
        IReadOnlyList<Movie> movies,
        CancellationToken cancellationToken = default)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        long syncLogId = await StartLogAsync(showDate, movies.Count, cancellationToken);
        int insertedCount = 0;
        int updatedCount = 0;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlTransaction transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (Movie movie in movies)
            {
                long movieId = await UpsertMovieAsync(connection, transaction, movie, cancellationToken);
                bool inserted = await UpsertDailyResultAsync(
                    connection, transaction, movieId, showDate, movie, cancellationToken);

                if (inserted)
                    insertedCount++;
                else
                    updatedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            await CompleteLogAsync(syncLogId, "Success", insertedCount, updatedCount, null, cancellationToken);
            return new BoxOfficeSyncResult(movies.Count, insertedCount, updatedCount);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await CompleteLogAsync(
                syncLogId, "Failed", insertedCount, updatedCount, exception.Message, CancellationToken.None);
            throw;
        }
    }

    private async Task<long> StartLogAsync(
        DateTime showDate, int receivedCount, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ApiSyncLogs (SourceName, TargetDate, Status, ReceivedCount)
            OUTPUT INSERTED.ApiSyncLogId
            VALUES (N'KOBIS+TMDB', @TargetDate, 'Running', @ReceivedCount);
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@TargetDate", SqlDbType.Date).Value = showDate.Date;
        command.Parameters.Add("@ReceivedCount", SqlDbType.Int).Value = receivedCount;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> UpsertMovieAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Movie movie,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;

            DECLARE @MovieId bigint;
            SELECT @MovieId = MovieId
            FROM dbo.Movies WITH (UPDLOCK, HOLDLOCK)
            WHERE KobisMovieCode = @KobisMovieCode;

            IF @MovieId IS NULL
            BEGIN
                INSERT dbo.Movies
                    (KobisMovieCode, TmdbId, Title, OriginalTitle, ReleaseDate, PosterUrl,
                     Genres, Overview, VoteAverage, VoteCount)
                VALUES
                    (@KobisMovieCode, NULLIF(@TmdbId, 0), @Title,
                     NULLIF(@OriginalTitle, ''), @ReleaseDate, @PosterUrl,
                     @Genres, @Overview, @VoteAverage, @VoteCount);
                SET @MovieId = SCOPE_IDENTITY();
            END
            ELSE
            BEGIN
                UPDATE dbo.Movies
                SET TmdbId = COALESCE(NULLIF(@TmdbId, 0), TmdbId),
                    Title = @Title,
                    OriginalTitle = NULLIF(@OriginalTitle, ''),
                    ReleaseDate = @ReleaseDate,
                    PosterUrl = @PosterUrl,
                    Genres = @Genres,
                    Overview = @Overview,
                    VoteAverage = @VoteAverage,
                    VoteCount = @VoteCount,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE MovieId = @MovieId;
            END;

            SELECT @MovieId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        AddMovieParameters(command, movie);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<bool> UpsertDailyResultAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long movieId,
        DateTime showDate,
        Movie movie,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM dbo.BoxOfficeDaily WITH (UPDLOCK, HOLDLOCK)
                WHERE ShowDate = @ShowDate AND MovieId = @MovieId
            )
            BEGIN
                UPDATE dbo.BoxOfficeDaily
                SET Rank = @Rank,
                    DailyAudience = @DailyAudience,
                    CumulativeAudience = @CumulativeAudience,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ShowDate = @ShowDate AND MovieId = @MovieId;
                SELECT CAST(0 AS bit);
            END
            ELSE
            BEGIN
                INSERT dbo.BoxOfficeDaily
                    (MovieId, ShowDate, Rank, DailyAudience, CumulativeAudience)
                VALUES
                    (@MovieId, @ShowDate, @Rank, @DailyAudience, @CumulativeAudience);
                SELECT CAST(1 AS bit);
            END;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@MovieId", SqlDbType.BigInt).Value = movieId;
        command.Parameters.Add("@ShowDate", SqlDbType.Date).Value = showDate.Date;
        command.Parameters.Add("@Rank", SqlDbType.TinyInt).Value = movie.Rank;
        command.Parameters.Add("@DailyAudience", SqlDbType.BigInt).Value = movie.DailyAudience;
        command.Parameters.Add("@CumulativeAudience", SqlDbType.BigInt).Value = movie.CumulativeAudience;
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task CompleteLogAsync(
        long syncLogId,
        string status,
        int insertedCount,
        int updatedCount,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.ApiSyncLogs
            SET CompletedAt = SYSUTCDATETIME(),
                Status = @Status,
                InsertedCount = @InsertedCount,
                UpdatedCount = @UpdatedCount,
                ErrorMessage = @ErrorMessage
            WHERE ApiSyncLogId = @ApiSyncLogId;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@ApiSyncLogId", SqlDbType.BigInt).Value = syncLogId;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 10).Value = status;
        command.Parameters.Add("@InsertedCount", SqlDbType.Int).Value = insertedCount;
        command.Parameters.Add("@UpdatedCount", SqlDbType.Int).Value = updatedCount;
        command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2000).Value =
            errorMessage is null ? DBNull.Value : errorMessage[..Math.Min(errorMessage.Length, 2000)];
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMovieParameters(SqlCommand command, Movie movie)
    {
        command.Parameters.Add("@KobisMovieCode", SqlDbType.NVarChar, 20).Value = movie.KobisMovieCode;
        command.Parameters.Add("@TmdbId", SqlDbType.Int).Value = movie.TmdbId;
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = movie.Title;
        command.Parameters.Add("@OriginalTitle", SqlDbType.NVarChar, 200).Value = movie.OriginalTitle;
        command.Parameters.Add("@ReleaseDate", SqlDbType.Date).Value = ParseReleaseDate(movie.ReleaseDate);
        command.Parameters.Add("@PosterUrl", SqlDbType.NVarChar, 500).Value =
            movie.PosterUrl is null ? DBNull.Value : movie.PosterUrl;
        command.Parameters.Add("@Genres", SqlDbType.NVarChar, 300).Value = movie.Genres;
        command.Parameters.Add("@Overview", SqlDbType.NVarChar, -1).Value = movie.Overview;
        command.Parameters.Add("@VoteAverage", SqlDbType.Decimal).Value = movie.VoteAverage;
        command.Parameters["@VoteAverage"].Precision = 3;
        command.Parameters["@VoteAverage"].Scale = 1;
        command.Parameters.Add("@VoteCount", SqlDbType.Int).Value = movie.VoteCount;
    }

    private static object ParseReleaseDate(string value)
    {
        string normalized = value.Replace('.', '-');
        return DateTime.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out DateTime date)
            ? date.Date
            : DBNull.Value;
    }
}
