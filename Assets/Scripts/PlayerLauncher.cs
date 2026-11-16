using System.Collections;
using UnityEngine;

/// <summary>
/// Voice-controlled launcher. The player waits (no physics) at a random X on screen with Y = 0.
/// Say an angle (0 = right, counterclockwise), then a power, then confirm with "yes".
/// The player is then launched with an impulse and leaves a trail.
/// Score: points build up for every moment the player is airborne without touching anything.
/// Out of bounds: score for the round is 0 and the player returns to the same spot immediately.
/// Hitting the target circle: explosion, "WIN", then after a short pause a new random spot.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerLauncher : MonoBehaviour
{
    private enum State { WaitingForAngle, WaitingForPower, WaitingForConfirm, Flying, Celebrating }

    [Header("References")]
    [Tooltip("Speech source. Found automatically if left empty.")]
    [SerializeField] private MicrophoneSpeechListener speech;
    [Tooltip("Camera that defines the visible area. Defaults to Main Camera.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Score text. Found automatically if left empty.")]
    [SerializeField] private ScoreBoard scoreBoard;
    [Tooltip("Particle effect spawned when the player hits the target.")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("Spawning")]
    [Tooltip("Gap between the player and the left/right screen edges when picking a spawn X.")]
    [SerializeField] private float edgeMargin = 0.5f;
    [Tooltip("Pause after hitting the target before the player respawns.")]
    [SerializeField] private float winRespawnDelay = 2f;

    [Header("Launch")]
    [Tooltip("Impulse = power x this multiplier (x mass), so speed in units/sec roughly equals power.")]
    [SerializeField] private float powerMultiplier = 1f;
    [SerializeField] private float maxPower = 100f;

    [Header("Score")]
    [Tooltip("Points earned per second of flight while not touching anything.")]
    [SerializeField] private float pointsPerSecond = 100f;

    [Header("Out of bounds")]
    [Tooltip("How far past the screen edge the player may go before it counts as out of bounds. " +
             "Going above the top is allowed, since gravity brings it back.")]
    [SerializeField] private float boundsMargin = 0.5f;
    [Tooltip("If the player sits still this long after launch, it is reset.")]
    [SerializeField] private float stuckSeconds = 2f;

    private Rigidbody2D body;
    private Collider2D[] colliders;
    private Renderer[] renderers;
    private TrailRenderer trail;
    private Vector3 spawnPosition;
    private State state;
    private float pendingAngle;
    private float pendingPower;
    private float stillTimer;
    private float roundScore;
    private int contactCount;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        colliders = GetComponents<Collider2D>();
        renderers = GetComponentsInChildren<Renderer>();
        trail = GetComponent<TrailRenderer>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (speech == null) speech = FindFirstObjectByType<MicrophoneSpeechListener>();
        if (scoreBoard == null) scoreBoard = FindFirstObjectByType<ScoreBoard>();
    }

    private void OnEnable()
    {
        if (speech != null) speech.PhraseRecognized += HandlePhrase;
    }

    private void OnDisable()
    {
        if (speech != null) speech.PhraseRecognized -= HandlePhrase;
    }

    private void Start()
    {
        if (speech == null)
            Debug.LogWarning("[Launcher] No MicrophoneSpeechListener in the scene; voice commands won't work.");

        spawnPosition = PickSpawnPosition();
        ResetToSpawn();
    }

    /// <summary>Feed recognized speech in. Public so it can also be driven by tests or other input.</summary>
    public void HandlePhrase(string text)
    {
        if (state == State.Flying || state == State.Celebrating) return;

        var numbers = SpokenNumbers.Extract(text);

        switch (state)
        {
            case State.WaitingForAngle:
                if (numbers.Count == 0)
                {
                    Debug.Log("[Launcher] I didn't catch an angle. Say a number of degrees (0 = right, 90 = up).");
                    return;
                }
                AcceptAngle(numbers);
                break;

            case State.WaitingForPower:
                if (numbers.Count == 0)
                {
                    Debug.Log("[Launcher] I didn't catch the power. Say a number.");
                    return;
                }
                SetPower(numbers[0]);
                break;

            case State.WaitingForConfirm:
                // Only an explicit yes launches. Anything else keeps listening and asks again.
                if (SpokenNumbers.IsYes(text) && !SpokenNumbers.IsNo(text) && numbers.Count == 0)
                {
                    Launch();
                }
                else if (numbers.Count > 0)
                {
                    // New values said directly, e.g. "No, 60 degrees power 20" or just "60 degrees".
                    AcceptAngle(numbers);
                }
                else if (SpokenNumbers.IsNo(text))
                {
                    state = State.WaitingForAngle;
                    Debug.Log("[Launcher] OK. Say the new angle (you can say the power in the same sentence).");
                }
                else
                {
                    AskConfirm(); // didn't understand: repeat the question
                }
                break;
        }
    }

    /// <summary>First number = angle; a second number in the same phrase = power. Ends at the confirmation question.</summary>
    private void AcceptAngle(System.Collections.Generic.List<float> numbers)
    {
        pendingAngle = Mathf.Repeat(numbers[0], 360f);
        if (numbers.Count >= 2)
        {
            // Both said in one breath: "45 degrees, power 10".
            SetPower(numbers[1]);
            return;
        }
        state = State.WaitingForPower;
        Debug.Log($"[Launcher] Angle {Format(pendingAngle)}°. Now say the power.");
    }

    private void SetPower(float power)
    {
        pendingPower = Mathf.Clamp(Mathf.Abs(power), 0f, maxPower);
        state = State.WaitingForConfirm;
        AskConfirm();
    }

    private void AskConfirm()
    {
        Debug.Log($"Launch at {Format(pendingAngle)} degrees with {Format(pendingPower)} power?");
    }

    private void AskForAngle(string prefix = null)
    {
        state = State.WaitingForAngle;
        Debug.Log($"[Launcher] {(prefix != null ? prefix + " " : "")}Say the launch angle in degrees (0 = right, counterclockwise).");
    }

    private void Launch()
    {
        float rad = pendingAngle * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        body.bodyType = RigidbodyType2D.Dynamic;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.AddForce(direction * pendingPower * powerMultiplier * body.mass, ForceMode2D.Impulse);

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }

        stillTimer = 0f;
        roundScore = 0f;
        contactCount = 0;
        scoreBoard?.SetCurrent(0);
        state = State.Flying;
        Debug.Log($"[Launcher] Launched at {Format(pendingAngle)}° with {Format(pendingPower)} power!");
    }

    private void FixedUpdate()
    {
        if (state != State.Flying) return;

        if (IsOutOfBounds(transform.position))
        {
            EndRoundWithoutWin("Out of bounds. Score reset to 0. Back to the start.");
            return;
        }

        // Airtime score: only while not touching anything.
        if (contactCount == 0)
        {
            roundScore += pointsPerSecond * Time.fixedDeltaTime;
            scoreBoard?.SetCurrent(Mathf.FloorToInt(roundScore));
        }

        stillTimer = body.linearVelocity.sqrMagnitude < 0.01f ? stillTimer + Time.fixedDeltaTime : 0f;
        if (stillTimer >= stuckSeconds)
            EndRoundWithoutWin("Stopped without reaching the target. Score reset to 0. Back to the start.");
    }

    private void OnCollisionEnter2D(Collision2D collision) => contactCount++;
    private void OnCollisionExit2D(Collision2D collision) => contactCount = Mathf.Max(0, contactCount - 1);

    /// <summary>
    /// Called by the target circle when this player touches it. (Done from the circle's side because the
    /// circle hides itself on contact, which would stop Unity from sending the player its own trigger event.)
    /// </summary>
    public void NotifyHitTarget()
    {
        if (state != State.Flying) return;
        StartCoroutine(WinRoutine());
    }

    private IEnumerator WinRoutine()
    {
        state = State.Celebrating;
        int score = Mathf.FloorToInt(roundScore);
        scoreBoard?.SetLast(score);
        scoreBoard?.SetCurrent(null);
        Debug.Log($"WIN (score {score})");

        if (explosionPrefab != null)
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);

        // Hide and freeze the player during the pause.
        Freeze();
        SetVisible(false);
        if (trail != null) trail.emitting = false;

        yield return new WaitForSeconds(winRespawnDelay);

        spawnPosition = PickSpawnPosition();
        SetVisible(true);
        ResetToSpawn();
    }

    private void EndRoundWithoutWin(string message)
    {
        roundScore = 0f;
        scoreBoard?.SetLast(0);
        scoreBoard?.SetCurrent(null);
        Debug.Log($"[Launcher] {message}");
        ResetToSpawn(); // immediate, no pause
    }

    private void ResetToSpawn()
    {
        Freeze();
        transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        body.position = spawnPosition;
        body.rotation = 0f;
        if (trail != null)
        {
            trail.emitting = false;
            trail.Clear();
        }
        contactCount = 0;
        AskForAngle();
    }

    // No physics while waiting: kinematic bodies don't fall or get pushed.
    private void Freeze()
    {
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in renderers)
            if (r != trail) r.enabled = visible;
        foreach (var c in colliders) c.enabled = visible;
    }

    private Vector3 PickSpawnPosition()
    {
        float halfWidth = targetCamera.orthographicSize * targetCamera.aspect;
        float camX = targetCamera.transform.position.x;
        float x = Random.Range(camX - halfWidth + edgeMargin, camX + halfWidth - edgeMargin);
        return new Vector3(x, 0f, 0f);
    }

    private bool IsOutOfBounds(Vector3 p)
    {
        float halfHeight = targetCamera.orthographicSize;
        float halfWidth = halfHeight * targetCamera.aspect;
        Vector3 c = targetCamera.transform.position;
        return p.x < c.x - halfWidth - boundsMargin ||
               p.x > c.x + halfWidth + boundsMargin ||
               p.y < c.y - halfHeight - boundsMargin;
    }

    private static string Format(float v) => v.ToString("0.##");
}
