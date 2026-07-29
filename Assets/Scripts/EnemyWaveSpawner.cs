using System;
using PlutoGE.ScriptCore;

namespace CoD.Scripts;

/// <summary>Runs escalating enemy waves from a set of arena spawn points.</summary>
public sealed class EnemyWaveSpawner : ScriptBehaviour
{
    [SerializedField] private string enemyPrefab = "project://Prefabs/Enemy.plutoprefab";
    [SerializedField] private string enemyTag = "Enemy";
    [SerializedField] private GameObject? spawnPointA = null;
    [SerializedField] private GameObject? spawnPointB = null;
    [SerializedField] private GameObject? spawnPointC = null;
    [SerializedField] private GameObject? spawnPointD = null;
    [SerializedField] private int startingWaveSize = 2;
    [SerializedField] private int enemiesAddedPerWave = 1;
    [SerializedField] private int maximumAlive = 6;
    [SerializedField] private float timeBetweenWaves = 4.0f;
    [SerializedField] private float timeBetweenSpawns = 0.65f;
    [SerializedField] private float aliveCountRefreshInterval = 0.25f;

    private GameObject?[] _spawnPoints = [];
    private float _time;
    private float _nextWaveAt;
    private float _nextSpawnAt;
    private int _wave;
    private int _remainingToSpawn;
    private int _nextSpawnPoint;
    private int _cachedAlive;
    private float _nextAliveCountRefreshAt;
    private bool _waitingForClear = true;

    public override void OnCreate()
    {
        _spawnPoints = [spawnPointA, spawnPointB, spawnPointC, spawnPointD];
        _nextSpawnPoint = (int)(EntityId % (uint)_spawnPoints.Length);
        _nextWaveAt = MathF.Max(0.0f, timeBetweenWaves);
        RefreshAliveCount();
    }

    public override void OnUpdate(float deltaTime)
    {
        _time += MathF.Max(0.0f, deltaTime);
        if (_time >= _nextAliveCountRefreshAt)
            RefreshAliveCount();

        if (_remainingToSpawn > 0)
        {
            if (_time >= _nextSpawnAt && _cachedAlive < Math.Max(1, maximumAlive))
                SpawnOne();
            return;
        }

        if (_cachedAlive > 0)
        {
            _waitingForClear = true;
            return;
        }

        if (_waitingForClear)
        {
            _waitingForClear = false;
            _nextWaveAt = _time + MathF.Max(0.0f, timeBetweenWaves);
        }

        if (_time >= _nextWaveAt)
            BeginWave();
    }

    private void BeginWave()
    {
        _wave++;
        _remainingToSpawn = Math.Max(1, startingWaveSize + (_wave - 1) * enemiesAddedPerWave);
        _nextSpawnAt = _time;
        Debug.Log($"Starting enemy wave {_wave} ({_remainingToSpawn} enemies).");
    }

    private void SpawnOne()
    {
        var point = NextValidSpawnPoint();
        if (point is null || string.IsNullOrWhiteSpace(enemyPrefab))
        {
            _remainingToSpawn = 0;
            Debug.LogWarning("Enemy wave spawner has no valid prefab or spawn points.");
            return;
        }

        var enemy = Prefab.Instantiate(enemyPrefab, point.WorldPosition, point.WorldRotation);
        if (enemy is null)
        {
            _remainingToSpawn = 0;
            Debug.LogWarning("Enemy wave spawner could not instantiate the enemy prefab.");
            return;
        }

        _remainingToSpawn--;
        _cachedAlive++;
        _nextSpawnAt = _time + MathF.Max(0.05f, timeBetweenSpawns);
    }

    private void RefreshAliveCount()
    {
        _cachedAlive = string.IsNullOrWhiteSpace(enemyTag)
            ? 0
            : GameObject.FindByTag(enemyTag).Length;
        _nextAliveCountRefreshAt = _time + MathF.Max(0.05f, aliveCountRefreshInterval);
    }

    private GameObject? NextValidSpawnPoint()
    {
        for (var attempt = 0; attempt < _spawnPoints.Length; attempt++)
        {
            var index = (_nextSpawnPoint + attempt) % _spawnPoints.Length;
            var point = _spawnPoints[index];
            if (point is null || !point.IsValid)
                continue;

            _nextSpawnPoint = (index + 1) % _spawnPoints.Length;
            return point;
        }
        return null;
    }
}
