using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    [Header("Spawner Settings")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject enemy2Prefab;
    [SerializeField] private GameObject enemy3Prefab;
    [SerializeField] private float spawnInterval = 1.0f;
    [SerializeField] private int baseEnemyCountPercent = 100; // ウェーブ進行で敵数を増やす調整値

    private List<Enemy> activeEnemies = new List<Enemy>();
    private bool isSpawning = false;
    private int currentSpawningWave = 1;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

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
        int activeSpawnerCount = 1;
        if (MapManager.Instance != null)
        {
            activeSpawnerCount = MapManager.Instance.GetActiveSpawners().Count;
        }
        
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
            if (MapManager.Instance != null)
            {
                // 現在解放されているアクティブな全スポナーを取得
                List<Vector3Int> activeSpawners = MapManager.Instance.GetActiveSpawners();
                // ランダムにスポナーを選択
                Vector3Int spawnGridPos = activeSpawners[Random.Range(0, activeSpawners.Count)];
                
                GameObject selectedPrefab = enemyPrefab;
                if (isBossWave)
                {
                    // ボスのベースとしてenemy3Prefabを使用、なければenemyPrefab
                    selectedPrefab = (enemy3Prefab != null) ? enemy3Prefab : enemyPrefab;
                    SpawnEnemy(spawnGridPos, selectedPrefab, true);
                }
                else
                {
                    bool isBarricadeBuster = (currentSpawningWave >= 6) && (Random.value < 0.05f);
                    if (isBarricadeBuster)
                    {
                        // 基本は通常Enemyと同じくenemyPrefabを使用する
                        selectedPrefab = enemyPrefab;
                    }
                    else
                    {
                        float r = Random.value;
                        // Wave帯に応じて特殊エネミー比率を段階的に上げる
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

                        if (enemy3Prefab != null && r < enemy3Rate)
                        {
                            selectedPrefab = enemy3Prefab;
                        }
                        else if (enemy2Prefab != null && r < enemy2Rate)
                        {
                            selectedPrefab = enemy2Prefab;
                        }
                    }
                    SpawnEnemy(spawnGridPos, selectedPrefab, false, isBarricadeBuster);
                }
            }
            else
            {
                // フォールバック（MapManagerが無い場合）
                if (isBossWave)
                {
                    GameObject selectedPrefab = (enemy3Prefab != null) ? enemy3Prefab : enemyPrefab;
                    SpawnEnemy(new Vector3Int(-18, -1, 0), selectedPrefab, true, false);
                }
                else
                {
                    bool isBarricadeBuster = (currentSpawningWave >= 6) && (Random.value < 0.05f);
                    SpawnEnemy(new Vector3Int(-18, -1, 0), enemyPrefab, false, isBarricadeBuster);
                }
            }
            yield return new WaitForSeconds(spawnInterval);
        }

        isSpawning = false;
        Debug.Log("[EnemySpawner] Finished Spawning all enemies. Waiting for clear.");
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
                pathPoints = MapManager.Instance.GridToWorld(spawnGridPos) == spawnPos ? MapManager.Instance.GetInitialPath(spawnGridPos) : new List<Vector3>();
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
