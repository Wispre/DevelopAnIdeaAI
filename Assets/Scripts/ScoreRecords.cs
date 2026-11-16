using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Saved scores (PlayerPrefs, kept between sessions) and the scores of the run in progress
/// (static, kept while moving between scenes).
/// A "run" starts at Level 1 and ends when the last level is won.
/// </summary>
public static class ScoreRecords
{
    private const string BestTotalKey = "BestRunTotal";
    private const string HistoryKey = "RunHistory";
    private const int HistoryLength = 5;

    // ---- Per-level saved scores ----

    private static string LastKey(LevelData l) => $"LevelScore_{l.name}_Last";
    private static string BestKey(LevelData l) => $"LevelScore_{l.name}_Best";

    public static void SaveLevelScore(LevelData level, int score)
    {
        PlayerPrefs.SetInt(LastKey(level), score);
        if (!PlayerPrefs.HasKey(BestKey(level)) || score > PlayerPrefs.GetInt(BestKey(level)))
            PlayerPrefs.SetInt(BestKey(level), score);
        PlayerPrefs.Save();
    }

    public static bool TryGetLevelScore(LevelData level, out int last, out int best)
    {
        last = PlayerPrefs.GetInt(LastKey(level), 0);
        best = PlayerPrefs.GetInt(BestKey(level), 0);
        return PlayerPrefs.HasKey(LastKey(level));
    }

    // ---- Current run ----

    /// <summary>Level names of the current run.</summary>
    public static string[] LevelNames { get; private set; } = new string[0];
    /// <summary>This run's score per level; -1 = not cleared yet.</summary>
    public static int[] RunScores { get; private set; } = new int[0];
    /// <summary>Best score per level before this run started; -1 = none.</summary>
    public static int[] PreviousBest { get; private set; } = new int[0];
    /// <summary>Best run total before this run started; -1 = none.</summary>
    public static int PreviousBestTotal { get; private set; } = -1;
    public static bool HasFinishedRun { get; private set; }

    public static int RunTotal => RunScores.Where(s => s >= 0).Sum();

    public static void StartRun(LevelData[] levels)
    {
        LevelNames = levels.Select(l => l.displayName).ToArray();
        RunScores = Enumerable.Repeat(-1, levels.Length).ToArray();
        PreviousBest = levels.Select(l => TryGetLevelScore(l, out _, out int best) ? best : -1).ToArray();
        PreviousBestTotal = PlayerPrefs.HasKey(BestTotalKey) ? PlayerPrefs.GetInt(BestTotalKey) : -1;
        HasFinishedRun = false;
    }

    public static void RecordWin(LevelData level, int levelIndex, int score)
    {
        SaveLevelScore(level, score);
        if (levelIndex >= 0 && levelIndex < RunScores.Length)
            RunScores[levelIndex] = score;
    }

    /// <summary>Stores the run total in the best-total and the recent-runs history.</summary>
    public static void FinishRun()
    {
        int total = RunTotal;
        if (!PlayerPrefs.HasKey(BestTotalKey) || total > PlayerPrefs.GetInt(BestTotalKey))
            PlayerPrefs.SetInt(BestTotalKey, total);

        var history = RecentRuns();
        history.Insert(0, total);
        if (history.Count > HistoryLength) history.RemoveRange(HistoryLength, history.Count - HistoryLength);
        PlayerPrefs.SetString(HistoryKey, string.Join(",", history));
        PlayerPrefs.Save();
        HasFinishedRun = true;
    }

    /// <summary>Totals of the most recent finished runs, newest first.</summary>
    public static List<int> RecentRuns()
    {
        var raw = PlayerPrefs.GetString(HistoryKey, "");
        return raw.Split(',').Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();
    }
}
