using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns a stationary trigger circle at a random spot along the bottom of the camera view.
/// When an object tagged "Player" touches it, the circle disappears and respawns somewhere else
/// along the bottom, following the same rules.
/// </summary>
public class CircleSpawner : MonoBehaviour
{
    [Header("Circle")]
    [SerializeField] private Sprite circleSprite;
    [SerializeField] private Color circleColor = new Color(1f, 0.85f, 0.2f);
    [Tooltip("Circle diameter in world units.")]
    [SerializeField] private float diameter = 1f;

    [Header("Placement")]
    [Tooltip("Camera whose view defines the bottom of the screen. Defaults to Main Camera.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Gap between the circle's edge and the screen edges, in world units.")]
    [SerializeField] private float edgeMargin = 0.25f;
    [Tooltip("A respawn is at least this far (world units, horizontally) from the previous spot.")]
    [SerializeField] private float minRespawnDistance = 2f;

    [Header("Respawn")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("How long the circle stays hidden before reappearing.")]
    [SerializeField] private float respawnDelay = 0.5f;

    private SpawnedCircle circle;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null)
        {
            Debug.LogError("[CircleSpawner] No camera found to define the screen bottom.");
            return;
        }

        circle = CreateCircle();
        circle.transform.position = PickPosition(null);
    }

    private SpawnedCircle CreateCircle()
    {
        var go = new GameObject("Circle");
        go.transform.SetParent(transform, false);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = circleSprite != null ? circleSprite : BuildFallbackSprite();
        sr.color = circleColor;

        // Scale the sprite so it is exactly `diameter` world units wide.
        float spriteWidth = sr.sprite.bounds.size.x;
        go.transform.localScale = Vector3.one * (diameter / spriteWidth);

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = spriteWidth * 0.5f; // local units; scaled with the transform

        // Kinematic body: never moved by physics, but still reports triggers reliably
        // whether the player uses a dynamic or kinematic Rigidbody2D.
        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;

        var spawned = go.AddComponent<SpawnedCircle>();
        spawned.Init(this, playerTag);
        return spawned;
    }

    /// <summary>Called by the circle when a player touches it.</summary>
    internal void OnCircleCollected(SpawnedCircle collected)
    {
        StartCoroutine(Respawn(collected));
    }

    private IEnumerator Respawn(SpawnedCircle c)
    {
        Vector3 previous = c.transform.position;
        c.gameObject.SetActive(false);

        if (respawnDelay > 0f)
            yield return new WaitForSeconds(respawnDelay);

        c.transform.position = PickPosition(previous);
        c.gameObject.SetActive(true);
    }

    private Vector3 PickPosition(Vector3? previous)
    {
        float radius = diameter * 0.5f;
        float halfHeight = targetCamera.orthographicSize;
        float halfWidth = halfHeight * targetCamera.aspect;
        Vector3 camPos = targetCamera.transform.position;

        float minX = camPos.x - halfWidth + radius + edgeMargin;
        float maxX = camPos.x + halfWidth - radius - edgeMargin;
        float y = camPos.y - halfHeight + radius + edgeMargin;

        float x = Random.Range(minX, maxX);
        if (previous.HasValue)
        {
            // Try a few times to land far enough from the last spot (if the screen is wide enough).
            for (int i = 0; i < 20 && Mathf.Abs(x - previous.Value.x) < minRespawnDistance; i++)
                x = Random.Range(minX, maxX);
        }

        return new Vector3(x, y, 0f);
    }

    // Used only if no sprite is assigned: a plain white circle texture.
    private static Sprite BuildFallbackSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
