using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class MovieJournalRepository(string connectionString)
{
    public async Task<IReadOnlyList<FavoriteMovie>> GetFavoritesAsync()
    {
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);

        const string sql = """
            SELECT m.TmdbId, m.Title, m.OriginalTitle, m.ReleaseDate, m.PosterUrl,
                   m.KobisMovieCode, m.Genres, m.Overview, m.VoteAverage, m.VoteCount,
                   j.PersonalRating, j.Review, j.UpdatedAt
            FROM dbo.MovieJournals AS j
            INNER JOIN dbo.Movies AS m ON m.MovieId = j.MovieId
            WHERE j.IsFavorite = 1
            ORDER BY j.UpdatedAt DESC, m.Title;
            """;

        var favorites = new List<FavoriteMovie>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            string genres = reader.IsDBNull(6) ? "장르 정보 없음" : reader.GetString(6);
            favorites.Add(new FavoriteMovie
            {
                Movie = new Movie
                {
                    TmdbId = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                    Title = reader.GetString(1),
                    OriginalTitle = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ReleaseDate = reader.IsDBNull(3)
                        ? "개봉일 미정"
                        : reader.GetDateTime(3).ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
                    PosterUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                    KobisMovieCode = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Genres = genres,
                    GenreNames = genres.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Overview = reader.IsDBNull(7) ? "등록된 줄거리가 없습니다." : reader.GetString(7),
                    VoteAverage = reader.IsDBNull(8) ? 0 : Convert.ToDouble(reader.GetDecimal(8)),
                    VoteCount = reader.IsDBNull(9) ? 0 : reader.GetInt32(9)
                },
                PersonalRating = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                Review = reader.IsDBNull(11) ? "" : reader.GetString(11),
                UpdatedAt = reader.GetDateTime(12)
            });
        }

        return favorites;
    }

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
                    (KobisMovieCode, TmdbId, Title, OriginalTitle, ReleaseDate, PosterUrl,
                     Genres, Overview, VoteAverage, VoteCount)
                VALUES
                    (NULLIF(@KobisMovieCode, ''), NULLIF(@TmdbId, 0), @Title,
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
