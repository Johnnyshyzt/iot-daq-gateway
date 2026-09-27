using System.Text;

namespace IotDaq.Persistence.Shop;

public sealed class DiffLine
{
    public string Kind { get; init; } = "same";

    public string Text { get; init; } = "";
}

public static class TextDiff
{
    public static string Unified(string before, string after)
    {
        var lines = Lines(before, after);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            var mark = line.Kind switch
            {
                "add" => "+",
                "remove" => "-",
                _ => " "
            };
            builder.Append(mark).Append(line.Text).Append('\n');
        }

        return builder.ToString().TrimEnd();
    }

    public static List<DiffLine> Lines(string? before, string? after)
    {
        var left = Split(before);
        var right = Split(after);
        if (left.Length * (long)right.Length > 250_000)
        {
            var rough = new List<DiffLine>();
            foreach (var line in left)
            {
                rough.Add(new DiffLine { Kind = "remove", Text = line });
            }

            foreach (var line in right)
            {
                rough.Add(new DiffLine { Kind = "add", Text = line });
            }

            return rough;
        }

        var lcs = Lcs(left, right);
        var rows = new List<DiffLine>();
        var i = 0;
        var j = 0;
        foreach (var (li, rj) in lcs)
        {
            while (i < li)
            {
                rows.Add(new DiffLine { Kind = "remove", Text = left[i++] });
            }

            while (j < rj)
            {
                rows.Add(new DiffLine { Kind = "add", Text = right[j++] });
            }

            rows.Add(new DiffLine { Kind = "same", Text = left[i] });
            i++;
            j++;
        }

        while (i < left.Length)
        {
            rows.Add(new DiffLine { Kind = "remove", Text = left[i++] });
        }

        while (j < right.Length)
        {
            rows.Add(new DiffLine { Kind = "add", Text = right[j++] });
        }

        return rows;
    }

    private static string[] Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    private static List<(int Left, int Right)> Lcs(string[] left, string[] right)
    {
        var width = right.Length + 1;
        var dp = new int[(left.Length + 1) * width];
        for (var i = left.Length - 1; i >= 0; i--)
        {
            for (var j = right.Length - 1; j >= 0; j--)
            {
                dp[i * width + j] = left[i] == right[j]
                    ? dp[(i + 1) * width + j + 1] + 1
                    : Math.Max(dp[(i + 1) * width + j], dp[i * width + j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        var x = 0;
        var y = 0;
        while (x < left.Length && y < right.Length)
        {
            if (left[x] == right[y])
            {
                pairs.Add((x, y));
                x++;
                y++;
            }
            else if (dp[(x + 1) * width + y] >= dp[x * width + y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }
}
