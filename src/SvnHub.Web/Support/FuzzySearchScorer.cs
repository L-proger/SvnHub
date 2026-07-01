using System.Globalization;
using System.Text;

namespace SvnHub.Web.Support;

internal static class FuzzySearchScorer
{
    public static int Score(string candidate, string query)
    {
        var tokens = SplitWords(query);
        if (tokens.Length == 0)
        {
            return 0;
        }

        var allowShortAcronym = tokens.Length > 1;
        return ScoreTokens(candidate, tokens, allowShortAcronym);
    }

    private static int ScoreTokens(string candidate, string[] tokens, bool allowShortAcronym)
    {
        var totalScore = 0;
        foreach (var token in tokens)
        {
            var tokenScore = ScoreToken(candidate, token, allowShortAcronym);
            if (tokenScore <= 0)
            {
                return 0;
            }

            totalScore += tokenScore;
        }

        return totalScore;
    }

    private static int ScoreToken(string candidate, string token, bool allowShortAcronym)
    {
        var candidateNormalized = Normalize(candidate);
        var tokenNormalized = Normalize(token);
        var candidateCompact = Compact(candidate);
        var tokenCompact = Compact(token);

        if (candidateNormalized.Length == 0 || tokenNormalized.Length == 0)
        {
            return 0;
        }

        if (tokenCompact.Length < 3)
        {
            return ScoreShortToken(candidate, candidateNormalized, candidateCompact, tokenNormalized, tokenCompact, allowShortAcronym);
        }

        if (string.Equals(candidateNormalized, tokenNormalized, StringComparison.Ordinal))
        {
            return 1000;
        }

        if (string.Equals(candidateCompact, tokenCompact, StringComparison.Ordinal))
        {
            return 980;
        }

        if (candidateNormalized.StartsWith(tokenNormalized, StringComparison.Ordinal))
        {
            return 940 - Math.Min(120, candidateNormalized.Length - tokenNormalized.Length);
        }

        if (candidateCompact.StartsWith(tokenCompact, StringComparison.Ordinal))
        {
            return 900 - Math.Min(120, candidateCompact.Length - tokenCompact.Length);
        }

        var containsIndex = candidateNormalized.IndexOf(tokenNormalized, StringComparison.Ordinal);
        if (containsIndex >= 0)
        {
            return 820 - Math.Min(160, containsIndex * 8);
        }

        var compactContainsIndex = candidateCompact.IndexOf(tokenCompact, StringComparison.Ordinal);
        if (compactContainsIndex >= 0)
        {
            return 780 - Math.Min(160, compactContainsIndex * 8);
        }

        var acronymScore = ScoreAcronym(candidate, tokenCompact, allowShortAcronym: true);
        if (acronymScore > 0)
        {
            return acronymScore;
        }

        var wordScore = ScoreNearestWord(candidate, tokenCompact);
        if (wordScore > 0)
        {
            return wordScore;
        }

        if (IsSubsequence(tokenCompact, candidateCompact, out var gaps))
        {
            return 520 - Math.Min(240, gaps * 10);
        }

        return 0;
    }

    private static int ScoreShortToken(
        string candidate,
        string candidateNormalized,
        string candidateCompact,
        string tokenNormalized,
        string tokenCompact,
        bool allowShortAcronym)
    {
        if (string.Equals(candidateNormalized, tokenNormalized, StringComparison.Ordinal) ||
            string.Equals(candidateCompact, tokenCompact, StringComparison.Ordinal))
        {
            return 1000;
        }

        if (candidateNormalized.StartsWith(tokenNormalized, StringComparison.Ordinal) ||
            candidateCompact.StartsWith(tokenCompact, StringComparison.Ordinal))
        {
            return 880;
        }

        if (allowShortAcronym)
        {
            var wordScore = ScoreShortWords(candidate, tokenCompact);
            if (wordScore > 0)
            {
                return wordScore;
            }
        }

        return ScoreAcronym(candidate, tokenCompact, allowShortAcronym);
    }

    private static int ScoreShortWords(string candidate, string tokenCompact)
    {
        var bestScore = 0;
        foreach (var word in SplitWords(candidate))
        {
            var wordCompact = Compact(word);
            if (wordCompact.Length == 0)
            {
                continue;
            }

            if (wordCompact.StartsWith(tokenCompact, StringComparison.Ordinal))
            {
                bestScore = Math.Max(bestScore, 760 - Math.Min(120, wordCompact.Length - tokenCompact.Length));
                continue;
            }

            if (IsDigits(tokenCompact) && IsDigits(wordCompact))
            {
                var containsIndex = wordCompact.IndexOf(tokenCompact, StringComparison.Ordinal);
                if (containsIndex >= 0)
                {
                    bestScore = Math.Max(bestScore, 820 - Math.Min(160, containsIndex * 16));
                }
            }
        }

        return bestScore;
    }

    private static int ScoreAcronym(string candidate, string tokenCompact, bool allowShortAcronym)
    {
        if (tokenCompact.Length < 3 && !allowShortAcronym)
        {
            return 0;
        }

        var words = SplitWords(candidate);
        if (words.Length < 2)
        {
            return 0;
        }

        var acronym = new string(words.Where(w => w.Length > 0).Select(w => w[0]).ToArray());
        if (acronym.Length == 0)
        {
            return 0;
        }

        if (string.Equals(acronym, tokenCompact, StringComparison.Ordinal))
        {
            return 760;
        }

        if (acronym.StartsWith(tokenCompact, StringComparison.Ordinal))
        {
            return 730 - Math.Min(120, acronym.Length - tokenCompact.Length);
        }

        var containsIndex = acronym.IndexOf(tokenCompact, StringComparison.Ordinal);
        if (containsIndex >= 0)
        {
            return 700 - Math.Min(100, containsIndex * 12);
        }

        if (tokenCompact.Length >= 3 && IsSubsequence(tokenCompact, acronym, out var gaps))
        {
            return 620 - Math.Min(180, gaps * 12);
        }

        return 0;
    }

    private static int ScoreNearestWord(string candidate, string tokenCompact)
    {
        var maxDistance = GetMaxEditDistance(tokenCompact.Length);
        if (maxDistance <= 0)
        {
            return 0;
        }

        var bestScore = 0;
        foreach (var word in SplitWords(candidate))
        {
            var wordCompact = Compact(word);
            if (wordCompact.Length == 0)
            {
                continue;
            }

            if (IsAdjacentTransposition(tokenCompact, wordCompact))
            {
                bestScore = Math.Max(bestScore, 720);
                continue;
            }

            var distance = BoundedEditDistance(tokenCompact, wordCompact, maxDistance);
            if (distance <= maxDistance)
            {
                bestScore = Math.Max(bestScore, 660 - distance * 100 - Math.Abs(wordCompact.Length - tokenCompact.Length) * 8);
            }
        }

        return bestScore;
    }

    private static int GetMaxEditDistance(int length)
    {
        if (length < 4)
        {
            return 0;
        }

        return length < 8 ? 1 : 2;
    }

    private static string[] SplitWords(string value)
    {
        var normalized = Normalize(value);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var result = new List<char>(value.Length + 8);
        var previousWasSeparator = true;
        char previousLetterOrDigit = '\0';
        var normalizedValue = value.Normalize(NormalizationForm.FormKC);

        for (var i = 0; i < normalizedValue.Length; i++)
        {
            var rawChar = normalizedValue[i];
            if (!char.IsLetterOrDigit(rawChar))
            {
                if (!previousWasSeparator)
                {
                    result.Add(' ');
                    previousWasSeparator = true;
                }

                previousLetterOrDigit = '\0';
                continue;
            }

            var currentIsDigit = char.IsDigit(rawChar);
            var currentIsLetter = char.IsLetter(rawChar);
            var previousIsDigit = char.IsDigit(previousLetterOrDigit);
            var previousIsLetter = char.IsLetter(previousLetterOrDigit);
            var nextIsLower = i + 1 < normalizedValue.Length && char.IsLower(normalizedValue[i + 1]);
            if (previousLetterOrDigit != '\0' &&
                !previousWasSeparator &&
                ((char.IsUpper(rawChar) && nextIsLower) ||
                    (previousIsLetter && currentIsDigit) ||
                    (previousIsDigit && currentIsLetter)))
            {
                result.Add(' ');
            }

            result.Add(char.ToLower(rawChar, CultureInfo.InvariantCulture));
            previousWasSeparator = false;
            previousLetterOrDigit = rawChar;
        }

        while (result.Count > 0 && result[^1] == ' ')
        {
            result.RemoveAt(result.Count - 1);
        }

        return new string(result.ToArray());
    }

    private static string Compact(string value)
    {
        var normalized = Normalize(value);
        return normalized.Replace(" ", "", StringComparison.Ordinal);
    }

    private static bool IsSubsequence(string needle, string haystack, out int gaps)
    {
        gaps = 0;
        if (needle.Length == 0 || haystack.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        var lastMatch = -1;
        var needleIndex = 0;
        for (var i = 0; i < haystack.Length && needleIndex < needle.Length; i++)
        {
            if (haystack[i] != needle[needleIndex])
            {
                continue;
            }

            if (lastMatch >= 0)
            {
                gaps += i - lastMatch - 1;
            }

            lastMatch = i;
            needleIndex++;
        }

        return needleIndex == needle.Length;
    }

    private static bool IsDigits(string value)
    {
        return value.Length > 0 && value.All(char.IsDigit);
    }

    private static bool IsAdjacentTransposition(string left, string right)
    {
        if (left.Length != right.Length || left.Length < 2)
        {
            return false;
        }

        var firstDifference = -1;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i])
            {
                continue;
            }

            if (firstDifference >= 0)
            {
                return i == firstDifference + 1 &&
                    left[firstDifference] == right[i] &&
                    left[i] == right[firstDifference] &&
                    left[(i + 1)..] == right[(i + 1)..];
            }

            firstDifference = i;
        }

        return false;
    }

    private static int BoundedEditDistance(string left, string right, int maxDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maxDistance)
        {
            return maxDistance + 1;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            var rowBest = current[0];

            for (var j = 1; j <= right.Length; j++)
            {
                var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + substitutionCost);
                rowBest = Math.Min(rowBest, current[j]);
            }

            if (rowBest > maxDistance)
            {
                return maxDistance + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
