namespace MovieExplorer.Models;

public sealed class MovieJournal
{
    public bool IsFavorite { get; init; }
    public int? PersonalRating { get; init; }
    public string Review { get; init; } = "";
    public DateTime? UpdatedAt { get; init; }
}
