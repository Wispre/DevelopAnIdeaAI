using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Runs the level-based game: loads each level's player/target positions, routes speech,
/// shows the win message and the continue prompt, and saves each level's score.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Levels (played in order)")]
    [SerializeField] private LevelData[] levels;

    [Header("Scene references")]
    [SerializeField] private PlayerLauncher player;
    [SerializeField] private Target target;
    [SerializeField] private MicrophoneSpeechListener speech;
    [SerializeField] private ScoreBoard scoreBoard;
    [SerializeField] private TMP_Text winMessage;
    [SerializeField] private TMP_Text continuePrompt;
    [SerializeField] private GameObject explosionPrefab;

    [Header("Win screen")]
    [SerializeField] private string winText = "Ayy, I see what you did there :)";
    [Tooltip("Seconds between the win message and the continue question.")]
    [SerializeField] private float promptDelay = 1f;

    private int levelIndex;
    private bool showingWin;
    private bool awaitingChoice;

    private LevelData Current => levels[levelIndex];
    private bool IsLastLevel => levelIndex == levels.Length - 1;

    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerLauncher>();
        if (target == null) target = FindFirstObjectByType<Target>(FindObjectsInactive.Include);
        if (speech == null) speech = FindFirstObjectByType<MicrophoneSpeechListener>();
        if (scoreBoard == null) scoreBoard = FindFirstObjectByType<ScoreBoard>();
    }

    private void OnEnable()
    {
        if (speech != null) speech.PhraseRecognized += OnPhrase;
        if (player != null) player.Won += OnWin;
    }

    private void OnDisable()
    {
        if (speech != null) speech.PhraseRecognized -= OnPhrase;
        if (player != null) player.Won -= OnWin;
    }

    private void Start()
    {
        if (levels == null || levels.Length == 0)
        {
            Debug.LogError("[Game] No levels assigned to the GameManager.");
            return;
        }
        LoadLevel(0);
    }

    /// <summary>All speech goes through here. Public so tests or other input can drive it.</summary>
    public void OnPhrase(string text)
    {
        if (showingWin)
        {
            if (!awaitingChoice) return; // still showing the win message

            if (SpokenNumbers.IsRetry(text)) LoadLevel(levelIndex);
            else if (SpokenNumbers.IsYes(text)) LoadLevel((levelIndex + 1) % levels.Length);
            else Debug.Log($"[Game] {PromptText().Replace("\n", " ")}");
            return;
        }

        player.HandlePhrase(text);
    }

    private void LoadLevel(int index)
    {
        StopAllCoroutines();
        levelIndex = index;
        showingWin = awaitingChoice = false;
        SetMessage(winMessage, null);
        SetMessage(continuePrompt, null);

        target.Place(Current.targetPosition);
        player.SetSpawn(Current.playerPosition);
        scoreBoard?.SetCurrent(null);
        RefreshScoreBoard();

        Debug.Log($"[Game] {LevelLabel()}");
        player.ResetToSpawn();
    }

    private void OnWin(int score)
    {
        StartCoroutine(WinRoutine(score));
    }

    private IEnumerator WinRoutine(int score)
    {
        showingWin = true;
        awaitingChoice = false;

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, target.transform.position, Quaternion.identity);
        target.Hide();

        SaveScore(Current, score);
        RefreshScoreBoard();
        Debug.Log($"WIN on {Current.displayName} (score {score})");

        SetMessage(winMessage, winText);
        Debug.Log(winText);

        yield return new WaitForSeconds(promptDelay);

        string prompt = PromptText();
        SetMessage(continuePrompt, prompt);
        Debug.Log($"[Game] {prompt.Replace("\n", " ")}");
        awaitingChoice = true;
    }

    private string PromptText() => IsLastLevel
        ? "That was the last level! Play again from Level 1?\nSay \"Yes\" to continue or \"Retry\" to replay this level."
        : "Continue to the next level?\nSay \"Yes\" to continue or \"Retry\" to replay this level.";

    private string LevelLabel() => $"Level {levelIndex + 1}/{levels.Length}: {Current.displayName}";

    private void RefreshScoreBoard()
    {
        if (scoreBoard == null) return;
        var sb = new StringBuilder("Level scores:");
        for (int i = 0; i < levels.Length; i++)
        {
            sb.Append($"\nLevel {i + 1}: ");
            sb.Append(TryGetSavedScore(levels[i], out int last, out int best) ? $"{last} (best {best})" : "-");
        }
        scoreBoard.SetLevelInfo(LevelLabel(), sb.ToString());
    }

    private static void SetMessage(TMP_Text label, string text)
    {
        if (label == null) return;
        label.text = text ?? "";
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    // ---- Saved scores (PlayerPrefs, kept between play sessions) ----

    private static string LastKey(LevelData l) => $"LevelScore_{l.name}_Last";
    private static string BestKey(LevelData l) => $"LevelScore_{l.name}_Best";

    private static void SaveScore(LevelData level, int score)
    {
        PlayerPrefs.SetInt(LastKey(level), score);
        if (score > PlayerPrefs.GetInt(BestKey(level), int.MinValue))
            PlayerPrefs.SetInt(BestKey(level), score);
        PlayerPrefs.Save();
    }

    public static bool TryGetSavedScore(LevelData level, out int last, out int best)
    {
        last = PlayerPrefs.GetInt(LastKey(level), 0);
        best = PlayerPrefs.GetInt(BestKey(level), 0);
        return PlayerPrefs.HasKey(LastKey(level));
    }
}
