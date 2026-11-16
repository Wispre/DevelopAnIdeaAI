using UnityEngine;

/// <summary>
/// The stationary trigger the player has to hit. Placed by <see cref="GameManager"/> for each level.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Target : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";

    public void Place(Vector2 position)
    {
        transform.position = new Vector3(position.x, position.y, 0f);
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    private void OnTriggerEnter2D(Collider2D other)
    {
        // The player is several colliders (square + circle) on one Rigidbody2D; check the body.
        var body = other.attachedRigidbody;
        if (body == null || !body.CompareTag(playerTag)) return;

        var launcher = body.GetComponent<PlayerLauncher>();
        if (launcher != null) launcher.NotifyHitTarget(GetComponent<Collider2D>());
    }
}
