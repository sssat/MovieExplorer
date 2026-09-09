using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class MovieJournalRepository(string connectionString)
{
    public async Task<MovieJournal> GetAsync(Movie movie)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);

        const string sql = """
            SELECT j.IsFavorite, j.PersonalRating, j.Review, j.UpdatedAt
            FROM dbo.MovieJournals AS j
            INNER JOIN dbo.Movies AS m ON m.MovieId = j.MovieId
            WHERE (@KobisMovieCode <> '' AND m.KobisMovieCode = @KobisMovieCode)
               OR (@KobisMovieCode = '' AND @TmdbId > 0 AND m.TmdbId = @TmdbId);
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddMovieKeyParameters(command, movie);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return new MovieJournal();

        return new MovieJournal
        {
            IsFavorite = reader.GetBoolean(0),
            PersonalRating = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            Review = reader.IsDBNull(2) ? "" : reader.GetString(2),
            UpdatedAt = reader.GetDateTime(3)
        };
    }

    public async Task SaveAsync(Movie movie, MovieJournal journal)
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);

        const string sql = """
            SET ANSI_NULLS ON;
            SET QUOTED_IDENTIFIER ON;
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            DECLARE @MovieId bigint;

            SELECT @MovieId = MovieId
            FROM dbo.Movies WITH (UPDLOCK, HOLDLOCK)
            WHERE (@KobisMovieCode <> '' AND KobisMovieCode = @KobisMovieCode)
               OR (@KobisMovieCode = '' AND @TmdbId > 0 AND TmdbId = @TmdbId);

            IF @MovieId IS NULL
            BEGIN
                INSERT dbo.Movies
                    (KobisMovieCode, TmdbId, Title, OriginalTitle, ReleaseDate, PosterUrl)
                VALUES
                    (NULLIF(@KobisMovieCode, ''), NULLIF(@TmdbId, 0), @Title,
                     NULLIF(@OriginalTitle, ''), @ReleaseDate, @PosterUrl);

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
                    UpdatedAt = SYSUTCDATETIME()
                WHERE MovieId = @MovieId;
            END;

            IF EXISTS (SELECT 1 FROM dbo.MovieJournals WHERE MovieId = @MovieId)
                UPDATE dbo.MovieJournals
                SET IsFavorite = @IsFavorite,
                    PersonalRating = @PersonalRating,
                    Review = NULLIF(@Review, ''),
                    UpdatedAt = SYSUTCDATETIME()
                WHERE MovieId = @MovieId;
            ELSE
                INSERT dbo.MovieJournals (MovieId, IsFavorite, PersonalRating, Review)
                VALUES (@MovieId, @IsFavorite, @PersonalRating, NULLIF(@Review, ''));

            COMMIT TRANSACTION;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        AddMovieParameters(command, movie);
        command.Parameters.Add("@IsFavorite", SqlDbType.Bit).Value = journal.IsFavorite;
        command.Parameters.Add("@PersonalRating", SqlDbType.Int).Value =
            journal.PersonalRating is null ? DBNull.Value : journal.PersonalRating.Value;
        command.Parameters.Add("@Review", SqlDbType.NVarChar, 2000).Value = journal.Review.Trim();
        await command.ExecuteNonQueryAsync();
    }

    private static void AddMovieKeyParameters(SqlCommand command, Movie movie)
    {
        command.Parameters.Add("@KobisMovieCode", SqlDbType.NVarChar, 20).Value = movie.KobisMovieCode;
        command.Parameters.Add("@TmdbId", SqlDbType.Int).Value = movie.TmdbId;
    }

    private static void AddMovieParameters(SqlCommand command, Movie movie)
    {
        AddMovieKeyParameters(command, movie);
        command.Parameters.Add("@Title", SqlDbType.NVarChar, 200).Value = movie.Title;
        command.Parameters.Add("@OriginalTitle", SqlDbType.NVarChar, 200).Value = movie.OriginalTitle;
        command.Parameters.Add("@ReleaseDate", SqlDbType.Date).Value = ParseReleaseDate(movie.ReleaseDate);
        command.Parameters.Add("@PosterUrl", SqlDbType.NVarChar, 500).Value =
            movie.PosterUrl is null ? DBNull.Value : movie.PosterUrl;
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
