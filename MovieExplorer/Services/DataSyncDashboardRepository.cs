using Microsoft.Data.SqlClient;
using MovieExplorer.Models;

namespace MovieExplorer.Services;

public sealed class DataSyncDashboardRepository(string connectionString)
{
    public async Task<DataSyncDashboard> GetAsync(
        int boxOfficeLogPage,
        int upcomingLogPage,
        int pastLogPage,
        int logPageSize,
        CancellationToken cancellationToken = default)
    {
        boxOfficeLogPage = Math.Max(1, boxOfficeLogPage);
        upcomingLogPage = Math.Max(1, upcomingLogPage);
        pastLogPage = Math.Max(1, pastLogPage);
        logPageSize = Math.Max(1, logPageSize);
        await DatabaseInitializer.EnsureCreatedAsync(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT COUNT_BIG(*) AS MovieCount,
                   SUM(CASE WHEN PosterUrl IS NULL OR LTRIM(RTRIM(PosterUrl)) = '' THEN 1 ELSE 0 END) AS MissingPosterCount,
                   SUM(CASE WHEN TmdbId IS NULL THEN 1 ELSE 0 END) AS MissingTmdbCount
            FROM dbo.Movies;

            SELECT N'박스오피스' AS DataSetName,
                   COUNT(DISTINCT MovieId) AS MovieCount,
                   COUNT(DISTINCT ShowDate) AS SnapshotCount,
                   MIN(ShowDate) AS FromDate,
                   MAX(ShowDate) AS ToDate,
                   MAX(UpdatedAt) AS LastUpdatedAt
            FROM dbo.BoxOfficeRankings
            UNION ALL
            SELECT N'지난 영화·통계',
                   COUNT(DISTINCT MovieId),
                   COUNT(DISTINCT WeekEndDate), MIN(WeekEndDate), MAX(WeekEndDate), MAX(UpdatedAt)
            FROM dbo.PastMovieRankings
            UNION ALL
            SELECT N'개봉 예정 영화',
                   COUNT(DISTINCT MovieId),
                   COUNT(DISTINCT SnapshotDate), MIN(SnapshotDate), MAX(SnapshotDate), MAX(LastSeenAt)
            FROM dbo.UpcomingMovieCache;

            SELECT COUNT(*)
            FROM dbo.ApiSyncLogs
            WHERE SourceName NOT LIKE N'%주간%'
              AND SourceName NOT LIKE N'%지난 영화%'
              AND SourceName NOT LIKE N'%개봉예정%'
              AND SourceName NOT LIKE N'%개봉 예정%';

            SELECT
                   ApiSyncLogId, SourceName, TargetDate, StartedAt, CompletedAt, Status,
                   ReceivedCount, InsertedCount, UpdatedCount, ErrorMessage
            FROM dbo.ApiSyncLogs
            WHERE SourceName NOT LIKE N'%주간%'
              AND SourceName NOT LIKE N'%지난 영화%'
              AND SourceName NOT LIKE N'%개봉예정%'
              AND SourceName NOT LIKE N'%개봉 예정%'
            ORDER BY StartedAt DESC, ApiSyncLogId DESC
            OFFSET @BoxOfficeOffset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.ApiSyncLogs
            WHERE SourceName LIKE N'%개봉예정%' OR SourceName LIKE N'%개봉 예정%';

            SELECT
                   ApiSyncLogId, SourceName, TargetDate, StartedAt, CompletedAt, Status,
                   ReceivedCount, InsertedCount, UpdatedCount, ErrorMessage
            FROM dbo.ApiSyncLogs
            WHERE SourceName LIKE N'%개봉예정%' OR SourceName LIKE N'%개봉 예정%'
            ORDER BY StartedAt DESC, ApiSyncLogId DESC
            OFFSET @UpcomingOffset ROWS FETCH NEXT @PageSize ROWS ONLY;

            SELECT COUNT(*)
            FROM dbo.ApiSyncLogs
            WHERE SourceName LIKE N'%주간%' OR SourceName LIKE N'%지난 영화%';

            SELECT
                   ApiSyncLogId, SourceName, TargetDate, StartedAt, CompletedAt, Status,
                   ReceivedCount, InsertedCount, UpdatedCount, ErrorMessage
            FROM dbo.ApiSyncLogs
            WHERE SourceName LIKE N'%주간%' OR SourceName LIKE N'%지난 영화%'
            ORDER BY StartedAt DESC, ApiSyncLogId DESC
            OFFSET @PastOffset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BoxOfficeOffset", (boxOfficeLogPage - 1) * logPageSize);
        command.Parameters.AddWithValue("@UpcomingOffset", (upcomingLogPage - 1) * logPageSize);
        command.Parameters.AddWithValue("@PastOffset", (pastLogPage - 1) * logPageSize);
        command.Parameters.AddWithValue("@PageSize", logPageSize);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        int movieCount = 0;
        int missingPosterCount = 0;
        int missingTmdbCount = 0;
        if (await reader.ReadAsync(cancellationToken))
        {
            movieCount = Convert.ToInt32(reader.GetInt64(0));
            missingPosterCount = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
            missingTmdbCount = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
        }

        await reader.NextResultAsync(cancellationToken);
        var dataSets = new List<DataSetStatus>();
        while (await reader.ReadAsync(cancellationToken))
        {
            dataSets.Add(new DataSetStatus
            {
                Name = reader.GetString(0),
                MovieCount = reader.GetInt32(1),
                SnapshotCount = reader.GetInt32(2),
                FromDate = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                ToDate = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                LastUpdatedAt = reader.IsDBNull(5) ? null : ToLocalTime(reader.GetDateTime(5))
            });
        }

        await reader.NextResultAsync(cancellationToken);
        int boxOfficeLogCount = await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : 0;

        await reader.NextResultAsync(cancellationToken);
        List<ApiSyncLogItem> boxOfficeLogs = await ReadLogsAsync(reader, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        int upcomingLogCount = await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : 0;

        await reader.NextResultAsync(cancellationToken);
        List<ApiSyncLogItem> upcomingLogs = await ReadLogsAsync(reader, cancellationToken);

        await reader.NextResultAsync(cancellationToken);
        int pastLogCount = await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : 0;

        await reader.NextResultAsync(cancellationToken);
        List<ApiSyncLogItem> pastLogs = await ReadLogsAsync(reader, cancellationToken);

        return new DataSyncDashboard
        {
            RefreshedAt = DateTime.Now,
            MovieCount = movieCount,
            MissingPosterCount = missingPosterCount,
            MissingTmdbCount = missingTmdbCount,
            BoxOfficeLogCount = boxOfficeLogCount,
            UpcomingLogCount = upcomingLogCount,
            PastLogCount = pastLogCount,
            DataSets = dataSets,
            BoxOfficeLogs = boxOfficeLogs,
            UpcomingLogs = upcomingLogs,
            PastLogs = pastLogs
        };
    }

    private static async Task<List<ApiSyncLogItem>> ReadLogsAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var logs = new List<ApiSyncLogItem>();
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new ApiSyncLogItem
            {
                Id = reader.GetInt64(0),
                SourceName = reader.GetString(1),
                TargetDate = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                StartedAt = ToLocalTime(reader.GetDateTime(3)),
                CompletedAt = reader.IsDBNull(4) ? null : ToLocalTime(reader.GetDateTime(4)),
                Status = reader.GetString(5),
                ReceivedCount = reader.GetInt32(6),
                InsertedCount = reader.GetInt32(7),
                UpdatedCount = reader.GetInt32(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return logs;
    }

    private static DateTime ToLocalTime(DateTime utcValue) =>
        DateTime.SpecifyKind(utcValue, DateTimeKind.Utc).ToLocalTime();
}
