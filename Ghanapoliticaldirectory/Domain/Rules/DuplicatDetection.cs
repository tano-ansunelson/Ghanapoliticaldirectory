using System.Globalization;
using System.Text;

namespace Ghanapoliticaldirectory.Domain.Rules;

public static class NameNormalizer
{
    // Titles that do not identify a person. Removed only for matching, never from the stored name.
    private static readonly HashSet<string> Honorifics = new(StringComparer.Ordinal)
    {
        "hon", "honourable", "honorable", "dr", "prof", "professor", "mr", "mrs", "ms", "miss",
        "rev", "reverend", "alhaji", "alhaja", "nana", "nii", "madam", "sir", "chief", "ing", "eng"
    };

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue; // drop accents
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        var all = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = all.Where(t => !Honorifics.Contains(t)).ToArray();
        return string.Join(' ', kept.Length > 0 ? kept : all);
    }

    public static string[] Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

public enum DuplicateLevel { Possible = 0, Likely = 1 }

public sealed record DuplicateMatch(int PersonId, string FullName, double Score, DuplicateLevel Level, string Reason);

public static class DuplicateDetector
{
    public const double PossibleThreshold = 0.9;
    public const double LikelyThreshold = 0.95;

    public static IReadOnlyList<DuplicateMatch> Find(string? input, IEnumerable<(int Id, string Name)> existing, int limit = 5)
    {
        var a = NameNormalizer.Normalize(input);
        if (a.Length == 0) return Array.Empty<DuplicateMatch>();

        var matches = new List<DuplicateMatch>();
        foreach (var (id, name) in existing)
        {
            var (score, reason) = Score(a, NameNormalizer.Normalize(name));
            if (score >= PossibleThreshold)
                matches.Add(new DuplicateMatch(id, name, Math.Round(score, 3),
                    score >= LikelyThreshold ? DuplicateLevel.Likely : DuplicateLevel.Possible, reason));
        }
        return matches.OrderByDescending(m => m.Score).ThenBy(m => m.FullName, StringComparer.Ordinal).Take(limit).ToList();
    }

    /// <summary>True when the administrator must confirm "this is a different person" before a new profile is created.</summary>
    public static bool BlocksCreation(IReadOnlyList<DuplicateMatch> matches, bool confirmedNotDuplicate) =>
        !confirmedNotDuplicate && matches.Any(m => m.Level == DuplicateLevel.Likely);

    internal static (double Score, string Reason) Score(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return (0, "");
        if (a == b) return (1.0, "Same name");

        var ta = NameNormalizer.Tokens(a).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        var tb = NameNormalizer.Tokens(b).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        var sa = string.Join(' ', ta);
        var sb = string.Join(' ', tb);
        if (sa == sb) return (0.98, "Same words in a different order");

        // One name contains every word of the other ("Kofi Adjei" / "Kofi Adjei Darko").
        // Single-word names are excluded: a common first name alone proves nothing.
        if (ta.Length >= 2 && tb.Length >= 2 && (ta.All(tb.Contains) || tb.All(ta.Contains)))
            return (0.95, "One name contains every word of the other");

        var jw = JaroWinkler(sa, sb);
        return jw >= PossibleThreshold ? (jw, "Very similar spelling") : (0, "");
    }

    internal static double JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1.0;
        if (s1.Length == 0 || s2.Length == 0) return 0.0;

        var window = Math.Max(Math.Max(s1.Length, s2.Length) / 2 - 1, 0);
        var m1 = new bool[s1.Length];
        var m2 = new bool[s2.Length];
        var matches = 0;

        for (var i = 0; i < s1.Length; i++)
        {
            var lo = Math.Max(0, i - window);
            var hi = Math.Min(s2.Length - 1, i + window);
            for (var j = lo; j <= hi; j++)
            {
                if (m2[j] || s1[i] != s2[j]) continue;
                m1[i] = m2[j] = true;
                matches++;
                break;
            }
        }
        if (matches == 0) return 0.0;

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < s1.Length; i++)
        {
            if (!m1[i]) continue;
            while (!m2[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        double m = matches;
        var jaro = (m / s1.Length + m / s2.Length + (m - transpositions / 2.0) / m) / 3.0;

        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)); i++)
        {
            if (s1[i] == s2[i]) prefix++; else break;
        }
        return jaro + prefix * 0.1 * (1 - jaro);
    }
}
