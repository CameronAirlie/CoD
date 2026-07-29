using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Populates configured arena points with alternating health and ammo supplies.</summary>
public sealed class SupplySpawner : ScriptBehaviour
{
    [SerializedField] private string healthPrefab = "project://Prefabs/HealthPickup.plutoprefab";
    [SerializedField] private string ammoPrefab = "project://Prefabs/AmmoPickup.plutoprefab";
    [SerializedField] private GameObject? pointA = null;
    [SerializedField] private GameObject? pointB = null;
    [SerializedField] private GameObject? pointC = null;
    [SerializedField] private GameObject? pointD = null;
    [SerializedField] private GameObject? pointE = null;
    [SerializedField] private GameObject? pointF = null;
    [SerializedField] private GameObject? pointG = null;
    [SerializedField] private GameObject? pointH = null;

    [SerializedField] private float spawnDelay = 5f;
    private float _spawnTimer = 0.0f;

    public override void OnCreate()
    {
        GameObject?[] points = [pointA, pointB, pointC, pointD, pointE, pointF, pointG, pointH];
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            if (point is null || !point.IsValid)
                continue;

            var prefab = index % 2 == 0 ? healthPrefab : ammoPrefab;
            if (string.IsNullOrWhiteSpace(prefab) ||
                Prefab.Instantiate(prefab, point.WorldPosition, point.WorldRotation) is null)
            {
                Debug.LogWarning($"Could not spawn supply at {point.Name}.");
            }
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        _spawnTimer += deltaTime;
        if (_spawnTimer < spawnDelay)
            return;

        _spawnTimer = 0.0f;
        OnCreate();
    }
}
