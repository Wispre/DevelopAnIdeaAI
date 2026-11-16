using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Pulls numbers out of recognized speech, whether written as digits ("45", "12.5")
/// or as words ("forty-five", "one hundred and twenty", "a hundred").
/// </summary>
public static class SpokenNumbers
{
    private static readonly Dictionary<string, int> Units = new Dictionary<string, int>
    {
        {"zero", 0}, {"one", 1}, {"two", 2}, {"three", 3}, {"four", 4}, {"five", 5},
        {"six", 6}, {"seven", 7}, {"eight", 8}, {"nine", 9}, {"ten", 10}, {"eleven", 11},
        {"twelve", 12}, {"thirteen", 13}, {"fourteen", 14}, {"fifteen", 15}, {"sixteen", 16},
        {"seventeen", 17}, {"eighteen", 18}, {"nineteen", 19},
    };

    private static readonly Dictionary<string, int> Tens = new Dictionary<string, int>
    {
        {"twenty", 20}, {"thirty", 30}, {"forty", 40}, {"fourty", 40}, {"fifty", 50},
        {"sixty", 60}, {"seventy", 70}, {"eighty", 80}, {"ninety", 90},
    };

    private static readonly Regex Tokenizer = new Regex(@"\d+(?:[.,]\d+)?|[a-z]+", RegexOptions.Compiled);

    /// <summary>All numbers found in the text, in the order they were spoken.</summary>
    public static List<float> Extract(string text)
    {
        var numbers = new List<float>();
        if (string.IsNullOrEmpty(text)) return numbers;

        var tokens = new List<string>();
        foreach (Match m in Tokenizer.Matches(text.ToLowerInvariant()))
            tokens.Add(m.Value);

        long total = 0, current = 0;
        bool building = false;

        void Flush()
        {
            if (building) numbers.Add(total + current);
            total = current = 0;
            building = false;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            string t = tokens[i];

            if (char.IsDigit(t[0]))
            {
                Flush();
                numbers.Add(float.Parse(t.Replace(',', '.'), CultureInfo.InvariantCulture));
                continue;
            }

            if (Units.TryGetValue(t, out int u))
            {
                // A unit only joins a bare tens word ("forty" + "five"); otherwise it's a new number ("five, ten").
                long lastTwo = current % 100;
                bool joinsTens = lastTwo >= 20 && lastTwo % 10 == 0 && u < 10;
                if (building && lastTwo != 0 && !joinsTens) Flush();
                current += u; building = true; continue;
            }
            if (Tens.TryGetValue(t, out int tn))
            {
                if (building && current % 100 != 0) Flush(); // "five twenty" -> 5, 20
                current += tn; building = true; continue;
            }

            if (t == "hundred")
            {
                current = (current == 0 ? 1 : current) * 100; // "a hundred" / "hundred"
                building = true;
                continue;
            }
            if (t == "thousand")
            {
                total += (current == 0 ? 1 : current) * 1000;
                current = 0;
                building = true;
                continue;
            }

            // "one hundred AND twenty", "A hundred"
            if ((t == "and" && building) || (t == "a" && i + 1 < tokens.Count && (tokens[i + 1] == "hundred" || tokens[i + 1] == "thousand")))
                continue;

            Flush();
        }
        Flush();
        return numbers;
    }

    public static bool IsYes(string text) => ContainsWord(text, "yes", "yeah", "yep", "yup", "sure", "correct", "affirmative", "launch", "fire", "go", "ok", "okay");
    public static bool IsNo(string text) => ContainsWord(text, "no", "nope", "nah", "wrong", "cancel", "incorrect");

    private static bool ContainsWord(string text, params string[] words)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (Match m in Regex.Matches(text.ToLowerInvariant(), "[a-z]+"))
            foreach (var w in words)
                if (m.Value == w) return true;
        return false;
    }
}
