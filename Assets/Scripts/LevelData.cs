using UnityEngine;

/// <summary>
/// One level: where the player starts and where the target sits. Each level is its own asset file.
/// Create more with Assets > Create > Launcher > Level.
/// </summary>
[CreateAssetMenu(fileName = "Level", menuName = "Launcher/Level")]
public class LevelData : ScriptableObject
{
    [Tooltip("Shown in the UI and Console.")]
    public string displayName = "Level";

    [Tooltip("World position of the player's square (its origin).")]
    public Vector2 playerPosition;

    [Tooltip("World position of the target's center.")]
    public Vector2 targetPosition;
}
