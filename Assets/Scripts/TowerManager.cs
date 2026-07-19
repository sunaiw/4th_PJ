using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class TowerManager : SingletonBehaviour<TowerManager>
{
    public enum PlacementType { Tower, Barricade, Healer }

    // 1回のSetupフェーズで設置できるバリケードの上限
    public const int MaxBarricadesPerSetup = 3;
    // Healerが解禁されるWave
    public const int HealerUnlockWave = 3;

    [Header("Placement Settings")]
    [SerializeField] private GameObject towerPrefab;
    [SerializeField] private GameObject barricadePrefab;
    [SerializeField] private GameObject healerPrefab;
    [SerializeField] private int towerCost = 2;
    [SerializeField] private int barricadeCost = 1;
    [SerializeField] private int healerCost = 2;

    public int TowerCost { get => towerCost; set => towerCost = value; }
    public int BarricadeCost { get => barricadeCost; set => barricadeCost = value; }
    public int HealerCost { get => healerCost; set => healerCost = value; }

    private TowerRangeIndicator previewIndicator;
    private GameObject ghostPreviewObj;
    private PlacementType ghostPreviewType;
    private PlacementType activePlacementType = PlacementType.Tower;

    private List<Tower> activeTowers = new List<Tower>();
    private bool isDraggingTower = false;

    private int placedBarricadesInCurrentSetup = 0;
    public int PlacedBarricadesInCurrentSetup => placedBarricadesInCurrentSetup;
    public event System.Action<int> OnBarricadeCountChanged;

    public List<Tower> GetActiveTowers()
    {
        return activeTowers;
    }

    // 配置タイプに対応するコストを返す
    public int GetPlacementCost(PlacementType type)
    {
        switch (type)
        {
            case PlacementType.Barricade: return barricadeCost;
            case PlacementType.Healer: return healerCost;
            default: return towerCost;
        }
    }

    // 配置タイプに対応するプレハブを返す
    private GameObject GetPlacementPrefab(PlacementType type)
    {
        switch (type)
        {
            case PlacementType.Barricade: return barricadePrefab;
            case PlacementType.Healer: return healerPrefab;
            default: return towerPrefab;
        }
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
            if (tower.IsBarricade && tower.PlacedWave == (GameManager.Instance != null ? GameManager.Instance.CurrentWave : 1))
            {
                placedBarricadesInCurrentSetup = Mathf.Max(0, placedBarricadesInCurrentSetup - 1);
                OnBarricadeCountChanged?.Invoke(placedBarricadesInCurrentSetup);
            }
        }
    }

    public void StartDragPlacement(PlacementType type)
    {
        if (type == PlacementType.Healer)
        {
            int wave = GameManager.Instance != null ? GameManager.Instance.CurrentWave : 1;
            if (wave < HealerUnlockWave)
            {
                Debug.LogWarning($"[TowerManager] Cannot place Healer before Wave {HealerUnlockWave}!");
                return;
            }
        }
        else if (type == PlacementType.Barricade)
        {
            if (placedBarricadesInCurrentSetup >= MaxBarricadesPerSetup)
            {
                Debug.LogWarning("[TowerManager] Cannot place more than 3 Barricades in this setup phase!");
                return;
            }
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
        barricadeCost = 0; // バリケードの設置コストを0（廃止）にする
    }

    private void HandlePhaseChanged(GamePhase newPhase)
    {
        if (newPhase == GamePhase.Setup)
        {
            placedBarricadesInCurrentSetup = 0;
            OnBarricadeCountChanged?.Invoke(placedBarricadesInCurrentSetup);
        }
    }

    private void Update()
    {
        // 準備フェーズ（Setup）の時に、ドラッグ中である場合のみプレビューを表示する
        bool isSetupPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Setup;

        if (isSetupPhase && isDraggingTower)
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

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;
        
        Vector3Int cellPos = MapManager.Instance.WorldToGrid(mouseWorldPos);

        if (ValidateTowerPlacement(cellPos))
        {
            if (activePlacementType == PlacementType.Barricade && placedBarricadesInCurrentSetup >= MaxBarricadesPerSetup)
            {
                Debug.LogWarning("[TowerManager] Barricade limit reached for this wave!");
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

        // 2. 経路閉塞チェック (A*を用いて、コアに到達できなくなる完全閉塞を防ぐ)
        if (!CheckPathValidityWithTemporaryTower(cellPos))
        {
            Debug.Log("[TowerManager] Placement rejected: blocking the path to the core!");
            return false;
        }

        return true;
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

        if (activePlacementType == PlacementType.Barricade)
        {
            placedBarricadesInCurrentSetup++;
            OnBarricadeCountChanged?.Invoke(placedBarricadesInCurrentSetup);
        }

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
        if (activePlacementType == PlacementType.Barricade)
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

    private GameObject CreateGhostPreview(PlacementType type)
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
