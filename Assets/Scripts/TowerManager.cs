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

    [Header("Outpost Supply Network (Step 2)")]
    // Outpost（バリケード）、またはOutpostに連結済みのタワーから、この半径以内にのみ他のタワーを配置できる。
    // 4近傍の厳密隣接にしない理由: Enemy4(Bomber, splashRadius 1.5)が密集配置を罰する設計と衝突するため。
    // 半径2.5なら市松状の配置が可能になり両立する
    [SerializeField] private float supplyRadius = 2.5f;

    // 追加対応: 中継ホップ数の上限。Outpostを深さ0とし、そこから辺をmaxSupplyHops回まで辿れる
    // タワーだけを「供給済み」とする。無制限に中継させると、盤面全体が1つの連結成分になった時点で
    // その中にOutpostが1つでも残っていれば全タワーがOnlineのままになり、Step 3のOfflineカスケードが
    // 実質発火しなくなる問題が判明したため導入した（詳細は仕様書「Outpost供給ネットワーク」参照）
    [SerializeField] private int maxSupplyHops = 2;

    // Outpost群を始点としたホップ数制限付きBFSで求めた「供給済みタワー集合」（Outpost自身も含む）。
    // 毎フレーム計算せず、配置・破壊・売却のたびにRecalculateSupplyNetwork()で再計算してキャッシュする
    private readonly HashSet<Tower> suppliedTowers = new HashSet<Tower>();
    // 各タワーのOutpostからのホップ数（Outpost自身は0）。供給されていないタワーはキーを持たない
    private readonly Dictionary<Tower, int> supplyHops = new Dictionary<Tower, int>();
    private readonly List<TowerRangeIndicator> supplyZoneIndicators = new List<TowerRangeIndicator>();
    private static readonly Color SupplyZoneOverlayColor = new Color(0.3f, 1f, 0.4f, 0.35f);

    private readonly Dictionary<TowerType, int> placedCountsInCurrentSetup = new Dictionary<TowerType, int>();

    // 引数: (種別, そのSetupフェーズでの現在の設置数)
    public event System.Action<TowerType, int> OnPlacedCountChanged;

    // Step 2: 配置が拒否された際、理由をUIに伝えるためのイベント（英語のトースト文言）
    public event System.Action<string> OnPlacementRejected;

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
            // Step 2: タワー構成が変わったため供給ネットワークを再計算する
            RecalculateSupplyNetwork();
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
            // Step 2: タワー構成が変わったため供給ネットワークを再計算する
            RecalculateSupplyNetwork();
        }
    }

    // Step 2: 指定タワーがOutpost供給ネットワークに接続済みか（Outpost自身も含む。
    // ホップ数上限内であることは suppliedTowers への追加時点で保証されているため、意味は変更していない）。
    // Step 3のOffline判定に使用する
    public bool IsTowerSupplied(Tower tower)
    {
        return tower != null && suppliedTowers.Contains(tower);
    }

    // 追加対応: 指定タワーのOutpostからのホップ数を返す。供給されていない場合は-1
    public int GetSupplyHops(Tower tower)
    {
        if (tower == null) return -1;
        return supplyHops.TryGetValue(tower, out int hops) ? hops : -1;
    }

    // 追加対応: 指定タワーが「他タワー配置の供給元として中継可能」かどうか。
    // 供給済みであっても、ホップ数が上限(maxSupplyHops)に達しているタワーはこれ以上中継できない。
    // ここでtrueを返すタワーの集合が、配置判定(IsWithinSupplyRange)と供給範囲オーバーレイ両方の基準になる
    public bool CanRelaySupply(Tower tower)
    {
        if (tower == null) return false;
        return supplyHops.TryGetValue(tower, out int hops) && hops < maxSupplyHops;
    }

    // Step 2: Outpost群を深さ0の始点に、供給半径supplyRadius・中継上限maxSupplyHopsで辺を張った
    // ホップ数制限付きBFSで供給済みタワー集合を求め直す。
    // 配置・破壊・売却などタワー構成が変化するタイミングでのみ呼び出すこと（毎フレーム呼び出さない）。
    // BFSはFIFOキューで幅優先に進めるため、各タワーに最初に割り当てられるホップ数が必ず最小値になる
    // （suppliedTowers.Add()に成功した場合のみキューへ積み、既訪問ノードのホップ数を上書きしない）
    public void RecalculateSupplyNetwork()
    {
        suppliedTowers.Clear();
        supplyHops.Clear();

        Queue<Tower> frontier = new Queue<Tower>();
        foreach (Tower t in activeTowers)
        {
            if (t != null && t.IsBarricade && suppliedTowers.Add(t))
            {
                supplyHops[t] = 0;
                frontier.Enqueue(t);
            }
        }

        while (frontier.Count > 0)
        {
            Tower current = frontier.Dequeue();
            int currentHops = supplyHops[current];

            // 中継上限に達したタワーからはこれ以上辺を張らない
            // （このタワー自身はsuppliedTowersに入ったまま＝供給はされるが、中継はしない）
            if (currentHops >= maxSupplyHops) continue;

            int neighborHops = currentHops + 1;
            foreach (Tower other in activeTowers)
            {
                if (other == null || suppliedTowers.Contains(other)) continue;

                float dist = Vector3.Distance(current.transform.position, other.transform.position);
                if (dist <= supplyRadius)
                {
                    suppliedTowers.Add(other);
                    supplyHops[other] = neighborHops;
                    frontier.Enqueue(other);
                }
            }
        }
    }

    // 盤面にOutpostが1つ以上存在するかどうか。
    // Step 3: Tower.Start()から「配置確定時点で供給ルールの適用対象かどうか(requiresSupply)」を
    // 判定するために参照されるため公開する
    public bool HasAnyOutpost()
    {
        foreach (Tower t in activeTowers)
        {
            if (t != null && t.IsBarricade) return true;
        }
        return false;
    }

    // Step 2: 指定セルへの配置が供給範囲内かどうか。Outpost自身は供給元が不要なので常に配置可能。
    // 盤面にOutpostが1つも無い場合は詰み防止のためチェックをスキップする（Wave1でOutpost未設置のまま
    // 通常タワーが一切置けなくなる事態を避けるための措置）。
    // 追加対応: 判定は「供給済みタワーのいずれかから」ではなく「中継可能(CanRelaySupply)なタワーの
    // いずれかから」に限定する。単に供給済みというだけで判定すると、ホップ数上限に達したタワーの隣に
    // 新規タワーを置けてしまい、置いた瞬間にホップ数超過でOfflineになる不整合が生じるため
    private bool IsWithinSupplyRange(TowerType type, Vector3Int cellPos)
    {
        if (type == TowerType.Barricade) return true;
        if (!HasAnyOutpost()) return true;

        Vector3 worldPos = MapManager.Instance.GridToWorld(cellPos);
        foreach (Tower tower in activeTowers)
        {
            if (tower == null || !CanRelaySupply(tower)) continue;
            if (Vector3.Distance(worldPos, tower.transform.position) <= supplyRadius) return true;
        }
        return false;
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
            // Step 2: バリケードは供給ネットワークの起点「Outpost」として再定義。
            // TowerType.Barricade のenum名・Prefab名（Barricade.prefab）・IsBarricadeプロパティ名は
            // 互換性維持のため変更しない（表示名とゲームデザイン上の役割だけが変わる）
            new TowerDefinition { type = TowerType.Barricade, cost = 0, unlockWave = 1, maxPerSetup = 3, displayName = "Outpost" },
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
            UpdateSupplyZoneOverlay();
        }
        else
        {
            HidePlacementPreview();
            HideSupplyZoneOverlay();
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

        PlacementRejectionReason reason = GetPlacementRejectionReason(cellPos);
        if (reason == PlacementRejectionReason.None)
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
            ReportPlacementRejection(reason, cellPos);
        }
    }

    // Step 2: 配置拒否の理由を区別するための分類。Debug.Logとトーストメッセージの両方をこれ1箇所から出す
    private enum PlacementRejectionReason
    {
        None,
        Terrain,
        EnemyOccupied,
        OutOfSupplyRange,
        PathBlocked,
    }

    public bool ValidateTowerPlacement(Vector3Int cellPos)
    {
        return GetPlacementRejectionReason(cellPos) == PlacementRejectionReason.None;
    }

    // 判定順序: 地形 → 敵占有セル(防衛フェーズのみ) → Step 2: 供給範囲 → 経路閉塞(A*)。
    // A*が最も重い処理なので最後に回す
    private PlacementRejectionReason GetPlacementRejectionReason(Vector3Int cellPos)
    {
        // 1. 地形や重複のチェック (壁、他のタワー、コア、スポーンポイント等)
        if (!MapManager.Instance.CanPlaceTower(cellPos))
        {
            return PlacementRejectionReason.Terrain;
        }

        // 2. Step 1: 防衛フェーズ中の緊急設置時のみ、敵が立っているセルへの設置を禁止する
        //    （Setupフェーズ中は敵が存在しないため判定不要）
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Defense
            && IsEnemyOccupyingCell(cellPos))
        {
            return PlacementRejectionReason.EnemyOccupied;
        }

        // 3. Step 2: Outpost供給ネットワークの範囲チェック（Outpost自身は対象外）
        if (!IsWithinSupplyRange(activePlacementType, cellPos))
        {
            return PlacementRejectionReason.OutOfSupplyRange;
        }

        // 4. 経路閉塞チェック (A*を用いて、コアに到達できなくなる完全閉塞を防ぐ)
        //    防衛フェーズ中の緊急設置でも必ず実行する
        if (!CheckPathValidityWithTemporaryTower(cellPos))
        {
            return PlacementRejectionReason.PathBlocked;
        }

        return PlacementRejectionReason.None;
    }

    // Debug.Logとトースト表示（OnPlacementRejectedイベント）を理由ごとに分けて発行する。
    // プレビュー中(毎フレーム)ではなく、実際に配置を試みた瞬間のみ呼び出すこと
    private void ReportPlacementRejection(PlacementRejectionReason reason, Vector3Int cellPos)
    {
        switch (reason)
        {
            case PlacementRejectionReason.Terrain:
                Debug.Log($"[TowerManager] Cannot place tower at {cellPos}: terrain or occupied cell.");
                OnPlacementRejected?.Invoke("Cannot place here");
                break;
            case PlacementRejectionReason.EnemyOccupied:
                Debug.Log($"[TowerManager] Placement rejected: an enemy occupies {cellPos}!");
                OnPlacementRejected?.Invoke("An enemy is standing here");
                break;
            case PlacementRejectionReason.OutOfSupplyRange:
                Debug.Log($"[TowerManager] Placement rejected: out of outpost supply range at {cellPos}!");
                OnPlacementRejected?.Invoke("Out of outpost supply range");
                break;
            case PlacementRejectionReason.PathBlocked:
                Debug.Log("[TowerManager] Placement rejected: blocking the path to the core!");
                OnPlacementRejected?.Invoke("This would block the path to the core");
                break;
        }
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

        // タワー配置が完了したため、既存の敵について経路を再計算させる
        NotifyEnemiesToRecalculatePath();

        // Step 2: 供給ネットワークを再計算する。新規タワーのStart()（RegisterTower経由）でも
        // 再計算されるが、Start()の実行はフレームを跨ぐ場合があるため、ここでも明示的に呼んでおく
        RecalculateSupplyNetwork();
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

    // Step 2: ドラッグ中、中継可能な各タワー（Outpost含む。CanRelaySupply=true）を中心に
    // 半径supplyRadiusの緑オーバーレイを重ねて表示し、配置可能なエリアを可視化する。
    // 追加対応: 表示対象を「供給済み全部」から「中継可能なタワー」に絞った。これは配置判定
    // (IsWithinSupplyRange)と表示を一致させるためであり、副次的にタワーが増えた際の円の
    // 重なりも軽減される（ホップ上限に達したタワーは円を描かなくなるため）。
    // Outpostをドラッグ中はどこでも置けるため表示不要。盤面にOutpostが無い場合も
    // （詰み防止でチェック自体がスキップされるため）表示不要
    private void UpdateSupplyZoneOverlay()
    {
        if (activePlacementType == TowerType.Barricade || !HasAnyOutpost())
        {
            HideSupplyZoneOverlay();
            return;
        }

        int shown = 0;
        foreach (Tower tower in activeTowers)
        {
            if (tower == null || !CanRelaySupply(tower)) continue;

            TowerRangeIndicator indicator = GetOrCreateSupplyZoneIndicator(shown);
            indicator.transform.position = tower.transform.position;
            indicator.UpdateRange(supplyRadius);
            indicator.SetVisible(true);
            shown++;
        }

        // 前フレームより供給元の数が減った場合、余ったインジケータを隠す
        for (int i = shown; i < supplyZoneIndicators.Count; i++)
        {
            if (supplyZoneIndicators[i] != null)
            {
                supplyZoneIndicators[i].SetVisible(false);
            }
        }
    }

    // インジケータをプールして使い回す（毎フレームの生成/破棄を避ける）
    private TowerRangeIndicator GetOrCreateSupplyZoneIndicator(int index)
    {
        if (index < supplyZoneIndicators.Count && supplyZoneIndicators[index] != null)
        {
            return supplyZoneIndicators[index];
        }

        GameObject obj = new GameObject($"SupplyZoneIndicator_{index}");
        TowerRangeIndicator indicator = obj.AddComponent<TowerRangeIndicator>();
        indicator.Init(supplyRadius, SupplyZoneOverlayColor);

        if (index < supplyZoneIndicators.Count)
        {
            supplyZoneIndicators[index] = indicator;
        }
        else
        {
            supplyZoneIndicators.Add(indicator);
        }
        return indicator;
    }

    private void HideSupplyZoneOverlay()
    {
        foreach (TowerRangeIndicator indicator in supplyZoneIndicators)
        {
            if (indicator != null)
            {
                indicator.SetVisible(false);
            }
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
        foreach (TowerRangeIndicator indicator in supplyZoneIndicators)
        {
            if (indicator != null)
            {
                Destroy(indicator.gameObject);
            }
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
    }
}
