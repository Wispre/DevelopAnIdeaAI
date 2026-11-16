using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a scene when a spoken phrase matches, e.g. "Start" on the main menu or "Play again" at the end.
/// </summary>
public class VoiceSceneLoader : MonoBehaviour
{
    [Tooltip("Speech source. Found automatically if left empty.")]
    [SerializeField] private MicrophoneSpeechListener speech;
    [Tooltip("Regular expression (case-insensitive) the recognized text must contain.")]
    [SerializeField] private string commandPattern = @"\bstart\b";
    [Tooltip("Scene to load when the command is heard (must be in Build Profiles / Build Settings).")]
    [SerializeField] private string sceneToLoad = "Game";

    private bool loading;

    private void Awake()
    {
        if (speech == null) speech = FindFirstObjectByType<MicrophoneSpeechListener>();
    }

    private void OnEnable()
    {
        if (speech != null) speech.PhraseRecognized += OnPhrase;
    }

    private void OnDisable()
    {
        if (speech != null) speech.PhraseRecognized -= OnPhrase;
    }

    /// <summary>Public so tests or other input can drive it.</summary>
    public void OnPhrase(string text)
    {
        if (loading || string.IsNullOrEmpty(text)) return;
        if (!Regex.IsMatch(text, commandPattern, RegexOptions.IgnoreCase)) return;

        loading = true;
        Debug.Log($"[Menu] Heard \"{text}\". Loading {sceneToLoad}...");
        SceneManager.LoadScene(sceneToLoad);
    }
}
