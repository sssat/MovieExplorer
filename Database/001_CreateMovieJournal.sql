SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'MovieExplorer') IS NULL
    CREATE DATABASE MovieExplorer;
GO

USE MovieExplorer;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

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
GO

IF COL_LENGTH(N'dbo.Movies', N'Genres') IS NULL
    ALTER TABLE dbo.Movies ADD Genres nvarchar(300) NULL;
IF COL_LENGTH(N'dbo.Movies', N'Overview') IS NULL
    ALTER TABLE dbo.Movies ADD Overview nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.Movies', N'VoteAverage') IS NULL
    ALTER TABLE dbo.Movies ADD VoteAverage decimal(3,1) NULL;
IF COL_LENGTH(N'dbo.Movies', N'VoteCount') IS NULL
    ALTER TABLE dbo.Movies ADD VoteCount int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Movies') AND name = N'UX_Movies_KobisMovieCode')
    CREATE UNIQUE INDEX UX_Movies_KobisMovieCode
        ON dbo.Movies(KobisMovieCode) WHERE KobisMovieCode IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Movies') AND name = N'UX_Movies_TmdbId')
    CREATE UNIQUE INDEX UX_Movies_TmdbId
        ON dbo.Movies(TmdbId) WHERE TmdbId IS NOT NULL;
GO

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
GO

IF OBJECT_ID(N'dbo.BoxOfficeDaily', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.BoxOfficeRankings', N'U') IS NULL
    EXEC sp_rename N'dbo.BoxOfficeDaily', N'BoxOfficeRankings';
GO

IF OBJECT_ID(N'dbo.BoxOfficeRankings', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.BoxOfficeRankings', N'BoxOfficeDailyId') IS NOT NULL
   AND COL_LENGTH(N'dbo.BoxOfficeRankings', N'BoxOfficeRankingId') IS NULL
    EXEC sp_rename N'dbo.BoxOfficeRankings.BoxOfficeDailyId', N'BoxOfficeRankingId', N'COLUMN';
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BoxOfficeRankings') AND name IN (N'IX_BoxOfficeDaily_ShowDate_Rank', N'IX_BoxOfficeRankings_ShowDate_Rank'))
    CREATE INDEX IX_BoxOfficeRankings_ShowDate_Rank
        ON dbo.BoxOfficeRankings(ShowDate, Rank) INCLUDE (MovieId, DailyAudience, CumulativeAudience);
GO

IF OBJECT_ID(N'dbo.BoxOfficeWeekly', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.PastMovieRankings', N'U') IS NULL
    EXEC sp_rename N'dbo.BoxOfficeWeekly', N'PastMovieRankings';
GO

IF OBJECT_ID(N'dbo.PastMovieRankings', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.PastMovieRankings', N'BoxOfficeWeeklyId') IS NOT NULL
   AND COL_LENGTH(N'dbo.PastMovieRankings', N'PastMovieRankingId') IS NULL
    EXEC sp_rename N'dbo.PastMovieRankings.BoxOfficeWeeklyId', N'PastMovieRankingId', N'COLUMN';
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.PastMovieRankings') AND name IN (N'IX_BoxOfficeWeekly_WeekEndDate_Rank', N'IX_PastMovieRankings_WeekEndDate_Rank'))
    CREATE INDEX IX_PastMovieRankings_WeekEndDate_Rank
        ON dbo.PastMovieRankings(WeekEndDate, Rank) INCLUDE (MovieId, WeeklyAudience, CumulativeAudience);
GO

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
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.UpcomingMovieCache') AND name = N'IX_UpcomingMovieCache_SnapshotDate')
    CREATE INDEX IX_UpcomingMovieCache_SnapshotDate
        ON dbo.UpcomingMovieCache(SnapshotDate) INCLUDE (MovieId);
GO

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
GO
