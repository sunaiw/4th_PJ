using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : SingletonBehaviour<EnemySpawner>
{
    [Header("Spawner Settings")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject enemy2Prefab;
    [SerializeField] private GameObject enemy3Prefab;
    [SerializeField] private GameObject enemy4Prefab;
    [SerializeField] private GameObject enemy5Prefab;
    [SerializeField] private float spawnInterval = 1.0f;

    [Header("BarricadeBuster Settings")]
    // Outpost(バリケード)は敵のターゲット優先度が最下位(Step 1)のため、Step 2の配置ルールで
    // 周囲に集まる通常タワーが常に先に狙われてしまい、BarricadeBuster以外の手段では
    // Outpostがほぼ破壊されない。これによりOfflineカスケード(Step 3)がバランス調整前は
    // ほぼ発火しない問題が確認されたため、解禁Wave・出現率をそれぞれ緩和する。
    // - 解禁Wave 6→4に変更。Wave 5開始案は、5がボスウェーブで通常敵の抽選自体が行われず
    //   実質Wave 6開始と変わらないため不採用。Wave 3開始案は、プレイヤーがOutpostの
    //   配置ルールに慣れる前に拠点を破壊され理不尽になるため不採用
    // - 出現率 5%→8%に変更。Wave 4で20体×8%≒1.6体、Wave 10で50体×8%=4体出現する計算になる。
    //   Busterはattack Range 1.5でタワー射程3.0の内側まで踏み込んで約3秒停止する仕様のため、
    //   実際に破壊まで到達するのはこのうち一部になる想定
    [SerializeField] private int barricadeBusterUnlockWave = 4;
    [SerializeField] private float barricadeBusterSpawnRate = 0.08f;

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
        if (enemy4Prefab == null)
        {
            enemy4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy4.prefab");
        }
        if (enemy5Prefab == null)
        {
            enemy5Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy5.prefab");
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
                bool isBarricadeBuster = (currentSpawningWave >= barricadeBusterUnlockWave) && (Random.value < barricadeBusterSpawnRate);
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

    // Wave帯に応じて特殊エネミーの出現比率を段階的に上げる。
    // 各レートは「その種別の単独出現率」で、合計が1.0に満たない残りが通常Enemyになる。
    private GameObject SelectRegularEnemyPrefab()
    {
        float heavyRate, fastRate, bomberRate, disruptorRate;
        if (currentSpawningWave >= 15)      { heavyRate = 0.20f; fastRate = 0.20f; bomberRate = 0.15f; disruptorRate = 0.15f; }
        else if (currentSpawningWave >= 12) { heavyRate = 0.20f; fastRate = 0.20f; bomberRate = 0.10f; disruptorRate = 0.10f; }
        else if (currentSpawningWave >= 10) { heavyRate = 0.20f; fastRate = 0.20f; bomberRate = 0.10f; disruptorRate = 0.00f; }
        else if (currentSpawningWave >= 8)  { heavyRate = 0.15f; fastRate = 0.15f; bomberRate = 0.10f; disruptorRate = 0.00f; }
        else if (currentSpawningWave >= 5)  { heavyRate = 0.15f; fastRate = 0.15f; bomberRate = 0.00f; disruptorRate = 0.00f; }
        else if (currentSpawningWave >= 3)  { heavyRate = 0.00f; fastRate = 0.10f; bomberRate = 0.00f; disruptorRate = 0.00f; }
        else                                { heavyRate = 0.00f; fastRate = 0.00f; bomberRate = 0.00f; disruptorRate = 0.00f; }

        float r = Random.value;
        float cumulative = 0f;

        cumulative += heavyRate;
        if (enemy3Prefab != null && r < cumulative) return enemy3Prefab;

        cumulative += fastRate;
        if (enemy2Prefab != null && r < cumulative) return enemy2Prefab;

        cumulative += bomberRate;
        if (enemy4Prefab != null && r < cumulative) return enemy4Prefab;

        cumulative += disruptorRate;
        if (enemy5Prefab != null && r < cumulative) return enemy5Prefab;

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
