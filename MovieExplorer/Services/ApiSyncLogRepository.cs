using System.Data;
using Microsoft.Data.SqlClient;

namespace MovieExplorer.Services;

internal static class ApiSyncLogRepository
{
    public static async Task<long> StartAsync(
        string connectionString,
        string sourceName,
        DateTime? targetDate,
        int receivedCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.ApiSyncLogs (SourceName, TargetDate, Status, ReceivedCount)
            OUTPUT INSERTED.ApiSyncLogId
            VALUES (@SourceName, @TargetDate, 'Running', @ReceivedCount);
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@SourceName", SqlDbType.NVarChar, 30).Value = sourceName;
        command.Parameters.Add("@TargetDate", SqlDbType.Date).Value =
            targetDate is null ? DBNull.Value : targetDate.Value.Date;
        command.Parameters.Add("@ReceivedCount", SqlDbType.Int).Value = receivedCount;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public static async Task CompleteAsync(
        string connectionString,
        long logId,
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
            WHERE ApiSyncLogId = @LogId;
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@LogId", SqlDbType.BigInt).Value = logId;
        command.Parameters.Add("@Status", SqlDbType.VarChar, 10).Value = status;
        command.Parameters.Add("@InsertedCount", SqlDbType.Int).Value = insertedCount;
        command.Parameters.Add("@UpdatedCount", SqlDbType.Int).Value = updatedCount;
        command.Parameters.Add("@ErrorMessage", SqlDbType.NVarChar, 2000).Value =
            string.IsNullOrWhiteSpace(errorMessage)
                ? DBNull.Value
                : errorMessage[..Math.Min(errorMessage.Length, 2000)];
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
