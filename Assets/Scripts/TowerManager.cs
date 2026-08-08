using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// タワー種別ごとの配置ルールをまとめたデータ定義。
// 種別を追加する際は、この定義を1件足すだけでコスト・解禁Wave・設置上限・HUDカードがすべて連動する。
[System.Serializable]
public class TowerDefinition
{
    public TowerType type;
    public GameObject prefab;
    public int cost = 2;
    public int unlockWave = 1;       // このWave以降で配置可能
    public int maxPerSetup = 0;      // 1回のSetupフェーズで配置できる上限。0 = 無制限
    public string displayName = "";  // HUDカードに表示する名前（英語）
}

public class TowerManager : SingletonBehaviour<TowerManager>
{
    [Header("Tower Definitions")]
    [SerializeField] private List<TowerDefinition> towerDefinitions = new List<TowerDefinition>();

    public List<TowerDefinition> GetTowerDefinitions() => towerDefinitions;

    public TowerDefinition GetDefinition(TowerType type)
    {
        for (int i = 0; i < towerDefinitions.Count; i++)
        {
            if (towerDefinitions[i] != null && towerDefinitions[i].type == type)
            {
                return towerDefinitions[i];
            }
        }
        return null;
    }

    private TowerRangeIndicator previewIndicator;
    private GameObject ghostPreviewObj;
    private TowerType ghostPreviewType;
    private TowerType activePlacementType = TowerType.Normal;

    private List<Tower> activeTowers = new List<Tower>();
    private bool isDraggingTower = false;

    private readonly Dictionary<TowerType, int> placedCountsInCurrentSetup = new Dictionary<TowerType, int>();

    // 引数: (種別, そのSetupフェーズでの現在の設置数)
    public event System.Action<TowerType, int> OnPlacedCountChanged;

    public int GetPlacedCountInCurrentSetup(TowerType type)
    {
        return placedCountsInCurrentSetup.TryGetValue(type, out int count) ? count : 0;
    }

    // maxPerSetup が 0（無制限）の場合は常に true
    public bool CanPlaceMoreInCurrentSetup(TowerType type)
    {
        TowerDefinition def = GetDefinition(type);
        if (def == null) return false;
        if (def.maxPerSetup <= 0) return true;
        return GetPlacedCountInCurrentSetup(type) < def.maxPerSetup;
    }

    private void ChangePlacedCount(TowerType type, int delta)
    {
        int current = GetPlacedCountInCurrentSetup(type);
        int next = Mathf.Max(0, current + delta);
        placedCountsInCurrentSetup[type] = next;
        OnPlacedCountChanged?.Invoke(type, next);
    }

    public List<Tower> GetActiveTowers()
    {
        return activeTowers;
    }

    // 配置タイプに対応するコストを返す
    public int GetPlacementCost(TowerType type)
    {
        TowerDefinition def = GetDefinition(type);
        return def != null ? def.cost : 0;
    }

    // 配置タイプに対応するプレハブを返す
    private GameObject GetPlacementPrefab(TowerType type)
    {
        TowerDefinition def = GetDefinition(type);
        return def != null ? def.prefab : null;
    }

    public void RegisterTower(Tower tower)
    {
        if (!activeTowers.Contains(tower))
        {
            activeTowers.Add(tower);
        }
    }

    public void UnregisterTower(Tower tower)
    {
        if (activeTowers.Contains(tower))
        {
            activeTowers.Remove(tower);
            if (tower.PlacedWave == (GameManager.Instance != null ? GameManager.Instance.CurrentWave : 1))
            {
                ChangePlacedCount(tower.Type, -1);
            }
        }
    }

    // Step 1: Setupフェーズは全種別配置可能。Defenseフェーズ中はバリケードによる緊急復旧のみ許可する。
    public bool IsPlacementAllowedInCurrentPhase(TowerType type)
    {
        if (GameManager.Instance == null) return false;
        GamePhase phase = GameManager.Instance.CurrentPhase;
        if (phase == GamePhase.Setup) return true;
        if (phase == GamePhase.Defense && type == TowerType.Barricade) return true;
        return false;
    }

    public void StartDragPlacement(TowerType type)
    {
        TowerDefinition def = GetDefinition(type);
        if (def == null || def.prefab == null)
        {
            Debug.LogWarning($"[TowerManager] No definition/prefab for {type}.");
            return;
        }

        if (!IsPlacementAllowedInCurrentPhase(type))
        {
            string phaseName = GameManager.Instance != null ? GameManager.Instance.CurrentPhase.ToString() : "Unknown";
            Debug.LogWarning($"[TowerManager] Cannot place {type} during {phaseName} phase.");
            return;
        }

        int wave = GameManager.Instance != null ? GameManager.Instance.CurrentWave : 1;
        if (wave < def.unlockWave)
        {
            Debug.LogWarning($"[TowerManager] Cannot place {type} before Wave {def.unlockWave}!");
            return;
        }
        if (!CanPlaceMoreInCurrentSetup(type))
        {
            Debug.LogWarning($"[TowerManager] Placement limit ({def.maxPerSetup}) reached for {type} in this setup phase!");
            return;
        }

        activePlacementType = type;
        isDraggingTower = true;
    }

    public void EndDragPlacement()
    {
        if (isDraggingTower)
        {
            isDraggingTower = false;
            TryPlaceTowerAtMouse();
            HidePlacementPreview();
        }
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    protected override void OnSingletonAwake()
    {
        if (towerDefinitions == null || towerDefinitions.Count == 0)
        {
            towerDefinitions = CreateDefaultDefinitions();
        }
        ResolveMissingPrefabs();
    }

    // 種別ごとの既定パラメータ。シーン側で未設定の場合に使用される。
    private List<TowerDefinition> CreateDefaultDefinitions()
    {
        return new List<TowerDefinition>
        {
            new TowerDefinition { type = TowerType.Normal,    cost = 2, unlockWave = 1, maxPerSetup = 0, displayName = "Tower" },
            new TowerDefinition { type = TowerType.Tank,      cost = 3, unlockWave = 1, maxPerSetup = 0, displayName = "Tank" },
            new TowerDefinition { type = TowerType.Healer,    cost = 4, unlockWave = 3, maxPerSetup = 0, displayName = "Healer" },
            new TowerDefinition { type = TowerType.Splash,    cost = 4, unlockWave = 5, maxPerSetup = 0, displayName = "Splash" },
            new TowerDefinition { type = TowerType.Frost,     cost = 3, unlockWave = 4, maxPerSetup = 0, displayName = "Frost" },
            new TowerDefinition { type = TowerType.Barricade, cost = 0, unlockWave = 1, maxPerSetup = 6, displayName = "Barricade" },
        };
    }

    // Prefab参照が未割り当ての場合、命名規約に従ってエディタ上で自動解決する。
    // （EnemySpawner.OnSingletonAwake() と同じ方式）
    private void ResolveMissingPrefabs()
    {
        #if UNITY_EDITOR
        foreach (TowerDefinition def in towerDefinitions)
        {
            if (def == null || def.prefab != null) continue;
            string path = "Assets/" + GetPrefabFileName(def.type) + ".prefab";
            def.prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (def.prefab == null)
            {
                Debug.LogWarning($"[TowerManager] Prefab not found for {def.type} at {path}");
            }
        }
        #endif
    }

    private static string GetPrefabFileName(TowerType type)
    {
        switch (type)
        {
            case TowerType.Healer: return "Healer";
            case TowerType.Barricade: return "Barricade";
            case TowerType.Tank: return "TankTower";
            case TowerType.Splash: return "SplashTower";
            case TowerType.Frost: return "FrostTower";
            default: return "Tower";
        }
    }

    private void HandlePhaseChanged(GamePhase newPhase)
    {
        if (newPhase == GamePhase.Setup)
        {
            placedCountsInCurrentSetup.Clear();
            foreach (TowerDefinition def in towerDefinitions)
            {
                if (def == null) continue;
                OnPlacedCountChanged?.Invoke(def.type, 0);
            }
        }
    }

    private void Update()
    {
        // Step 1: Setupフェーズは常時、Defenseフェーズはバリケードのドラッグ中のみプレビューを表示する
        bool canShowPreview = isDraggingTower && IsPlacementAllowedInCurrentPhase(activePlacementType);

        if (canShowPreview)
        {
            UpdatePlacementPreview();
        }
        else
        {
            HidePlacementPreview();
        }
    }

    private void TryPlaceTowerAtMouse()
    {
        if (MapManager.Instance == null || GameManager.Instance == null) return;

        if (!IsPlacementAllowedInCurrentPhase(activePlacementType))
        {
            Debug.Log($"[TowerManager] Cannot place {activePlacementType} during {GameManager.Instance.CurrentPhase} phase.");
            return;
        }

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;
        
        Vector3Int cellPos = MapManager.Instance.WorldToGrid(mouseWorldPos);

        if (ValidateTowerPlacement(cellPos))
        {
            if (!CanPlaceMoreInCurrentSetup(activePlacementType))
            {
                Debug.LogWarning($"[TowerManager] Placement limit reached for {activePlacementType} in this setup phase!");
                return;
            }

            if (GameManager.Instance.SpendCost(GetPlacementCost(activePlacementType)))
            {
                SpawnTower(cellPos);
            }
            else
            {
                Debug.Log("[TowerManager] Not enough cost to place this item!");
            }
        }
        else
        {
            Debug.Log($"[TowerManager] Cannot place tower at {cellPos}");
        }
    }

    public bool ValidateTowerPlacement(Vector3Int cellPos)
    {
        // 1. 地形や重複のチェック (壁、他のタワー、コア、スポーンポイント等)
        if (!MapManager.Instance.CanPlaceTower(cellPos))
        {
            return false;
        }

        // 2. Step 1: 防衛フェーズ中の緊急設置時のみ、敵が立っているセルへの設置を禁止する
        //    （Setupフェーズ中は敵が存在しないため判定不要）
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Defense
            && IsEnemyOccupyingCell(cellPos))
        {
            Debug.Log($"[TowerManager] Placement rejected: an enemy occupies {cellPos}!");
            return false;
        }

        // 3. 経路閉塞チェック (A*を用いて、コアに到達できなくなる完全閉塞を防ぐ)
        //    防衛フェーズ中の緊急設置でも必ず実行する
        if (!CheckPathValidityWithTemporaryTower(cellPos))
        {
            Debug.Log("[TowerManager] Placement rejected: blocking the path to the core!");
            return false;
        }

        return true;
    }

    // 指定セルに現在アクティブな敵が立っているかどうかを判定する（防衛フェーズ中の設置判定用）
    private bool IsEnemyOccupyingCell(Vector3Int cellPos)
    {
        if (EnemySpawner.Instance == null || MapManager.Instance == null) return false;

        List<Enemy> activeEnemies = EnemySpawner.Instance.GetActiveEnemies();
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            Enemy enemy = activeEnemies[i];
            if (enemy == null) continue;
            if (MapManager.Instance.WorldToGrid(enemy.transform.position) == cellPos)
            {
                return true;
            }
        }
        return false;
    }

    private bool CheckPathValidityWithTemporaryTower(Vector3Int cellPos)
    {
        // 一時的にその位置の占有状態をTowerにする
        MapManager.Instance.SetTowerOccupant(cellPos, true);

        bool isValid = true;

        // すべてのアクティブなスポナーからコアまでの経路が有効かをチェックする
        AStarPathfinding pathfinder = AStarPathfinding.Instance;
        if (pathfinder != null)
        {
            List<Vector3Int> activeSpawners = MapManager.Instance.GetActiveSpawners();
            foreach (Vector3Int spawnerPos in activeSpawners)
            {
                if (!pathfinder.HasValidPath(spawnerPos, MapManager.Instance.CoreGridPos))
                {
                    isValid = false;
                    break;
                }
            }
        }

        // 状態を元に戻す
        MapManager.Instance.SetTowerOccupant(cellPos, false);

        return isValid;
    }

    private void SpawnTower(Vector3Int cellPos)
    {
        Vector3 spawnWorldPos = MapManager.Instance.GridToWorld(cellPos);
        Instantiate(GetPlacementPrefab(activePlacementType), spawnWorldPos, Quaternion.identity);

        // MapManagerにタワー占有を確定登録
        MapManager.Instance.SetTowerOccupant(cellPos, true);

        ChangePlacedCount(activePlacementType, 1);

        // タワー配置が完了したため、既存の敵について経路を再計算させる（Step 2で本格連携）
        NotifyEnemiesToRecalculatePath();
    }

    public void NotifyEnemiesToRecalculatePath()
    {
        AStarPathfinding pathfinder = AStarPathfinding.Instance;
        if (pathfinder == null || MapManager.Instance == null) return;

        List<Enemy> activeEnemies = EnemySpawner.Instance != null ? EnemySpawner.Instance.GetActiveEnemies() : new List<Enemy>();
        foreach (Enemy enemy in activeEnemies)
        {
            if (enemy == null) continue;
            
            // 現在の敵のグリッド座標からコアまでの経路を再取得
            Vector3Int enemyGridPos = MapManager.Instance.WorldToGrid(enemy.transform.position);
            List<Vector3> newPath = pathfinder.FindPath(enemyGridPos, MapManager.Instance.CoreGridPos, enemy.IgnoreTowers, enemy.AvoidThreats);
            if (newPath != null && newPath.Count > 0)
            {
                enemy.UpdatePath(newPath);
            }
        }
    }

    private void UpdatePlacementPreview()
    {
        if (MapManager.Instance == null) return;

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;
        Vector3Int cellPos = MapManager.Instance.WorldToGrid(mouseWorldPos);

        Vector3 cellCenterWorld = MapManager.Instance.GridToWorld(cellPos);

        // 累積獲得済みの射程アップグレードを取得
        float rangeMultiplier = 1f;
        if (RewardManager.Instance != null)
        {
            var counts = RewardManager.Instance.GetAcquiredRewardCounts();
            if (counts.TryGetValue(RewardType.IncreaseTowerRange, out int rangeCount))
            {
                rangeMultiplier += rangeCount * 0.1f;
            }
        }

        float rangeToShow = 3f;
        if (activePlacementType == TowerType.Barricade)
        {
            rangeToShow = 0.5f;
        }
        else // Tower / Healer
        {
            GameObject prefab = GetPlacementPrefab(activePlacementType);
            Tower t = prefab != null ? prefab.GetComponent<Tower>() : null;
            if (t != null) rangeToShow = t.Range * rangeMultiplier;
        }

        if (previewIndicator == null)
        {
            GameObject indicatorObj = new GameObject("PlacementRangePreview");
            previewIndicator = indicatorObj.AddComponent<TowerRangeIndicator>();
            previewIndicator.Init(rangeToShow, new Color(0.2f, 1f, 0.3f, 0.35f));
        }

        previewIndicator.transform.position = cellCenterWorld;
        previewIndicator.UpdateRange(rangeToShow);
        previewIndicator.SetVisible(true);

        bool isValidPos = ValidateTowerPlacement(cellPos);
        bool hasEnoughCost = GameManager.Instance.Cost >= GetPlacementCost(activePlacementType);

        // 配置可否に応じて緑/赤の半透明で表示
        previewIndicator.SetColor(isValidPos && hasEnoughCost
            ? new Color(0f, 1f, 0f, 0.35f)
            : new Color(1f, 0f, 0f, 0.35f));

        // ドラッグ中のタワー本体を半透明のゴーストとして表示
        UpdateGhostPreview(cellCenterWorld, isValidPos && hasEnoughCost);
    }

    private void UpdateGhostPreview(Vector3 worldPos, bool isValidPlacement)
    {
        if (ghostPreviewObj == null || ghostPreviewType != activePlacementType)
        {
            if (ghostPreviewObj != null)
            {
                Destroy(ghostPreviewObj);
            }
            ghostPreviewObj = CreateGhostPreview(activePlacementType);
            ghostPreviewType = activePlacementType;
        }

        if (ghostPreviewObj == null) return;

        ghostPreviewObj.transform.position = worldPos;
        SetGhostColor(ghostPreviewObj, isValidPlacement
            ? new Color(1f, 1f, 1f, 0.5f)
            : new Color(1f, 0.4f, 0.4f, 0.5f));
        ghostPreviewObj.SetActive(true);
    }

    private GameObject CreateGhostPreview(TowerType type)
    {
        GameObject prefab = GetPlacementPrefab(type);
        if (prefab == null) return null;

        GameObject ghost = Instantiate(prefab);
        ghost.name = "PlacementGhostPreview";

        // タワーとしての挙動（攻撃、当たり判定等）を全て無効化し、見た目のみ残す
        foreach (MonoBehaviour behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }
        foreach (Collider2D collider in ghost.GetComponentsInChildren<Collider2D>(true))
        {
            collider.enabled = false;
        }
        foreach (Rigidbody2D rb in ghost.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.simulated = false;
        }

        return ghost;
    }

    private void SetGhostColor(GameObject ghost, Color color)
    {
        foreach (SpriteRenderer sr in ghost.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.color = color;
        }
    }

    private void HidePlacementPreview()
    {
        if (previewIndicator != null)
        {
            previewIndicator.SetVisible(false);
        }
        if (ghostPreviewObj != null)
        {
            ghostPreviewObj.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (previewIndicator != null)
        {
            Destroy(previewIndicator.gameObject);
        }
        if (ghostPreviewObj != null)
        {
            Destroy(ghostPreviewObj);
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
    }
}
