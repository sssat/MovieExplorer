using System.IO;

namespace MovieExplorer.Configuration;

public static class LocalSecrets
{
    public static string? Get(string key)
    {
        string? environmentValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue;

        string? userValue = Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(userValue))
            return userValue;

        string? envFile = FindEnvFile();
        if (envFile is null)
            return null;

        foreach (string rawLine in File.ReadLines(envFile))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int separator = line.IndexOf('=');
            if (separator <= 0 || !line[..separator].Trim().Equals(key, StringComparison.Ordinal))
                continue;

            return line[(separator + 1)..].Trim().Trim('"', '\'');
        }

        return null;
    }

    private static string? FindEnvFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
