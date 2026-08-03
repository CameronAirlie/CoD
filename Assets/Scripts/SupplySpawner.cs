using System.Numerics;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Populates configured arena points with alternating health and ammo supplies.</summary>
public sealed class SupplySpawner : ScriptBehaviour
{
    [SerializedField] private string healthPrefab = "project://Prefabs/HealthPickup.plutoprefab";
    [SerializedField] private string ammoPrefab = "project://Prefabs/AmmoPickup.plutoprefab";
    [SerializedField] private string armourPrefab = "project://Prefabs/ArmourPickup.plutoprefab";
    [SerializedField] private GameObject? pointA = null;
    [SerializedField] private GameObject? pointB = null;
    [SerializedField] private GameObject? pointC = null;
    [SerializedField] private GameObject? pointD = null;
    [SerializedField] private GameObject? pointE = null;
    [SerializedField] private GameObject? pointF = null;
    [SerializedField] private GameObject? pointG = null;
    [SerializedField] private GameObject? pointH = null;

    [SerializedField] private float spawnDelay = 5f;
    private readonly List<PendingSpawn> _pending = [];

    public override void OnCreate()
    {
        GameObject?[] points = [pointA, pointB, pointC, pointD, pointE, pointF, pointG, pointH];
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            if (point is null || !point.IsValid)
                continue;

            var prefab = (index % 3) switch
            {
                0 => healthPrefab,
                1 => ammoPrefab,
                _ => armourPrefab,
            };
            if (string.IsNullOrWhiteSpace(prefab) ||
                Prefab.Instantiate(prefab, point.WorldPosition, point.WorldRotation) is null)
            {
                Debug.LogWarning($"Could not spawn supply at {point.Name}.");
            }
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        for (var index = _pending.Count - 1; index >= 0; index--)
        {
            var pending = _pending[index];
            pending.TimeRemaining -= MathF.Max(0.0f, deltaTime);
            if (pending.TimeRemaining > 0.0f)
            {
                _pending[index] = pending;
                continue;
            }
            if (Prefab.Instantiate(pending.Prefab, pending.Position, pending.Rotation) is null)
                Debug.LogWarning($"Could not respawn supply '{pending.Prefab}'.");
            _pending.RemoveAt(index);
        }
    }

    public void PickupConsumed(string prefab, Vector3 position, Vector3 rotation)
    {
        if (!string.IsNullOrWhiteSpace(prefab))
            _pending.Add(new PendingSpawn(prefab, position, rotation, MathF.Max(0.0f, spawnDelay)));
    }

    private record struct PendingSpawn(string Prefab, Vector3 Position, Vector3 Rotation, float TimeRemaining);
}
