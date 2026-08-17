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

    // Step 4-2: Outpost群を始点としたホップ数制限付きBFSで求めた「供給済みタワー集合」（Outpost自身も含む）を
    // プレイヤーごと（0=Blue, 1=Orange）に二重化して保持する。要素数は常に2固定で、シングルプレイでは
    // index 0 のみが使用される（index 1 は常に空のまま）。
    // 毎フレーム計算せず、配置・破壊・売却のたびにRecalculateSupplyNetwork()で再計算してキャッシュする
    private readonly HashSet<Tower>[] suppliedTowersByOwner = { new HashSet<Tower>(), new HashSet<Tower>() };
    // 各タワーのOutpostからのホップ数（Outpost自身は0）。供給されていないタワーはキーを持たない（プレイヤー別）
    private readonly Dictionary<Tower, int>[] supplyHopsByOwner = { new Dictionary<Tower, int>(), new Dictionary<Tower, int>() };
    // 不具合修正（Step 4-2 Relay Extension）: 以前は「タワー→usedAllyRelayフラグ」を1本のDictionaryで
    // 持ち、同じタワーに複数の経路（自チーム経由/相手タワー経由）が到達し得る場合でも、BFSで最初に
    // 確定した経路の値しか保持できなかった。これにより、同じ盤面でもタワーの配置順（＝activeTowersの
    // 走査順）次第でCanRelaySupply()の結果が変わってしまう不具合があった。
    // 修正後は、BFSの訪問状態を「タワー単体」ではなく「(タワー, usedAllyRelayフラグ) の組」として扱い、
    // false状態とtrue状態を独立に探索した上で、いずれかの状態が中継可能なら中継可能と判定する。
    // このHashSetはその統合結果（＝CanRelaySupply()の判定結果そのもの）をプレイヤー別にキャッシュしたもの
    private readonly HashSet<Tower>[] relayableTowersByOwner = { new HashSet<Tower>(), new HashSet<Tower>() };
    private readonly List<TowerRangeIndicator> supplyZoneIndicatorsOwn = new List<TowerRangeIndicator>();
    private readonly List<TowerRangeIndicator> supplyZoneIndicatorsAlly = new List<TowerRangeIndicator>();
    private static readonly Color SupplyZoneOverlayColor = new Color(0.3f, 1f, 0.4f, 0.35f);
    // Step 4-2: 相手ネットワークのオーバーレイ（ドラッグ中のみ・薄いグレーの塗りつぶし）
    private static readonly Color SupplyZoneOverlayAllyColor = new Color(0.7f, 0.7f, 0.7f, 0.18f);

    private readonly Dictionary<TowerType, int> placedCountsInCurrentSetup = new Dictionary<TowerType, int>();

    // Step 4-5: Outpost(バリケード)に、所有者ごと独立の通し番号(1始まり)を配置順で採番する。
    // Siege MarkerのHUD警告(「BLUE OUTPOST (3)」等)と、盤面上のOutpostの常時ラベル表示を照合するために使う
    private readonly int[] nextOutpostNumberByOwner = { 1, 1 };

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

    // Step 4-2: 指定タワーが「いずれかのプレイヤーのネットワーク」から供給済みか（Outpost自身も含む）。
    // Step 3のOffline判定に使用する。片方のネットワークが切れても、もう片方から供給されていれば
    // Onlineを維持する（＝Interlinkの「Offline耐性」効果はこのメソッドの定義だけで自動的に成立する）
    public bool IsTowerSupplied(Tower tower)
    {
        if (tower == null) return false;
        return suppliedTowersByOwner[0].Contains(tower) || suppliedTowersByOwner[1].Contains(tower);
    }

    // Step 4-2: 新規。指定タワーが「指定プレイヤーpのネットワーク」から供給済みか
    public bool IsTowerSuppliedBy(Tower tower, int ownerId)
    {
        if (tower == null || ownerId < 0 || ownerId > 1) return false;
        return suppliedTowersByOwner[ownerId].Contains(tower);
    }

    // Step 4-2: 新規。両プレイヤーの供給集合の両方に含まれるタワーを「Interlinked」と定義する。
    // Outpost自身（供給元）は対象外。シングルプレイではindex 1が常に空集合のため自動的にfalseになる
    public bool IsInterlinked(Tower tower)
    {
        if (tower == null || tower.IsBarricade) return false;
        return suppliedTowersByOwner[0].Contains(tower) && suppliedTowersByOwner[1].Contains(tower);
    }

    // Step 4-2: 両プレイヤーのネットワークのうち小さい方のホップ数を返す。どちらからも供給されていない場合は-1
    public int GetSupplyHops(Tower tower)
    {
        if (tower == null) return -1;
        bool has0 = supplyHopsByOwner[0].TryGetValue(tower, out int hops0);
        bool has1 = supplyHopsByOwner[1].TryGetValue(tower, out int hops1);
        if (!has0 && !has1) return -1;
        if (!has0) return hops1;
        if (!has1) return hops0;
        return Mathf.Min(hops0, hops1);
    }

    // Step 4-2: シグネチャ変更。指定プレイヤーpのネットワークにおいて「他タワー配置の供給元として
    // 中継可能」かどうか。供給済みであっても、ホップ数がそのタワーの有効上限（Relay Extensionが
    // 発動していればmaxSupplyHops+1、そうでなければmaxSupplyHops）に達しているタワーはこれ以上中継できない。
    // ここでtrueを返すタワーの集合が、配置判定(IsWithinSupplyRange)と供給範囲オーバーレイ両方の基準になる
    public bool CanRelaySupply(Tower tower, int ownerId)
    {
        if (tower == null || ownerId < 0 || ownerId > 1) return false;
        // 不具合修正: 以前はsupplyHopsByOwner（単一経路のホップ数）とusedAllyRelayByOwner
        // （単一経路のフラグ）を組み合わせて判定していたため、同じタワーに到達し得る複数の経路の
        // うちBFSが最初に確定させた1本しか評価できなかった。今はRecalculateSupplyNetworkForOwner()側で
        // 「(タワー, usedAllyRelayフラグ) の組」を状態として独立に探索し、いずれかの状態が中継可能なら
        // trueとなるよう統合済みの結果をrelayableTowersByOwnerにキャッシュしてあるので、ここでは参照するだけでよい
        return relayableTowersByOwner[ownerId].Contains(tower);
    }

    // Step 4-2: プレイヤーごとにBFSを実行し、3組のキャッシュ（suppliedTowersByOwner / supplyHopsByOwner /
    // relayableTowersByOwner）を求め直す。シングルプレイ（IsCoop == false）では owner 0 のみを計算し、
    // owner 1 のキャッシュは空のまま（＝IsTowerSuppliedBy(tower, 1)やIsInterlinked()が常にfalseになる）。
    // 配置・破壊・売却などタワー構成が変化するタイミングでのみ呼び出すこと（毎フレーム呼び出さない）。
    public void RecalculateSupplyNetwork()
    {
        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;
        int ownerCount = isCoop ? 2 : 1;

        for (int p = 0; p < 2; p++)
        {
            suppliedTowersByOwner[p].Clear();
            supplyHopsByOwner[p].Clear();
            relayableTowersByOwner[p].Clear();
        }

        for (int p = 0; p < ownerCount; p++)
        {
            RecalculateSupplyNetworkForOwner(p);
        }
    }

    // Step 4-2: プレイヤーpを始点としたホップ数制限付きBFS（Relay Extension対応）。
    // 始点はownerId == pのOutpostのみ。中継点は所有者を問わずあらゆるタワーが対象。
    //
    // 不具合修正: 以前は訪問状態を「タワー単体」で管理していたため、同じ深さのノードへ
    // 「自チーム経由（usedAllyRelay=false）」と「相手タワー経由（usedAllyRelay=true）」の
    // 2つの経路が両方存在する場合、activeTowersの走査順（＝タワーの配置順）次第でどちらが先に
    // 確定するかが変わり、Relay Extensionの発動が配置順に依存して不安定になっていた。
    // 修正後は訪問状態を「(タワー, usedAllyRelayフラグ) の組」として扱い、false状態とtrue状態を
    // それぞれ独立に1回ずつ訪問する（hopsByState[0]=false側、hopsByState[1]=true側）。
    // BFSはFIFOキューで幅優先に進めるため、各状態に最初に割り当てられるホップ数が必ず最小値になる
    // （hopsByState[state]に未登録の場合のみキューへ積み、既訪問状態のホップ数を上書きしない）。
    // 最後に2状態を統合し、供給済み/最小ホップ数/中継可否（いずれかの状態が中継可能なら中継可能）を求める
    private void RecalculateSupplyNetworkForOwner(int ownerId)
    {
        HashSet<Tower> supplied = suppliedTowersByOwner[ownerId];
        Dictionary<Tower, int> hops = supplyHopsByOwner[ownerId];
        HashSet<Tower> relayable = relayableTowersByOwner[ownerId];

        // 状態別のホップ数。index 0 = usedAllyRelay(false)側、index 1 = usedAllyRelay(true)側
        var hopsByState = new Dictionary<Tower, int>[] { new Dictionary<Tower, int>(), new Dictionary<Tower, int>() };

        Queue<(Tower tower, bool usedAllyRelay)> frontier = new Queue<(Tower, bool)>();
        foreach (Tower t in activeTowers)
        {
            // 始点は必ず自プレイヤー所有のOutpostのため usedAllyRelay=false の状態のみ
            if (t != null && t.IsBarricade && t.OwnerId == ownerId && !hopsByState[0].ContainsKey(t))
            {
                hopsByState[0][t] = 0;
                frontier.Enqueue((t, false));
            }
        }

        while (frontier.Count > 0)
        {
            (Tower current, bool currentAllyRelay) = frontier.Dequeue();
            int stateIndex = currentAllyRelay ? 1 : 0;
            int currentHops = hopsByState[stateIndex][current];

            // Relay Extension: 相手所有タワーを1回以上経由済みの状態なら中継上限を+1する
            int limit = currentAllyRelay ? maxSupplyHops + 1 : maxSupplyHops;
            // 中継上限に達した状態からはこれ以上辺を張らない
            // （このタワー自身はこの状態としては到達済みのまま＝供給はされるが、この経路では中継しない）
            if (currentHops >= limit) continue;

            int neighborHops = currentHops + 1;
            foreach (Tower other in activeTowers)
            {
                if (other == null || other == current) continue;

                float dist = Vector3.Distance(current.transform.position, other.transform.position);
                if (dist > supplyRadius) continue;

                // otherが相手所有タワーである、またはここまでの経路で既に相手所有タワーを
                // 経由している場合、otherの状態はusedAllyRelay=trueになる
                bool neighborAllyRelay = currentAllyRelay || other.OwnerId != ownerId;
                int neighborStateIndex = neighborAllyRelay ? 1 : 0;

                // この状態(other, neighborAllyRelay)への訪問が初めての場合のみ採用する
                // （＝各状態について最初に確定したホップ数＝最小ホップ数を上書きしない）
                if (hopsByState[neighborStateIndex].ContainsKey(other)) continue;

                hopsByState[neighborStateIndex][other] = neighborHops;
                frontier.Enqueue((other, neighborAllyRelay));
            }
        }

        // 2状態を統合する。
        // 供給済み: いずれかの状態で到達していれば供給済み
        // ホップ数: 到達した状態のうち最小のホップ数
        // 中継可能: いずれかの状態が「その状態の上限」未満のホップ数で到達していれば中継可能
        foreach (Dictionary<Tower, int> stateHops in hopsByState)
        {
            foreach (KeyValuePair<Tower, int> kv in stateHops)
            {
                Tower tower = kv.Key;
                int stateHopValue = kv.Value;
                supplied.Add(tower);

                if (!hops.TryGetValue(tower, out int bestHops) || stateHopValue < bestHops)
                {
                    hops[tower] = stateHopValue;
                }
            }
        }

        for (int state = 0; state < hopsByState.Length; state++)
        {
            bool usedAllyRelayForState = state == 1;
            int limit = usedAllyRelayForState ? maxSupplyHops + 1 : maxSupplyHops;
            foreach (KeyValuePair<Tower, int> kv in hopsByState[state])
            {
                if (kv.Value < limit)
                {
                    relayable.Add(kv.Key);
                }
            }
        }
    }

    // Step 4-2: シグネチャ変更。指定プレイヤーのOutpostが1つ以上存在するかどうか。
    // Tower.Start()から「配置確定時点で供給ルールの適用対象かどうか(requiresSupply)」を
    // 配置者本人のOutpostの有無で判定するために参照される
    public bool HasAnyOutpost(int ownerId)
    {
        foreach (Tower t in activeTowers)
        {
            if (t != null && t.IsBarricade && t.OwnerId == ownerId) return true;
        }
        return false;
    }

    // 引数なし版: 「どちらかのプレイヤーにOutpostがあるか」。CO-OP関連の呼び出し元は全てプレイヤー別版
    // (HasAnyOutpost(int))に置き換え済みのため、現状は将来の呼び出し元のために残している
    public bool HasAnyOutpost()
    {
        foreach (Tower t in activeTowers)
        {
            if (t != null && t.IsBarricade) return true;
        }
        return false;
    }

    // Step 4-2: シグネチャ変更。指定セルへの配置が「要求元プレイヤーownerIdのネットワーク」の
    // 供給範囲内かどうか。Outpost自身は供給元が不要なので常に配置可能。
    // 詰み防止: そのプレイヤーのOutpostが1つも無い場合はそのプレイヤーの配置チェックのみをスキップする
    // （相方がOutpostを置いていても、自分のOutpostが0個ならスキップされる＝プレイヤー別の詰み防止）。
    // 判定は「供給済みタワーのいずれかから」ではなく「中継可能(CanRelaySupply)なタワーの
    // いずれかから」に限定する。単に供給済みというだけで判定すると、ホップ数上限に達したタワーの隣に
    // 新規タワーを置けてしまい、置いた瞬間にホップ数超過でOfflineになる不整合が生じるため
    private bool IsWithinSupplyRange(TowerType type, Vector3Int cellPos, int ownerId)
    {
        if (type == TowerType.Barricade) return true;
        if (!HasAnyOutpost(ownerId)) return true;

        Vector3 worldPos = MapManager.Instance.GridToWorld(cellPos);
        foreach (Tower tower in activeTowers)
        {
            if (tower == null || !CanRelaySupply(tower, ownerId)) continue;
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

        // 3. Step 2 / 4-2: Outpost供給ネットワークの範囲チェック（Outpost自身は対象外）。
        //    要求元プレイヤー（現状はGameManager.ActiveOwnerId）のネットワークのみで判定する
        int requestingOwnerId = GameManager.Instance != null ? GameManager.Instance.ActiveOwnerId : 0;
        if (!IsWithinSupplyRange(activePlacementType, cellPos, requestingOwnerId))
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
        GameObject towerObj = Instantiate(GetPlacementPrefab(activePlacementType), spawnWorldPos, Quaternion.identity);

        // Step 4-1: 生成直後（Start()より前）に要求元プレイヤーのownerIdを書き込む
        Tower tower = towerObj != null ? towerObj.GetComponent<Tower>() : null;
        int placingOwnerId = GameManager.Instance != null ? GameManager.Instance.ActiveOwnerId : 0;
        if (tower != null && GameManager.Instance != null)
        {
            tower.SetOwner(placingOwnerId);
        }

        // Step 4-5: Outpostの場合のみ、所有者ごとに独立した通し番号を配置順で採番する
        if (tower != null && tower.IsBarricade)
        {
            int idx = (placingOwnerId >= 0 && placingOwnerId < nextOutpostNumberByOwner.Length) ? placingOwnerId : 0;
            tower.SetOutpostNumber(nextOutpostNumberByOwner[idx]++);
        }

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

            // 現在の敵のグリッド座標から目標地点までの経路を再取得
            // Step 4-5: 目標地点はenemy.GetPathTargetGridPos()に委ねる（Siege Markerはマーク対象Outpostの
            // セル、それ以外は常にコア）。
            // 注意: マーク対象Outpostがこの再計算のトリガー自体（Tower.Die()等）である場合でも、
            // UnityのDestroy()はフレーム終端まで実際の破棄を遅延させるため、この時点ではmarkedOutpostは
            // まだ有効な参照のままであり、GetPathTargetGridPos()は破棄中のOutpostのセルを返し続ける
            // （自動的にコアへ切り替わったりはしない）。マーク対象消失後の経路の取り直しは
            // Enemy.Update()側（siegeTargetLostの検知）で別途行われる
            Vector3Int enemyGridPos = MapManager.Instance.WorldToGrid(enemy.transform.position);
            Vector3Int targetGridPos = enemy.GetPathTargetGridPos();
            List<Vector3> newPath = pathfinder.FindPath(enemyGridPos, targetGridPos, enemy.IgnoreTowers, enemy.AvoidThreats);
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

    // Step 2 / 4-2: ドラッグ中、中継可能な各タワー（Outpost含む。CanRelaySupply=true）を中心に
    // 半径supplyRadiusのオーバーレイを重ねて表示し、配置可能なエリアを可視化する。
    // 自分（要求元プレイヤー）のネットワークは従来どおり緑、CO-OP時は相手のネットワークも
    // 薄いグレーの塗りつぶしで重ねて表示する（噛み合わせ位置の確認用。ドラッグ中のみ＝このメソッドが
    // 呼ばれている間だけの表示になる。UpdateSupplyZoneOverlay()自体がドラッグ中しか呼ばれないため）。
    // 表示対象は「供給済み全部」ではなく「中継可能なタワー」に絞る。これは配置判定
    // (IsWithinSupplyRange)と表示を一致させるためであり、副次的にタワーが増えた際の円の
    // 重なりも軽減される（ホップ上限に達したタワーは円を描かなくなるため）。
    // Outpostをドラッグ中はどこでも置けるため両方とも表示不要
    private void UpdateSupplyZoneOverlay()
    {
        int ownerId = GameManager.Instance != null ? GameManager.Instance.ActiveOwnerId : 0;

        // 自分のネットワーク（緑）。盤面に自分のOutpostが無い場合
        // （詰み防止でチェック自体がスキップされるため）表示不要
        if (activePlacementType == TowerType.Barricade || !HasAnyOutpost(ownerId))
        {
            HideSupplyZoneIndicatorList(supplyZoneIndicatorsOwn);
        }
        else
        {
            int shown = 0;
            foreach (Tower tower in activeTowers)
            {
                if (tower == null || !CanRelaySupply(tower, ownerId)) continue;

                TowerRangeIndicator indicator = GetOrCreateSupplyZoneIndicator(supplyZoneIndicatorsOwn, shown, SupplyZoneOverlayColor);
                indicator.transform.position = tower.transform.position;
                indicator.UpdateRange(supplyRadius);
                indicator.SetVisible(true);
                shown++;
            }
            for (int i = shown; i < supplyZoneIndicatorsOwn.Count; i++)
            {
                if (supplyZoneIndicatorsOwn[i] != null) supplyZoneIndicatorsOwn[i].SetVisible(false);
            }
        }

        // Step 4-2: 相手のネットワーク（薄いグレー）。CO-OP時のみ、かつOutpostドラッグ中は表示不要
        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;
        if (!isCoop || activePlacementType == TowerType.Barricade)
        {
            HideSupplyZoneIndicatorList(supplyZoneIndicatorsAlly);
        }
        else
        {
            int allyOwnerId = ownerId == 0 ? 1 : 0;
            int shown = 0;
            foreach (Tower tower in activeTowers)
            {
                if (tower == null || !CanRelaySupply(tower, allyOwnerId)) continue;

                TowerRangeIndicator indicator = GetOrCreateSupplyZoneIndicator(supplyZoneIndicatorsAlly, shown, SupplyZoneOverlayAllyColor);
                indicator.transform.position = tower.transform.position;
                indicator.UpdateRange(supplyRadius);
                indicator.SetVisible(true);
                shown++;
            }
            for (int i = shown; i < supplyZoneIndicatorsAlly.Count; i++)
            {
                if (supplyZoneIndicatorsAlly[i] != null) supplyZoneIndicatorsAlly[i].SetVisible(false);
            }
        }
    }

    // インジケータをプールして使い回す（毎フレームの生成/破棄を避ける）。自分用/相手用でプールを分ける
    private TowerRangeIndicator GetOrCreateSupplyZoneIndicator(List<TowerRangeIndicator> pool, int index, Color color)
    {
        if (index < pool.Count && pool[index] != null)
        {
            return pool[index];
        }

        GameObject obj = new GameObject($"SupplyZoneIndicator_{pool.Count}");
        TowerRangeIndicator indicator = obj.AddComponent<TowerRangeIndicator>();
        indicator.Init(supplyRadius, color);

        if (index < pool.Count)
        {
            pool[index] = indicator;
        }
        else
        {
            pool.Add(indicator);
        }
        return indicator;
    }

    private void HideSupplyZoneOverlay()
    {
        HideSupplyZoneIndicatorList(supplyZoneIndicatorsOwn);
        HideSupplyZoneIndicatorList(supplyZoneIndicatorsAlly);
    }

    private void HideSupplyZoneIndicatorList(List<TowerRangeIndicator> pool)
    {
        foreach (TowerRangeIndicator indicator in pool)
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
        foreach (TowerRangeIndicator indicator in supplyZoneIndicatorsOwn)
        {
            if (indicator != null)
            {
                Destroy(indicator.gameObject);
            }
        }
        foreach (TowerRangeIndicator indicator in supplyZoneIndicatorsAlly)
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
