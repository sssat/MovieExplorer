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

IF OBJECT_ID(N'dbo.BoxOfficeDaily', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BoxOfficeDaily
    (
        BoxOfficeDailyId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BoxOfficeDaily PRIMARY KEY,
        MovieId bigint NOT NULL,
        ShowDate date NOT NULL,
        Rank tinyint NOT NULL,
        DailyAudience bigint NOT NULL,
        CumulativeAudience bigint NOT NULL,
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_BoxOfficeDaily_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_BoxOfficeDaily_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_BoxOfficeDaily_Movies FOREIGN KEY (MovieId)
            REFERENCES dbo.Movies(MovieId),
        CONSTRAINT UQ_BoxOfficeDaily_ShowDate_MovieId UNIQUE (ShowDate, MovieId),
        CONSTRAINT CK_BoxOfficeDaily_Rank CHECK (Rank BETWEEN 1 AND 10),
        CONSTRAINT CK_BoxOfficeDaily_Audience CHECK (DailyAudience >= 0 AND CumulativeAudience >= 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BoxOfficeDaily') AND name = N'IX_BoxOfficeDaily_ShowDate_Rank')
    CREATE INDEX IX_BoxOfficeDaily_ShowDate_Rank
        ON dbo.BoxOfficeDaily(ShowDate, Rank) INCLUDE (MovieId, DailyAudience, CumulativeAudience);
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
