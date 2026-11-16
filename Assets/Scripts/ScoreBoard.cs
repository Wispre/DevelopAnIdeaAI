using TMPro;
using UnityEngine;

/// <summary>
/// Shows the last round's score, plus the live score while the player is in the air.
/// </summary>
public class ScoreBoard : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private int lastScore;
    private int? currentScore;

    private void Awake()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        Refresh();
    }

    public void SetLast(int score)
    {
        lastScore = score;
        Refresh();
    }

    /// <summary>Live score during flight; null hides it.</summary>
    public void SetCurrent(int? score)
    {
        if (currentScore == score) return;
        currentScore = score;
        Refresh();
    }

    private void Refresh()
    {
        if (label == null) return;
        label.text = currentScore.HasValue
            ? $"Last Score: {lastScore}\nScore: {currentScore.Value}"
            : $"Last Score: {lastScore}";
    }
}
