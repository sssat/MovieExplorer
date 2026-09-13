using System.Data;
using Microsoft.Data.SqlClient;

namespace MovieExplorer.Services;

public static class DatabaseInitializer
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HashSet<string> InitializedConnections = [];

    public static async Task EnsureCreatedAsync(string connectionString)
    {
        await Gate.WaitAsync();
        try
        {
            if (InitializedConnections.Contains(connectionString))
                return;

            var builder = new SqlConnectionStringBuilder(connectionString);
            string databaseName = builder.InitialCatalog;
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new InvalidOperationException("DB 연결 문자열에 Database가 지정되지 않았습니다.");

            var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            await using (var masterConnection = new SqlConnection(masterBuilder.ConnectionString))
            {
                await masterConnection.OpenAsync();
                const string createDatabaseSql = """
                    IF DB_ID(@DatabaseName) IS NULL
                    BEGIN
                        DECLARE @Sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
                        EXEC sys.sp_executesql @Sql;
                    END;
                    """;
                await using var createDatabase = new SqlCommand(createDatabaseSql, masterConnection);
                createDatabase.Parameters.Add("@DatabaseName", SqlDbType.NVarChar, 128).Value = databaseName;
                await createDatabase.ExecuteNonQueryAsync();
            }

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var createSchema = new SqlCommand(SchemaSql, connection);
            await createSchema.ExecuteNonQueryAsync();
            InitializedConnections.Add(connectionString);
        }
        finally
        {
            Gate.Release();
        }
    }

    private const string SchemaSql = """
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;

        IF OBJECT_ID(N'dbo.Movies', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.Movies
            (
                MovieId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Movies PRIMARY KEY,
                KobisMovieCode nvarchar(20) NULL,
                TmdbId int NULL,
                Title nvarchar(200) NOT NULL,
                OriginalTitle nvarchar(200) NULL,
                ReleaseDate date NULL,
                PosterUrl nvarchar(500) NULL,
                Genres nvarchar(300) NULL,
                Overview nvarchar(max) NULL,
                VoteAverage decimal(3,1) NULL,
                VoteCount int NULL,
                CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_Movies_CreatedAt DEFAULT SYSUTCDATETIME(),
                UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_Movies_UpdatedAt DEFAULT SYSUTCDATETIME()
            );
        END;

        IF COL_LENGTH(N'dbo.Movies', N'Genres') IS NULL
            ALTER TABLE dbo.Movies ADD Genres nvarchar(300) NULL;
        IF COL_LENGTH(N'dbo.Movies', N'Overview') IS NULL
            ALTER TABLE dbo.Movies ADD Overview nvarchar(max) NULL;
        IF COL_LENGTH(N'dbo.Movies', N'VoteAverage') IS NULL
            ALTER TABLE dbo.Movies ADD VoteAverage decimal(3,1) NULL;
        IF COL_LENGTH(N'dbo.Movies', N'VoteCount') IS NULL
            ALTER TABLE dbo.Movies ADD VoteCount int NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Movies') AND name = N'UX_Movies_KobisMovieCode')
            CREATE UNIQUE INDEX UX_Movies_KobisMovieCode
                ON dbo.Movies(KobisMovieCode) WHERE KobisMovieCode IS NOT NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Movies') AND name = N'UX_Movies_TmdbId')
            CREATE UNIQUE INDEX UX_Movies_TmdbId
                ON dbo.Movies(TmdbId) WHERE TmdbId IS NOT NULL;

        IF OBJECT_ID(N'dbo.MovieJournals', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.MovieJournals
            (
                MovieJournalId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MovieJournals PRIMARY KEY,
                MovieId bigint NOT NULL,
                IsFavorite bit NOT NULL CONSTRAINT DF_MovieJournals_IsFavorite DEFAULT 0,
                PersonalRating int NULL,
                Review nvarchar(2000) NULL,
                UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_MovieJournals_UpdatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_MovieJournals_Movies FOREIGN KEY (MovieId)
                    REFERENCES dbo.Movies(MovieId) ON DELETE CASCADE,
                CONSTRAINT UQ_MovieJournals_MovieId UNIQUE (MovieId),
                CONSTRAINT CK_MovieJournals_PersonalRating
                    CHECK (PersonalRating IS NULL OR PersonalRating BETWEEN 1 AND 5)
            );
        END;

        IF OBJECT_ID(N'dbo.BoxOfficeDaily', N'U') IS NOT NULL
           AND OBJECT_ID(N'dbo.BoxOfficeRankings', N'U') IS NULL
            EXEC sp_rename N'dbo.BoxOfficeDaily', N'BoxOfficeRankings';

        IF OBJECT_ID(N'dbo.BoxOfficeRankings', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.BoxOfficeRankings', N'BoxOfficeDailyId') IS NOT NULL
           AND COL_LENGTH(N'dbo.BoxOfficeRankings', N'BoxOfficeRankingId') IS NULL
            EXEC sp_rename N'dbo.BoxOfficeRankings.BoxOfficeDailyId', N'BoxOfficeRankingId', N'COLUMN';

        IF OBJECT_ID(N'dbo.BoxOfficeRankings', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.BoxOfficeRankings
            (
                BoxOfficeRankingId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BoxOfficeRankings PRIMARY KEY,
                MovieId bigint NOT NULL,
                ShowDate date NOT NULL,
                Rank tinyint NOT NULL,
                DailyAudience bigint NOT NULL,
                CumulativeAudience bigint NOT NULL,
                CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BoxOfficeRankings_CreatedAt DEFAULT SYSUTCDATETIME(),
                UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_BoxOfficeRankings_UpdatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_BoxOfficeRankings_Movies FOREIGN KEY (MovieId)
                    REFERENCES dbo.Movies(MovieId),
                CONSTRAINT UQ_BoxOfficeRankings_ShowDate_MovieId UNIQUE (ShowDate, MovieId),
                CONSTRAINT CK_BoxOfficeRankings_Rank CHECK (Rank BETWEEN 1 AND 10),
                CONSTRAINT CK_BoxOfficeRankings_Audience CHECK (DailyAudience >= 0 AND CumulativeAudience >= 0)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BoxOfficeRankings') AND name IN (N'IX_BoxOfficeDaily_ShowDate_Rank', N'IX_BoxOfficeRankings_ShowDate_Rank'))
            CREATE INDEX IX_BoxOfficeRankings_ShowDate_Rank
                ON dbo.BoxOfficeRankings(ShowDate, Rank) INCLUDE (MovieId, DailyAudience, CumulativeAudience);

        IF OBJECT_ID(N'dbo.BoxOfficeWeekly', N'U') IS NOT NULL
           AND OBJECT_ID(N'dbo.PastMovieRankings', N'U') IS NULL
            EXEC sp_rename N'dbo.BoxOfficeWeekly', N'PastMovieRankings';

        IF OBJECT_ID(N'dbo.PastMovieRankings', N'U') IS NOT NULL
           AND COL_LENGTH(N'dbo.PastMovieRankings', N'BoxOfficeWeeklyId') IS NOT NULL
           AND COL_LENGTH(N'dbo.PastMovieRankings', N'PastMovieRankingId') IS NULL
            EXEC sp_rename N'dbo.PastMovieRankings.BoxOfficeWeeklyId', N'PastMovieRankingId', N'COLUMN';

        IF OBJECT_ID(N'dbo.PastMovieRankings', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PastMovieRankings
            (
                PastMovieRankingId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_PastMovieRankings PRIMARY KEY,
                MovieId bigint NOT NULL,
                WeekEndDate date NOT NULL,
                Rank tinyint NOT NULL,
                WeeklyAudience bigint NOT NULL,
                CumulativeAudience bigint NOT NULL,
                CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_PastMovieRankings_CreatedAt DEFAULT SYSUTCDATETIME(),
                UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_PastMovieRankings_UpdatedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_PastMovieRankings_Movies FOREIGN KEY (MovieId) REFERENCES dbo.Movies(MovieId),
                CONSTRAINT UQ_PastMovieRankings_WeekEndDate_MovieId UNIQUE (WeekEndDate, MovieId),
                CONSTRAINT CK_PastMovieRankings_Rank CHECK (Rank BETWEEN 1 AND 10),
                CONSTRAINT CK_PastMovieRankings_Audience CHECK (WeeklyAudience >= 0 AND CumulativeAudience >= 0)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PastMovieRankings') AND name IN (N'IX_BoxOfficeWeekly_WeekEndDate_Rank', N'IX_PastMovieRankings_WeekEndDate_Rank'))
            CREATE INDEX IX_PastMovieRankings_WeekEndDate_Rank
                ON dbo.PastMovieRankings(WeekEndDate, Rank) INCLUDE (MovieId, WeeklyAudience, CumulativeAudience);

        IF OBJECT_ID(N'dbo.PastMovieSyncWeeks', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.PastMovieSyncWeeks
            (
                WeekEndDate date NOT NULL CONSTRAINT PK_PastMovieSyncWeeks PRIMARY KEY,
                RecordCount int NOT NULL,
                LastSyncedAt datetime2(0) NOT NULL CONSTRAINT DF_PastMovieSyncWeeks_LastSyncedAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT CK_PastMovieSyncWeeks_RecordCount CHECK (RecordCount >= 0)
            );
        END;

        INSERT dbo.PastMovieSyncWeeks (WeekEndDate, RecordCount, LastSyncedAt)
        SELECT rankings.WeekEndDate, COUNT(*), MAX(rankings.UpdatedAt)
        FROM dbo.PastMovieRankings rankings
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.PastMovieSyncWeeks synced
            WHERE synced.WeekEndDate = rankings.WeekEndDate
        )
        GROUP BY rankings.WeekEndDate;

        IF OBJECT_ID(N'dbo.UpcomingMovieCache', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.UpcomingMovieCache
            (
                MovieId bigint NOT NULL CONSTRAINT PK_UpcomingMovieCache PRIMARY KEY,
                SnapshotDate date NOT NULL,
                LastSeenAt datetime2(0) NOT NULL CONSTRAINT DF_UpcomingMovieCache_LastSeenAt DEFAULT SYSUTCDATETIME(),
                CONSTRAINT FK_UpcomingMovieCache_Movies FOREIGN KEY (MovieId) REFERENCES dbo.Movies(MovieId)
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UpcomingMovieCache') AND name = N'IX_UpcomingMovieCache_SnapshotDate')
            CREATE INDEX IX_UpcomingMovieCache_SnapshotDate
                ON dbo.UpcomingMovieCache(SnapshotDate) INCLUDE (MovieId);

        IF OBJECT_ID(N'dbo.ApiSyncLogs', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.ApiSyncLogs
            (
                ApiSyncLogId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiSyncLogs PRIMARY KEY,
                SourceName nvarchar(30) NOT NULL,
                TargetDate date NULL,
                StartedAt datetime2(0) NOT NULL CONSTRAINT DF_ApiSyncLogs_StartedAt DEFAULT SYSUTCDATETIME(),
                CompletedAt datetime2(0) NULL,
                Status varchar(10) NOT NULL,
                ReceivedCount int NOT NULL CONSTRAINT DF_ApiSyncLogs_ReceivedCount DEFAULT 0,
                InsertedCount int NOT NULL CONSTRAINT DF_ApiSyncLogs_InsertedCount DEFAULT 0,
                UpdatedCount int NOT NULL CONSTRAINT DF_ApiSyncLogs_UpdatedCount DEFAULT 0,
                ErrorMessage nvarchar(2000) NULL,
                CONSTRAINT CK_ApiSyncLogs_Status CHECK (Status IN ('Running', 'Success', 'Failed'))
            );
        END;
        """;
}
