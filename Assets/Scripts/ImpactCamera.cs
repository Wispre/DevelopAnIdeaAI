using System.Collections;
using UnityEngine;

/// <summary>
/// Impact cinematic: freezes the game, shows a red ring-in-ring marker where the objects touched,
/// zooms the camera in fast and eases into the impact point, holds, then zooms back out and unfreezes.
/// Put it on the camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class ImpactCamera : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("Orthographic size when zoomed in (smaller = closer).")]
    [SerializeField] private float zoomedSize = 1.6f;
    [Tooltip("Zoom-in time. Starts fast, eases into the final spot.")]
    [SerializeField] private float zoomInSeconds = 0.35f;
    [Tooltip("How long everything stays frozen while zoomed in.")]
    [SerializeField] private float holdSeconds = 0.5f;
    [SerializeField] private float zoomOutSeconds = 0.4f;

    [Header("Impact marker")]
    [SerializeField] private Color markerColor = new Color(1f, 0.1f, 0.1f);
    [Tooltip("Marker diameter in world units.")]
    [SerializeField] private float markerSize = 0.9f;

    private Camera cam;
    private Transform marker;

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        marker = BuildMarker().transform;
        marker.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        // Never leave the game frozen.
        if (IsPlaying) Time.timeScale = 1f;
    }

    /// <summary>Run the whole cinematic. Uses unscaled time because the game is frozen meanwhile.</summary>
    public IEnumerator Play(Vector2 impactPoint)
    {
        IsPlaying = true;
        Time.timeScale = 0f; // freeze everything where it touched

        Vector3 startPos = transform.position;
        float startSize = cam.orthographicSize;
        Vector3 zoomPos = new Vector3(impactPoint.x, impactPoint.y, startPos.z);

        marker.position = new Vector3(impactPoint.x, impactPoint.y, 0f);
        marker.gameObject.SetActive(true);

        // Zoom in: fast at first, easing into the final spot.
        for (float t = 0f; t < zoomInSeconds; t += Time.unscaledDeltaTime)
        {
            float e = EaseOutExpo(t / zoomInSeconds);
            transform.position = Vector3.LerpUnclamped(startPos, zoomPos, e);
            cam.orthographicSize = Mathf.LerpUnclamped(startSize, zoomedSize, e);
            marker.localScale = Vector3.one * markerSize * Mathf.Lerp(1.6f, 1f, e); // marker snaps in
            yield return null;
        }
        transform.position = zoomPos;
        cam.orthographicSize = zoomedSize;
        marker.localScale = Vector3.one * markerSize;

        yield return new WaitForSecondsRealtime(holdSeconds);

        // Zoom back out to exactly where we were.
        for (float t = 0f; t < zoomOutSeconds; t += Time.unscaledDeltaTime)
        {
            float e = EaseInOutCubic(t / zoomOutSeconds);
            transform.position = Vector3.LerpUnclamped(zoomPos, startPos, e);
            cam.orthographicSize = Mathf.LerpUnclamped(zoomedSize, startSize, e);
            yield return null;
        }
        transform.position = startPos;
        cam.orthographicSize = startSize;

        marker.gameObject.SetActive(false);
        Time.timeScale = 1f;
        IsPlaying = false;
    }

    private static float EaseOutExpo(float x) => x >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * x);
    private static float EaseInOutCubic(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f;

    // Red crosshair-style target: an unfilled circle inside another unfilled circle.
    private GameObject BuildMarker()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        float c = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c; // 0 center .. 1 edge
            float outer = Ring(d, 0.93f, 0.06f);
            float inner = Ring(d, 0.45f, 0.06f);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(outer, inner)));
        }
        tex.Apply();

        var go = new GameObject("ImpactMarker");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sr.color = markerColor;
        sr.sortingOrder = 50;
        return go;
    }

    // Anti-aliased ring of the given radius and thickness (in normalized units).
    private static float Ring(float d, float radius, float thickness)
    {
        float edge = 0.012f;
        float half = thickness * 0.5f;
        return Mathf.Clamp01((half - Mathf.Abs(d - radius)) / edge);
    }
}
