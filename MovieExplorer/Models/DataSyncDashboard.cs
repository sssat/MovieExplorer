namespace MovieExplorer.Models;

public sealed class DataSyncDashboard
{
    public DateTime RefreshedAt { get; init; }
    public int MovieCount { get; init; }
    public int MissingPosterCount { get; init; }
    public int MissingTmdbCount { get; init; }
    public int BoxOfficeLogCount { get; init; }
    public int UpcomingLogCount { get; init; }
    public int PastLogCount { get; init; }
    public List<DataSetStatus> DataSets { get; init; } = [];
    public List<ApiSyncLogItem> BoxOfficeLogs { get; init; } = [];
    public List<ApiSyncLogItem> UpcomingLogs { get; init; } = [];
    public List<ApiSyncLogItem> PastLogs { get; init; } = [];

    public string MovieCountLabel => $"{MovieCount:N0}편";
    public string MissingPosterCountLabel => $"{MissingPosterCount:N0}편";
    public string MissingTmdbCountLabel => $"{MissingTmdbCount:N0}편";
    public string RefreshedAtLabel => $"마지막 확인 {RefreshedAt:yyyy.MM.dd HH:mm:ss}";
}

public sealed class DataSetStatus
{
    public string Name { get; init; } = "";
    public int MovieCount { get; init; }
    public int SnapshotCount { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
    public DateTime? LastUpdatedAt { get; init; }

    public string MovieCountLabel => $"{MovieCount:N0}편";
    public string CoverageLabel => FromDate is null
        ? "보유한 정보가 없습니다."
        : FromDate == ToDate
            ? $"{FromDate:yyyy.MM.dd}"
            : $"{FromDate:yyyy.MM.dd} ~ {ToDate:yyyy.MM.dd}";
    public string SnapshotCountLabel => SnapshotCount == 0
        ? "-"
        : Name switch
        {
            "박스오피스" => $"{SnapshotCount:N0}일",
            "지난 영화·통계" => $"{SnapshotCount:N0}주차",
            "개봉 예정 영화" => $"{SnapshotCount:N0}회",
            _ => $"{SnapshotCount:N0}개"
        };
    public string LastUpdatedLabel => LastUpdatedAt is null
        ? "업데이트 내역 없음"
        : $"{LastUpdatedAt:yyyy.MM.dd HH:mm}";
}

public sealed class ApiSyncLogItem
{
    public long Id { get; init; }
    public string SourceName { get; init; } = "";
    public DateTime? TargetDate { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string Status { get; init; } = "";
    public int ReceivedCount { get; init; }
    public int InsertedCount { get; init; }
    public int UpdatedCount { get; init; }
    public string? ErrorMessage { get; init; }

    public string TargetDateLabel => TargetDate?.ToString("yyyy.MM.dd") ?? "-";
    public string StartedAtLabel => StartedAt.ToString("yyyy.MM.dd HH:mm:ss");
    public string StatusLabel => Status switch
    {
        "Success" => "성공",
        "Failed" => "실패",
        "Running" => "진행 중",
        _ => Status
    };
    public string ResultLabel => $"확인 {ReceivedCount:N0} · 추가 {InsertedCount:N0} · 변경 {UpdatedCount:N0}";
    public string ErrorLabel => string.IsNullOrWhiteSpace(ErrorMessage) ? "-" : "일부 정보를 반영하지 못했습니다.";
}
