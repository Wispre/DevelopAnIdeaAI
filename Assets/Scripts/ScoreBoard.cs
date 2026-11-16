using TMPro;
using UnityEngine;

/// <summary>
/// Top-left HUD: current level, the live score for this attempt, and each level's saved score.
/// </summary>
public class ScoreBoard : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private string levelLine = "";
    private string savedScores = "";
    private int? currentScore;

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        Refresh();
    }

    public void SetLevelInfo(string level, string scores)
    {
        levelLine = level;
        savedScores = scores;
        Refresh();
    }

    /// <summary>Live score during flight; null shows 0.</summary>
    public void SetCurrent(int? score)
    {
        if (currentScore == score) return;
        currentScore = score;
        Refresh();
    }

    private void Refresh()
    {
        if (label == null) return;
        label.text = $"{levelLine}\nScore: {currentScore ?? 0}\n\n<size=75%>{savedScores}</size>";
    }
}
