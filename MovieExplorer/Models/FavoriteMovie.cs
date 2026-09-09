namespace MovieExplorer.Models;

public sealed class FavoriteMovie
{
    public required Movie Movie { get; init; }
    public int? PersonalRating { get; init; }
    public string Review { get; init; } = "";
    public DateTime UpdatedAt { get; init; }

    public string RatingLabel => PersonalRating is null
        ? "개인 평점 없음"
        : $"내 평점 {new string('★', PersonalRating.Value)} {PersonalRating}점";

    public string ReviewLabel => string.IsNullOrWhiteSpace(Review)
        ? "작성한 감상평이 없습니다."
        : Review;

    public string UpdatedAtLabel => $"기록 수정 {UpdatedAt.ToLocalTime():yyyy.MM.dd}";
}
