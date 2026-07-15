using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : SingletonBehaviour<EnemySpawner>
{
    [Header("Spawner Settings")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject enemy2Prefab;
    [SerializeField] private GameObject enemy3Prefab;
    [SerializeField] private float spawnInterval = 1.0f;

    private List<Enemy> activeEnemies = new List<Enemy>();
    private bool isSpawning = false;
    private int currentSpawningWave = 1;

    protected override void OnSingletonAwake()
    {
        #if UNITY_EDITOR
        if (enemyPrefab == null)
        {
            enemyPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy.prefab");
        }
        if (enemy2Prefab == null)
        {
            enemy2Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy2.prefab");
        }
        if (enemy3Prefab == null)
        {
            enemy3Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy3.prefab");
        }
        #endif
    }

    public void StartWave(int waveNumber)
    {
        currentSpawningWave = waveNumber;

        // 5の倍数のWaveではボスのみが出現するため出現数は1
        int enemyCount = (waveNumber % 5 == 0) ? 1 : 5 * waveNumber;
        StartCoroutine(SpawnWaveCoroutine(enemyCount));
    }

    private IEnumerator SpawnWaveCoroutine(int count)
    {
        isSpawning = true;
        Debug.Log($"[EnemySpawner] Starting Wave Spawning. Count: {count}");

        bool isBossWave = (currentSpawningWave % 5 == 0);

        for (int i = 0; i < count; i++)
        {
            Vector3Int spawnGridPos = GetRandomSpawnGridPos();

            if (isBossWave)
            {
                // ボスのベースとしてenemy3Prefabを使用、なければenemyPrefab
                GameObject bossPrefab = (enemy3Prefab != null) ? enemy3Prefab : enemyPrefab;
                SpawnEnemy(spawnGridPos, bossPrefab, true);
            }
            else
            {
                bool isBarricadeBuster = (currentSpawningWave >= 6) && (Random.value < 0.05f);
                // バリケードバスターは通常Enemyと同じenemyPrefabをベースにする
                GameObject selectedPrefab = isBarricadeBuster ? enemyPrefab : SelectRegularEnemyPrefab();
                SpawnEnemy(spawnGridPos, selectedPrefab, false, isBarricadeBuster);
            }
            yield return new WaitForSeconds(spawnInterval);
        }

        isSpawning = false;
        Debug.Log("[EnemySpawner] Finished Spawning all enemies. Waiting for clear.");
    }

    // 現在解放されているアクティブなスポナーからランダムに1つ選ぶ
    private Vector3Int GetRandomSpawnGridPos()
    {
        if (MapManager.Instance != null)
        {
            List<Vector3Int> activeSpawners = MapManager.Instance.GetActiveSpawners();
            return activeSpawners[Random.Range(0, activeSpawners.Count)];
        }
        return new Vector3Int(-18, -1, 0); // フォールバック（MapManagerが無い場合）
    }

    // Wave帯に応じて特殊エネミー比率を段階的に上げる
    private GameObject SelectRegularEnemyPrefab()
    {
        float enemy3Rate, enemy2Rate;
        if (currentSpawningWave >= 15)
        {
            enemy3Rate = 0.25f; enemy2Rate = 0.50f; // Enemy3: 25%, Enemy2: 25%, Enemy: 50%
        }
        else if (currentSpawningWave >= 10)
        {
            enemy3Rate = 0.20f; enemy2Rate = 0.40f; // Enemy3: 20%, Enemy2: 20%, Enemy: 60%
        }
        else if (currentSpawningWave >= 5)
        {
            enemy3Rate = 0.15f; enemy2Rate = 0.30f; // Enemy3: 15%, Enemy2: 15%, Enemy: 70%
        }
        else if (currentSpawningWave >= 3)
        {
            enemy3Rate = 0.0f; enemy2Rate = 0.1f;   // Enemy2: 10%, Enemy: 90%
        }
        else
        {
            enemy3Rate = 0.0f; enemy2Rate = 0.0f;   // Enemy: 100%
        }

        float r = Random.value;
        if (enemy3Prefab != null && r < enemy3Rate) return enemy3Prefab;
        if (enemy2Prefab != null && r < enemy2Rate) return enemy2Prefab;
        return enemyPrefab;
    }

    private void SpawnEnemy(Vector3Int spawnGridPos, GameObject prefabToSpawn, bool isBoss = false, bool isBarricadeBuster = false)
    {
        if (prefabToSpawn == null)
        {
            Debug.LogError("[EnemySpawner] Enemy Prefab is missing!");
            return;
        }

        if (MapManager.Instance == null)
        {
            Debug.LogError("[EnemySpawner] MapManager Instance is missing!");
            return;
        }

        Vector3 spawnPos = MapManager.Instance.GridToWorld(spawnGridPos);
        GameObject enemyObj = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);
        Enemy enemy = enemyObj.GetComponent<Enemy>();

        if (enemy != null)
        {
            activeEnemies.Add(enemy);
            if (isBoss)
            {
                enemy.SetupBoss(currentSpawningWave);
            }
            else if (isBarricadeBuster)
            {
                enemy.SetupBarricadeBuster(currentSpawningWave);
            }
            else
            {
                enemy.ScaleStats(currentSpawningWave);
            }
            
            // A* を使って最短経路を計算。ignoreTowersフラグとavoidThreatsフラグを伝達
            AStarPathfinding pathfinder = AStarPathfinding.Instance;
            List<Vector3> pathPoints = null;
            if (pathfinder != null)
            {
                pathPoints = pathfinder.FindPath(spawnGridPos, MapManager.Instance.CoreGridPos, enemy.IgnoreTowers, enemy.AvoidThreats);
            }
            
            if (pathPoints == null || pathPoints.Count == 0)
            {
                pathPoints = MapManager.Instance.GetInitialPath(spawnGridPos);
            }
            enemy.SetPath(pathPoints);
        }
    }

    public void UnregisterEnemy(Enemy enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
        }
    }

    public bool IsWaveRunning()
    {
        return isSpawning || activeEnemies.Count > 0;
    }

    public List<Enemy> GetActiveEnemies()
    {
        return activeEnemies;
    }
}
