using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Fills the end screen with this run's scores and the high scores from before the run.
/// </summary>
public class EndGameScreen : MonoBehaviour
{
    [SerializeField] private TMP_Text runScoresText;
    [SerializeField] private TMP_Text highScoresText;
    [SerializeField] private string newBestTag = "<color=#FFD84D>  NEW BEST!</color>";

    private void Start()
    {
        if (!ScoreRecords.HasFinishedRun || ScoreRecords.LevelNames.Length == 0)
        {
            if (runScoresText != null) runScoresText.text = "<b>Your run</b>\n\nNo run finished yet.\nPlay through all the levels first!";
            if (highScoresText != null) highScoresText.text = HighScoresWithoutRun();
            return;
        }

        var names = ScoreRecords.LevelNames;
        var run = ScoreRecords.RunScores;
        var prev = ScoreRecords.PreviousBest;

        var r = new StringBuilder("<b>Your run</b>\n\n");
        for (int i = 0; i < names.Length; i++)
        {
            r.Append($"Level {i + 1} - {names[i]}: {Show(run[i])}");
            if (run[i] >= 0 && run[i] > prev[i]) r.Append(newBestTag);
            r.Append('\n');
        }
        int total = ScoreRecords.RunTotal;
        r.Append($"\n<b>Total: {total}</b>");
        if (total > ScoreRecords.PreviousBestTotal) r.Append(newBestTag);

        var recent = ScoreRecords.RecentRuns();
        if (recent.Count > 1)
        {
            r.Append("\n\n<size=80%>Recent runs: ");
            r.Append(string.Join(", ", recent));
            r.Append("</size>");
        }

        var h = new StringBuilder("<b>Previous high scores</b>\n\n");
        for (int i = 0; i < names.Length; i++)
            h.Append($"Level {i + 1}: {Show(prev[i])}\n");
        h.Append($"\n<b>Best run: {Show(ScoreRecords.PreviousBestTotal)}</b>");

        if (runScoresText != null) runScoresText.text = r.ToString();
        if (highScoresText != null) highScoresText.text = h.ToString();
    }

    private static string HighScoresWithoutRun()
    {
        var recent = ScoreRecords.RecentRuns();
        return "<b>Previous high scores</b>\n\n" +
               (recent.Count > 0 ? $"Recent runs: {string.Join(", ", recent)}" : "None yet");
    }

    private static string Show(int score) => score >= 0 ? score.ToString() : "-";
}
