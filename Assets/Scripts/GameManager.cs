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

    [Header("End of the game")]
    [Tooltip("Scene shown after the last level is won.")]
    [SerializeField] private string endGameScene = "EndGame";
    [Tooltip("Seconds the 'all levels done' message stays before the end scene loads.")]
    [SerializeField] private float endGameDelay = 2f;

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
        ScoreRecords.StartRun(levels);
        LoadLevel(0);
    }

    /// <summary>All speech goes through here. Public so tests or other input can drive it.</summary>
    public void OnPhrase(string text)
    {
        if (showingWin)
        {
            if (!awaitingChoice) return; // still showing the win message

            if (SpokenNumbers.IsRetry(text)) LoadLevel(levelIndex);
            else if (SpokenNumbers.IsYes(text)) LoadLevel(levelIndex + 1);
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

        ScoreRecords.RecordWin(Current, levelIndex, score);
        RefreshScoreBoard();
        Debug.Log($"WIN on {Current.displayName} (score {score})");

        SetMessage(winMessage, winText);
        Debug.Log(winText);

        yield return new WaitForSeconds(promptDelay);

        if (IsLastLevel)
        {
            // All levels done: save the run and go to the end screen.
            ScoreRecords.FinishRun();
            const string done = "You finished every level!";
            SetMessage(continuePrompt, done);
            Debug.Log($"[Game] {done} Run total {ScoreRecords.RunTotal}.");
            yield return new WaitForSeconds(endGameDelay);
            UnityEngine.SceneManagement.SceneManager.LoadScene(endGameScene);
            yield break;
        }

        string prompt = PromptText();
        SetMessage(continuePrompt, prompt);
        Debug.Log($"[Game] {prompt.Replace("\n", " ")}");
        awaitingChoice = true;
    }

    private static string PromptText() =>
        "Continue to the next level?\nSay \"Yes\" to continue or \"Retry\" to replay this level.";

    private string LevelLabel() => $"Level {levelIndex + 1}/{levels.Length}: {Current.displayName}";

    private void RefreshScoreBoard()
    {
        if (scoreBoard == null) return;
        var sb = new StringBuilder("Level scores:");
        for (int i = 0; i < levels.Length; i++)
        {
            sb.Append($"\nLevel {i + 1}: ");
            sb.Append(ScoreRecords.TryGetLevelScore(levels[i], out int last, out int best) ? $"{last} (best {best})" : "-");
        }
        scoreBoard.SetLevelInfo(LevelLabel(), sb.ToString());
    }

    private static void SetMessage(TMP_Text label, string text)
    {
        if (label == null) return;
        label.text = text ?? "";
        label.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }
}
