using System.Collections;
using UnityEngine;

/// <summary>
/// Voice-controlled launcher. The player (a square with a heavier circle on top, one Rigidbody2D)
/// waits with no physics at its level's start position. Say an angle (0 = right, counterclockwise),
/// a power, then confirm with "yes". The impulse is applied at the square's origin, so the heavier
/// head makes the player tumble. Points build up for every moment it flies without touching anything.
/// Out of bounds: the round's score is 0 and the player returns to its start immediately.
/// Hitting the target raises <see cref="Won"/>; the <see cref="GameManager"/> decides what happens next.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerLauncher : MonoBehaviour
{
    private enum State { WaitingForAngle, WaitingForPower, WaitingForConfirm, Flying, Won }

    /// <summary>Raised with the round's score when the player hits the target.</summary>
    public event System.Action<int> Won;

    [Header("References")]
    [Tooltip("Camera that defines the visible area. Defaults to Main Camera.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Live score display. Found automatically if left empty.")]
    [SerializeField] private ScoreBoard scoreBoard;
    [Tooltip("Where the launch impulse is applied: the square's origin.")]
    [SerializeField] private Transform forcePoint;

    [Header("Launch")]
    [Tooltip("Impulse = power x this multiplier x total mass, so launch speed in units/sec roughly equals power.")]
    [SerializeField] private float powerMultiplier = 1f;
    [SerializeField] private float maxPower = 100f;

    [Header("Score")]
    [Tooltip("Points earned per second of flight while not touching anything.")]
    [SerializeField] private float pointsPerSecond = 100f;

    [Header("Impact")]
    [Tooltip("Cinematic played on hit. Found on the main camera if left empty.")]
    [SerializeField] private ImpactCamera impactCamera;
    [Tooltip("Slow motion kicks in when the player is about to reach the target within this many seconds.")]
    [SerializeField] private float slowMoLookahead = 0.3f;
    [Tooltip("Game speed during the slow motion just before impact.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float slowMoScale = 0.2f;

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
    private bool slowMo;
    private float defaultFixedDeltaTime;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D triggerFilter;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        colliders = GetComponentsInChildren<Collider2D>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        trail = GetComponent<TrailRenderer>();
        if (forcePoint == null) forcePoint = transform;
        if (targetCamera == null) targetCamera = Camera.main;
        if (scoreBoard == null) scoreBoard = FindFirstObjectByType<ScoreBoard>();
        if (impactCamera == null && targetCamera != null) impactCamera = targetCamera.GetComponent<ImpactCamera>();
        defaultFixedDeltaTime = Time.fixedDeltaTime;
        triggerFilter = new ContactFilter2D { useTriggers = true };
        spawnPosition = transform.position;
        Freeze();
    }

    /// <summary>Start position for the current level.</summary>
    public void SetSpawn(Vector2 position) => spawnPosition = new Vector3(position.x, position.y, 0f);

    /// <summary>Feed recognized speech in (the GameManager forwards it here).</summary>
    public void HandlePhrase(string text)
    {
        if (state == State.Flying || state == State.Won) return;

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
        // Applied at the square's origin, not the center of mass (which sits higher, toward the heavy circle).
        body.AddForceAtPosition(direction * pendingPower * powerMultiplier * body.mass,
                                forcePoint.position, ForceMode2D.Impulse);

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
        {
            EndRoundWithoutWin("Stopped without reaching the target. Score reset to 0. Back to the start.");
            return;
        }

        SetSlowMotion(IsAboutToHitTarget());
    }

    /// <summary>Sweeps the player's shapes along its velocity to see if the target is just ahead.</summary>
    private bool IsAboutToHitTarget()
    {
        Vector2 v = body.linearVelocity;
        float speed = v.magnitude;
        if (speed < 0.01f) return false;

        // Lookahead is in real seconds, so it doesn't shrink once the game is slowed down.
        float distance = speed * slowMoLookahead;
        int count = body.Cast(v / speed, triggerFilter, castHits, distance);
        for (int i = 0; i < count; i++)
            if (castHits[i].collider.GetComponent<Target>() != null) return true;
        return false;
    }

    private void SetSlowMotion(bool on)
    {
        if (slowMo == on) return;
        slowMo = on;
        Time.timeScale = on ? slowMoScale : 1f;
        Time.fixedDeltaTime = defaultFixedDeltaTime * Time.timeScale; // keep physics smooth while slowed
    }

    private void OnDisable()
    {
        if (slowMo) SetSlowMotion(false);
    }

    // Collisions from the square or the circle both arrive here (they share this Rigidbody2D).
    private void OnCollisionEnter2D(Collision2D collision) => contactCount++;
    private void OnCollisionExit2D(Collision2D collision) => contactCount = Mathf.Max(0, contactCount - 1);

    /// <summary>Called by the <see cref="Target"/> when this player touches it.</summary>
    public void NotifyHitTarget(Collider2D targetCollider)
    {
        if (state != State.Flying) return;

        state = State.Won;
        int score = Mathf.FloorToInt(roundScore);
        StartCoroutine(HitRoutine(score, ContactPoint(targetCollider)));
    }

    private IEnumerator HitRoutine(int score, Vector2 contactPoint)
    {
        SetSlowMotion(false);

        // Freeze-frame + zoom on the impact point while both objects are still visible.
        if (impactCamera != null)
            yield return impactCamera.Play(contactPoint);

        Freeze();
        SetVisible(false);
        if (trail != null) trail.emitting = false;
        Won?.Invoke(score);
    }

    /// <summary>Where the player and the target touch: the closest points between their shapes.</summary>
    private Vector2 ContactPoint(Collider2D targetCollider)
    {
        Vector2 best = targetCollider != null ? (Vector2)targetCollider.bounds.center : body.worldCenterOfMass;
        if (targetCollider == null) return best;

        float bestDistance = float.MaxValue;
        foreach (var c in colliders)
        {
            if (!c.enabled) continue;
            var d = Physics2D.Distance(c, targetCollider);
            if (d.isValid && d.distance < bestDistance)
            {
                bestDistance = d.distance;
                best = (d.pointA + d.pointB) * 0.5f;
            }
        }
        return best;
    }

    private void EndRoundWithoutWin(string message)
    {
        roundScore = 0f;
        scoreBoard?.SetCurrent(0);
        Debug.Log($"[Launcher] {message}");
        ResetToSpawn(); // immediate, no pause
    }

    /// <summary>Put the player back at its start position, upright and frozen, and ask for an angle.</summary>
    public void ResetToSpawn()
    {
        StopAllCoroutines();
        SetSlowMotion(false);
        Freeze();
        transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        body.position = spawnPosition;
        body.rotation = 0f;
        SetVisible(true);
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
