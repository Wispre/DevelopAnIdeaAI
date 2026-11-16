using UnityEngine;

/// <summary>
/// The trigger circle created by <see cref="CircleSpawner"/>. Tells the spawner when a player touches it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SpawnedCircle : MonoBehaviour
{
    private CircleSpawner spawner;
    private string playerTag = "Player";

    internal void Init(CircleSpawner owner, string tagToDetect)
    {
        spawner = owner;
        playerTag = tagToDetect;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (spawner == null) return;

        // Accept the tag on the collider itself or on the Rigidbody2D it belongs to (e.g. collider on a child).
        bool isPlayer = other.CompareTag(playerTag) ||
                        (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag(playerTag));
        if (!isPlayer) return;

        // Tell the player first: once this circle hides itself, Unity stops sending trigger events for it.
        var launcher = other.attachedRigidbody != null
            ? other.attachedRigidbody.GetComponent<PlayerLauncher>()
            : other.GetComponent<PlayerLauncher>();
        if (launcher != null) launcher.NotifyHitTarget();

        spawner.OnCircleCollected(this);
        spawner.OnCircleCollected(this);
    }
}
