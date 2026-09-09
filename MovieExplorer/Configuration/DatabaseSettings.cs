namespace MovieExplorer.Configuration;

public static class DatabaseSettings
{
    public const string DefaultConnectionString =
        "Server=localhost;Database=MovieExplorer;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string ConnectionString =>
        LocalSecrets.Get("MOVIEEXPLORER_DB_CONNECTION") ?? DefaultConnectionString;
}
