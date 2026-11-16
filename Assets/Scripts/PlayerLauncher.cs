using UnityEngine;

/// <summary>
/// Voice-controlled launcher. The player waits (no physics) at a random X on screen with Y = 0.
/// Say an angle (0 = right, counterclockwise), then a power, then confirm with "yes".
/// The player is then launched with an impulse. Out of bounds: back to the same spot.
/// Hitting the target circle: "WIN" and a new random spot.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerLauncher : MonoBehaviour
{
    private enum State { WaitingForAngle, WaitingForPower, WaitingForConfirm, Flying }

    [Header("References")]
    [Tooltip("Speech source. Found automatically if left empty.")]
    [SerializeField] private MicrophoneSpeechListener speech;
    [Tooltip("Camera that defines the visible area. Defaults to Main Camera.")]
    [SerializeField] private Camera targetCamera;

    [Header("Spawning")]
    [Tooltip("Gap between the player and the left/right screen edges when picking a spawn X.")]
    [SerializeField] private float edgeMargin = 0.5f;

    [Header("Launch")]
    [Tooltip("Impulse = power x this multiplier (x mass), so speed in units/sec roughly equals power.")]
    [SerializeField] private float powerMultiplier = 1f;
    [SerializeField] private float maxPower = 100f;

    [Header("Out of bounds")]
    [Tooltip("How far past the screen edge the player may go before it counts as out of bounds. " +
             "Going above the top is allowed, since gravity brings it back.")]
    [SerializeField] private float boundsMargin = 0.5f;
    [Tooltip("If the player sits still this long after launch, it is reset.")]
    [SerializeField] private float stuckSeconds = 2f;

    private Rigidbody2D body;
    private Vector3 spawnPosition;
    private State state;
    private float pendingAngle;
    private float pendingPower;
    private float stillTimer;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        if (targetCamera == null) targetCamera = Camera.main;
        if (speech == null) speech = FindFirstObjectByType<MicrophoneSpeechListener>();
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
        if (state == State.Flying) return;

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

        stillTimer = 0f;
        state = State.Flying;
        Debug.Log($"[Launcher] Launched at {Format(pendingAngle)}° with {Format(pendingPower)} power!");
    }

    private void FixedUpdate()
    {
        if (state != State.Flying) return;

        if (IsOutOfBounds(transform.position))
        {
            Debug.Log("[Launcher] Out of bounds. Back to the start.");
            ResetToSpawn();
            return;
        }

        stillTimer = body.linearVelocity.sqrMagnitude < 0.01f ? stillTimer + Time.fixedDeltaTime : 0f;
        if (stillTimer >= stuckSeconds)
        {
            Debug.Log("[Launcher] Stopped without reaching the target. Back to the start.");
            ResetToSpawn();
        }
    }

    /// <summary>
    /// Called by the target circle when this player touches it. (Done from the circle's side because the
    /// circle hides itself on contact, which would stop Unity from sending the player its own trigger event.)
    /// </summary>
    public void NotifyHitTarget()
    {
        if (state != State.Flying) return;

        Debug.Log("WIN");
        spawnPosition = PickSpawnPosition();
        ResetToSpawn();
    }

    private void ResetToSpawn()
    {
        // No physics while waiting: kinematic bodies don't fall or get pushed.
        body.bodyType = RigidbodyType2D.Kinematic;
        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        body.position = spawnPosition;
        body.rotation = 0f;
        AskForAngle();
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
