using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Whisper;

/// <summary>
/// Records the microphone, cuts the audio into phrases when you pause, boosts quiet audio,
/// and transcribes each phrase offline with Whisper (whisper.unity). Prints the words to the Console.
/// Needs the model file at Assets/StreamingAssets/{modelPath}.
/// </summary>
public class MicrophoneSpeechListener : MonoBehaviour
{
    [Header("Whisper")]
    [Tooltip("Model file path inside Assets/StreamingAssets.")]
    [SerializeField] private string modelPath = "Whisper/ggml-base.en.bin";
    [Tooltip("Spoken language code, e.g. \"en\". .en models only support English.")]
    [SerializeField] private string language = "en";

    [Header("Microphone")]
    [Tooltip("Leave empty to use the default microphone.")]
    [SerializeField] private string microphoneDevice = "";
    [SerializeField] private float deviceWaitSeconds = 3f;

    [Header("Phrase detection")]
    [Tooltip("Speech must be this many times louder than the background noise.")]
    [SerializeField] private float speechToNoiseRatio = 3f;
    [Tooltip("Absolute minimum loudness (RMS) counted as speech. Lower it if your mic is very quiet.")]
    [SerializeField] private float minSpeechRms = 0.0006f;
    [Tooltip("Silence this long ends a phrase.")]
    [SerializeField] private float endSilenceSeconds = 0.8f;
    [Tooltip("Phrases shorter than this are ignored (clicks, bumps).")]
    [SerializeField] private float minPhraseSeconds = 0.3f;
    [Tooltip("Phrases are cut and sent after this long even without a pause.")]
    [SerializeField] private float maxPhraseSeconds = 15f;
    [Tooltip("Audio kept from just before speech started, so first syllables aren't lost.")]
    [SerializeField] private float preRollSeconds = 0.3f;

    [Header("Gain")]
    [Tooltip("Quiet phrases are amplified until their peak reaches this level (0..1).")]
    [SerializeField] private float targetPeak = 0.6f;
    [SerializeField] private float maxGain = 60f;

    [Header("Debug")]
    [Tooltip("Log mic level, noise floor and phrase start/stop.")]
    [SerializeField] private bool verboseLogging = false;

    /// <summary>Raised on the main thread with the cleaned text of every recognized phrase.</summary>
    public event System.Action<string> PhraseRecognized;

    private const int SampleRate = 16000;
    private const int FrameSamples = 320; // 20 ms
    private const int ClipSeconds = 10;

    private WhisperManager whisper;
    private AudioClip micClip;
    private string device;
    private int readPosition;
    private readonly float[] frame = new float[FrameSamples];

    private float noiseFloor = -1f;
    private bool inSpeech;
    private int silentFrames;
    private readonly List<float> phrase = new List<float>();
    private readonly Queue<float[]> preRoll = new Queue<float[]>();
    private readonly Queue<float[]> pendingPhrases = new Queue<float[]>();
    private bool transcribing;
    private float nextVerboseLog;

    private IEnumerator Start()
    {
        CreateWhisper();

        float waited = 0f;
        while (Microphone.devices.Length == 0 && waited < deviceWaitSeconds)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            waited += 0.25f;
        }

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[Speech] No microphone found. Plug one in and press Play again.");
            yield break;
        }

        device = string.IsNullOrEmpty(microphoneDevice) ? Microphone.devices[0] : microphoneDevice;
        micClip = Microphone.Start(device, true, ClipSeconds, SampleRate);
        if (micClip == null)
        {
            Debug.LogError($"[Speech] Could not open microphone \"{device}\".");
            yield break;
        }

        // Wait until the mic actually delivers samples.
        float t = 0f;
        while (Microphone.GetPosition(device) <= 0 && t < 2f)
        {
            yield return null;
            t += Time.unscaledDeltaTime;
        }
        readPosition = Microphone.GetPosition(device);

        Debug.Log($"[Speech] Listening on \"{device}\" with Whisper ({modelPath}). Speak, then pause briefly.");
    }

    private void CreateWhisper()
    {
        // Created inactive so the model path is set before WhisperManager.Awake loads the model.
        var go = new GameObject("Whisper");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        whisper = go.AddComponent<WhisperManager>();
        whisper.ModelPath = modelPath;
        whisper.IsModelPathInStreamingAssets = true;
        whisper.language = language;
        whisper.noContext = true;
        whisper.singleSegment = false;
        go.SetActive(true);
    }

    private void Update()
    {
        if (micClip == null) return;

        int position = Microphone.GetPosition(device);
        int clipLength = micClip.samples;
        int available = (position - readPosition + clipLength) % clipLength;

        while (available >= FrameSamples)
        {
            micClip.GetData(frame, readPosition); // wraps around for looping clips
            readPosition = (readPosition + FrameSamples) % clipLength;
            available -= FrameSamples;
            ProcessFrame(frame);
        }
    }

    private void ProcessFrame(float[] samples)
    {
        float sum = 0f;
        for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
        float rms = Mathf.Sqrt(sum / samples.Length);

        if (noiseFloor < 0f) noiseFloor = rms;
        float threshold = Mathf.Max(noiseFloor * speechToNoiseRatio, minSpeechRms);
        bool isSpeech = rms > threshold;

        // Track background noise only while nobody is talking.
        if (!inSpeech)
            noiseFloor = rms < noiseFloor ? Mathf.Lerp(noiseFloor, rms, 0.2f) : Mathf.Lerp(noiseFloor, rms, 0.01f);

        if (verboseLogging && Time.realtimeSinceStartup >= nextVerboseLog)
        {
            nextVerboseLog = Time.realtimeSinceStartup + 2f;
            Debug.Log($"[Speech] level {rms:F4}, noise {noiseFloor:F4}, threshold {threshold:F4}");
        }

        var copy = (float[])samples.Clone();
        int preRollFrames = Mathf.CeilToInt(preRollSeconds * SampleRate / FrameSamples);

        if (!inSpeech)
        {
            preRoll.Enqueue(copy);
            while (preRoll.Count > preRollFrames) preRoll.Dequeue();

            if (isSpeech)
            {
                inSpeech = true;
                silentFrames = 0;
                phrase.Clear();
                foreach (var f in preRoll) phrase.AddRange(f);
                preRoll.Clear();
                if (verboseLogging) Debug.Log("[Speech] phrase started");
            }
            return;
        }

        phrase.AddRange(copy);
        silentFrames = isSpeech ? 0 : silentFrames + 1;

        float silentSeconds = silentFrames * FrameSamples / (float)SampleRate;
        float phraseSeconds = phrase.Count / (float)SampleRate;
        if (silentSeconds >= endSilenceSeconds || phraseSeconds >= maxPhraseSeconds)
            EndPhrase(phraseSeconds - silentSeconds);
    }

    private void EndPhrase(float spokenSeconds)
    {
        inSpeech = false;
        silentFrames = 0;
        if (verboseLogging) Debug.Log($"[Speech] phrase ended ({spokenSeconds:F1}s of speech)");

        if (spokenSeconds >= minPhraseSeconds)
        {
            pendingPhrases.Enqueue(Amplify(phrase.ToArray()));
            _ = TranscribeQueue();
        }
        phrase.Clear();
    }

    private float[] Amplify(float[] samples)
    {
        float peak = 0f;
        foreach (var s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak <= 0f) return samples;

        float gain = Mathf.Min(targetPeak / peak, maxGain);
        if (gain <= 1f) return samples;
        for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
        if (verboseLogging) Debug.Log($"[Speech] amplified phrase x{gain:F1}");
        return samples;
    }

    private async Task TranscribeQueue()
    {
        if (transcribing) return;
        transcribing = true;
        try
        {
            while (pendingPhrases.Count > 0)
            {
                var samples = pendingPhrases.Dequeue();
                var result = await whisper.GetTextAsync(samples, SampleRate, 1);
                if (this == null) return; // object destroyed while waiting

                var text = Clean(result?.Result);
                if (!string.IsNullOrEmpty(text))
                {
                    Debug.Log($"[Speech] Heard: \"{text}\"");
                    PhraseRecognized?.Invoke(text);
                }
                else if (verboseLogging)
                    Debug.Log("[Speech] (no words recognized in that phrase)");
            }
        }
        finally
        {
            transcribing = false;
        }
    }

    // Whisper marks non-speech with tags like [BLANK_AUDIO] or (wind blowing); drop those.
    private static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", "").Trim();
        return text.Length == 0 ? null : text;
    }

    private void OnDestroy()
    {
        if (micClip != null && Microphone.IsRecording(device))
            Microphone.End(device);
        micClip = null;
    }
}
