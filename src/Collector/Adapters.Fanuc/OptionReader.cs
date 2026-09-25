using System.Globalization;

namespace Adapters.Fanuc;

internal static class OptionReader
{
    public static string GetString(IReadOnlyDictionary<string, object?> options, string key, string fallback)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
    }

    public static int GetInt(IReadOnlyDictionary<string, object?> options, string key, int fallback)
    {
        if (!options.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            string s when int.TryParse(s, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback
        };
    }
}
