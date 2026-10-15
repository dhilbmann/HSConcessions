namespace HSConcessions.Web;

/// <summary>
/// Loads KEY=VALUE lines from an optional .env file into configuration. Keys use "__" as the section
/// separator, like environment variables (e.g. Auth__Providers__Google__ClientSecret=...).
/// Real environment variables take precedence.
/// </summary>
public static class DotEnv
{
    public static void Load(string path, ConfigurationManager config)
    {
        if (!File.Exists(path)) return;
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(key) is not null) continue;
            values[key.Replace("__", ":")] = value;
        }
        config.AddInMemoryCollection(values);
    }
}
