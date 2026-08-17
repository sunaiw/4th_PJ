using System.Collections.Generic;
using UnityEngine;

public enum TowerType
{
    Normal = 0,
    Healer = 1,
    Barricade = 2,
    Tank = 3,
    Splash = 4,
    Frost = 5,
}

public class Tower : MonoBehaviour, IDamageable
{
    private static readonly List<Enemy> emptyEnemyList = new List<Enemy>(0);
    private static readonly List<Tower> emptyTowerList = new List<Tower>(0);
    [Header("Tower Attributes")]
    [SerializeField] private float range = 3.0f;
    [SerializeField] private float fireRate = 1.0f; // 1秒間の弾数
    [SerializeField] private float damage = 2f;
    [SerializeField] private float maxHp = 5f;
    [SerializeField] private float armor = 0f;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private TowerType towerType = TowerType.Normal;

    public TowerType Type => towerType;

    // 既存の呼び出し箇所（Enemy.cs / TowerManager.cs / AStarPathfinding.cs）との
    // 互換性を保つため、判定用プロパティはenumから導出する形で残す
    public bool IsBarricade => towerType == TowerType.Barricade;
    public bool IsHealer => towerType == TowerType.Healer;

    private float currentHp;
    private float fireCooldown = 0f;

    private TowerRangeIndicator rangeIndicator;
    private HealthDisplay healthDisplay;

    // C-2: ターゲットキャッシュ
    private Enemy cachedTarget = null;
    private float targetSearchCooldown = 0f;

    private int placedWave;
    public int PlacedWave => placedWave;
    private int buildCost;

    private SpriteRenderer spriteRenderer;
    private Color originalSpriteColor = Color.white;

    private float baseRange;
    private float baseFireRate;
    private float baseDamage;
    private float baseMaxHp;
    private float baseArmor;

    private bool isRangeVisible = false;

    // Frost Action パラメータ
    private float frostSlowPercent = 0f;
    private float frostSlowDuration = 1.0f;

    // 敵デバッファーから受ける攻撃速度低下。1.0で通常、値が小さいほど遅い。
    private float attackSpeedDebuffMultiplier = 1f;
    private float attackSpeedDebuffTimer = 0f;

    // Piercing Shot パラメータ
    private bool piercingEnabled = false;
    private float piercingDamageRatio = 0f;

    // Healerからの被回復量の上限（複数Healerを集めても際限なく回復し続けないようにする）
    [SerializeField] private float maxHealPercentPerSecond = 0.15f;

    [Header("Splash Attack")]
    // 0より大きい場合のみ範囲攻撃になる（スプラッシュタワー用。他の種別はPrefabに項目が無く0のまま）
    [SerializeField] private float splashRadius = 0f;
    [SerializeField] private float splashDamageRatio = 1.0f;

    [Header("Slow Debuff Attack")]
    // 0より大きい場合、命中したエネミーを減速させる（フロストタワー用）
    [SerializeField] private float debuffSlowPercent = 0f;
    [SerializeField] private float debuffSlowDuration = 0f;
    private float healBudgetWindowStart = 0f;
    private float healReceivedInWindow = 0f;

    [Header("Outpost Supply (Step 3)")]
    // 供給ネットワークから切断されてからOfflineになるまでの猶予秒数
    [SerializeField] private float offlineGraceDuration = 3.0f;

    [Header("CO-OP Rescue (Step 4-2)")]
    // CO-OP時のみ適用される猶予秒数。3秒では「相方に気づいてもらい、口頭で伝え、カーソルを移動して
    // 設置する」時間が足りないため、CO-OP時のみ5秒に延長する
    [SerializeField] private float coopOfflineGraceDuration = 5.0f;

    // 実効グレース秒数。CO-OP時のみcoopOfflineGraceDurationを、それ以外はofflineGraceDurationを返す
    private float EffectiveOfflineGraceDuration =>
        (GameManager.Instance != null && GameManager.Instance.IsCoop) ? coopOfflineGraceDuration : offlineGraceDuration;

    // Step 2で「盤面にOutpostが1つも無い間は供給範囲チェックをスキップする」詰み対策を入れたため、
    // その間に配置されたタワーが後からOutpostが置かれた瞬間に一斉Offline化する理不尽を防ぐ必要がある。
    // 配置確定時（Start()）に、その時点で盤面にOutpostが存在したかどうかを記録し、以後変化しない。
    // falseの場合はStep 3の供給ルールの適用対象外（恒久的にOnline扱い）
    private bool requiresSupply = false;

    private bool isOffline = false;
    // >=0: 供給が途切れてからの猶予秒数をカウントダウン中。-1: カウントダウンしていない（供給中/対象外）
    private float offlineGraceTimer = -1f;

    public bool IsOffline => isOffline;

    // Step 4-2: 前フレームのInterlink状態。変化検知にのみ使う（Update()参照）
    private bool wasInterlinked = false;

    [Header("CO-OP Ownership (Step 4-1)")]
    // 所有者プレイヤーのownerId（0 = Blue / 1 = Orange）。配置時にSetOwner()で外部から設定され、以後不変
    public int OwnerId { get; private set; } = 0;

    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);
    private SpriteRenderer ownerOutlineRenderer;

    // Step 4-6: 通常タワー(Tower.prefab)のスプライトが青色であるため、Blue所有者のアウトラインが
    // スプライト自体の色と同化して視認できない不具合の対策。タワー本体より手前に、所有者カラーの
    // 小さな正方形バッジを追加で表示する（スプライトの色に関係なく必ず判別できるようにするため）
    private SpriteRenderer ownerBadgeRenderer;
    private SpriteRenderer ownerBadgeBorderRenderer;
    private static readonly Color OwnerBadgeBorderColor = new Color(0.1f, 0.1f, 0.1f);
    // 実行時生成するバッジ用の正方形Spriteは全タワーで1枚だけ共有する静的キャッシュ
    // （タワーごとに生成するとTexture2D/SpriteがDestroy時にリークするため）
    private static Sprite ownerBadgeSprite;

    // TowerManager.SpawnTower()からタワー生成直後に呼ばれる。Start()より前（Instantiate直後）に呼ばれる想定
    public void SetOwner(int ownerId)
    {
        OwnerId = ownerId;
    }

    [Header("CO-OP Siege Marker (Step 4-5)")]
    // Outpost(バリケード)のみが持つ、所有者ごと独立の通し番号(1始まり、配置順)。
    // TowerManager.SpawnTower()からInstantiate直後（Start()より前）にSetOutpostNumber()で書き込まれる
    public int OutpostNumber { get; private set; } = 0;

    // Siege Markerにマークされている間だけtrue。複数体のSiege Markerが同じOutpostをマークする
    // 稀なケースにも対応できるよう、bool単独ではなく参照カウントで管理する
    private int siegeMarkCount = 0;
    private bool isSiegeMarked = false;
    private TowerRangeIndicator siegeMarkerIndicator;
    private TextMesh outpostNumberLabel;
    private static readonly Color SiegeMarkerTrackColor = new Color(0.75f, 0.0f, 0.65f);

    public void SetOutpostNumber(int number)
    {
        OutpostNumber = number;
        if (outpostNumberLabel != null)
        {
            outpostNumberLabel.text = $"#{OutpostNumber}";
        }
    }

    // Step 4-5: Enemy.SetupSiegeMarker() / Enemy.DestroySelf()から呼ばれる。
    // マークされている間、盤面上に点滅する追跡マーカーを表示し続ける（Siege Markerが倒されるか
    // このOutpost自身が破壊されるまで維持する）
    public void SetSiegeMarked(bool marked)
    {
        siegeMarkCount = Mathf.Max(0, siegeMarkCount + (marked ? 1 : -1));
        isSiegeMarked = siegeMarkCount > 0;

        if (isSiegeMarked)
        {
            CreateSiegeMarkerIndicatorIfNeeded();
            if (siegeMarkerIndicator != null) siegeMarkerIndicator.SetVisible(true);
        }
        else if (siegeMarkerIndicator != null)
        {
            siegeMarkerIndicator.SetVisible(false);
        }
    }

    private void CreateSiegeMarkerIndicatorIfNeeded()
    {
        if (siegeMarkerIndicator != null) return;

        GameObject obj = new GameObject("SiegeTrackerMarker");
        obj.transform.SetParent(transform);
        obj.transform.localPosition = Vector3.zero;

        siegeMarkerIndicator = obj.AddComponent<TowerRangeIndicator>();
        siegeMarkerIndicator.Init(1.3f, SiegeMarkerTrackColor); // タワー本体より一回り大きい点滅枠
    }

    // Step 4-5: Barricade(Outpost)のUpdate()は攻撃ロジックを持たないため早期returnするが、
    // 追跡マーカーの点滅アニメーションはその早期returnより前に動かす必要があるため専用メソッドに分離する
    private void UpdateSiegeTrackerVisual()
    {
        if (!isSiegeMarked || siegeMarkerIndicator == null) return;

        float blink = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f; // 0..1で明滅
        Color c = SiegeMarkerTrackColor;
        c.a = Mathf.Lerp(0.25f, 0.9f, blink);
        siegeMarkerIndicator.SetColor(c);
    }

    // Step 4-5: 盤面上のOutpostに識別番号を常時ラベル表示する（Siege MarkerのHUD警告と照合するため）。
    // CO-OP時のみ生成し、シングルプレイでは生成しない（既存の見た目を変えないため）
    private void CreateOutpostNumberLabel()
    {
        GameObject labelObj = new GameObject("OutpostNumberLabel");
        labelObj.transform.SetParent(transform);
        labelObj.transform.localPosition = new Vector3(0f, -0.65f, -1f);

        outpostNumberLabel = labelObj.AddComponent<TextMesh>();
        outpostNumberLabel.text = $"#{OutpostNumber}";
        outpostNumberLabel.fontSize = 48;
        outpostNumberLabel.characterSize = 0.15f;
        outpostNumberLabel.anchor = TextAnchor.MiddleCenter;
        outpostNumberLabel.alignment = TextAlignment.Center;
        outpostNumberLabel.color = OwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;

        MeshRenderer meshRenderer = labelObj.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingOrder = 100;
            meshRenderer.sortingLayerName = "Default";
        }
    }

    private void Awake()
    {
        baseRange = range;
        baseFireRate = fireRate;
        baseDamage = damage;
        baseMaxHp = maxHp;
        baseArmor = armor;

        // Step 1: currentHpの初期化。
        // 通常タワーはこの後Start()内のUpdateStatsFromRewards()でも初期化されるため実質無害だが、
        // バリケードはUpdateStatsFromRewards()を早期リターンして通らないため、ここで確実にHPを持たせる。
        currentHp = maxHp;
    }

    public void UpdateStatsFromRewards()
    {
        if (IsBarricade) return;
        if (RewardManager.Instance != null)
        {
            ApplyRewardStats();
        }
        else
        {
            // 不具合修正: RewardManagerが存在しない場合もfireRateをベース値から再構築してから
            // Interlink倍率を掛ける（ApplyRewardStats()を通らないためここでリセットしないと多重適用の原因になる）
            fireRate = baseFireRate;
        }

        // Step 4-2: Interlink（両プレイヤーの供給集合の両方に含まれるタワー）は攻撃速度
        // （ヒーラーの回復速度にも同じfireRateが使われるため自動的に適用される）を+20%する。
        // 報酬バフによる最終ステータス算出の最後に掛けることで、報酬倍率と正しく重畳する。
        // シングルプレイやOutpostではIsInterlinked()が常にfalseを返すため無効となる。
        // 不具合修正: fireRateはApplyRewardStats()内で常にbaseFireRateから再構築される（下記参照）ため、
        // ここでの1.2倍はInterlink成立時に毎回「ベース値×報酬倍率」に対して掛かり、解除時に元へ戻る。
        // 以前はSpeed UP報酬を1回も取得していない場合にfireRateがリセットされず、
        // Interlinkの成立・解除を繰り返すたびに1.2倍が複利で重なっていた
        if (TowerManager.Instance != null && TowerManager.Instance.IsInterlinked(this))
        {
            fireRate *= 1.2f;
        }
    }

    // 報酬（ローグライク）バフから最終ステータスを算出する（UpdateStatsFromRewards()から分離）
    private void ApplyRewardStats()
    {
        var counts = RewardManager.Instance.GetAcquiredRewardCounts();

        // 攻撃力UP: 獲得数*10%
        if (counts.TryGetValue(RewardType.IncreaseTowerDamage, out int dmgCount))
        {
            damage = baseDamage * (1f + dmgCount * 0.1f);
        }

        // 攻撃速度UP: 獲得数*10%
        // 不具合修正: 報酬を1回も取得していない場合（frCount==0）でも必ずbaseFireRateから再構築する。
        // Interlink（TowerManager.UpdateStatsFromRewards()側で+20%）のON/OFFが繰り返されても、
        // ここで毎回ベース値からfireRateを作り直すため多重適用（複利増幅）が起きなくなる
        int frCount = counts.TryGetValue(RewardType.IncreaseTowerFireRate, out int frc) ? frc : 0;
        fireRate = baseFireRate * (1f + frCount * 0.1f);

        // 攻撃範囲UP: 獲得数*10%
        if (counts.TryGetValue(RewardType.IncreaseTowerRange, out int rangeCount))
        {
            Range = baseRange * (1f + rangeCount * 0.1f);
        }

        // HPUP: 複利 1.15^hpCount (1スタックあたり15%上昇)
        if (counts.TryGetValue(RewardType.IncreaseTowerMaxHP, out int hpCount))
        {
            float prevMaxHp = maxHp;
            maxHp = baseMaxHp * Mathf.Pow(1.15f, hpCount);

            // 初期化時以外は、現在のHPも割合で増減させる
            if (prevMaxHp > 0 && currentHp > 0)
            {
                float ratio = maxHp / prevMaxHp;
                currentHp = Mathf.Min(maxHp, currentHp * ratio);
            }
            else
            {
                currentHp = maxHp;
            }
            UpdateHPText();
        }

        // アーマーUP: 獲得数*5% (軽減率+5%)
        if (counts.TryGetValue(RewardType.IncreaseTowerArmor, out int armorCount))
        {
            Armor = baseArmor + armorCount * 5f;
        }

        // Frost Action: スタック数 × 15%のスロウ率（上限60%）
        if (counts.TryGetValue(RewardType.FrostAction, out int frostCount))
        {
            frostSlowPercent = Mathf.Min(frostCount * 0.15f, 0.60f);
            frostSlowDuration = 1.0f;
        }

        // Piercing Shot: 初期50%、スタックごとに+10%（上限100%）
        if (counts.TryGetValue(RewardType.PiercingShot, out int pierceCount))
        {
            piercingEnabled = pierceCount > 0;
            piercingDamageRatio = pierceCount > 0 ? Mathf.Min(0.50f + (pierceCount - 1) * 0.10f, 1.0f) : 0f;
        }
    }

    // ローグライク報酬での強化などに使えるようプロパティを公開
    public float Range 
    { 
        get => range; 
        set 
        { 
            range = value; 
            if (rangeIndicator != null)
            {
                rangeIndicator.UpdateRange(range);
            }
        } 
    }
    public float FireRate { get => fireRate; set => fireRate = value; }
    public float Damage { get => damage; set => damage = value; }

    public float Armor
    {
        get => armor;
        set => armor = Mathf.Clamp(value, 0f, CombatUtils.MaxArmor);
    }

    private void Update()
    {
        // Step 4-5: Siege Marker追跡マーカーの点滅は、Barricade(Outpost)自身の早期returnより前に
        // 処理する必要がある（Outpostは後続の攻撃ロジックを持たないため早期returnするが、
        // 点滅アニメーションはこの早期returnの影響を受けずに動かす必要があるため）
        UpdateSiegeTrackerVisual();

        if (IsBarricade) return;

        // デバフタイマーの更新（フェーズを問わず実時間で減衰させる。Enemy.slowTimerと同じ扱い）
        if (attackSpeedDebuffTimer > 0f)
        {
            attackSpeedDebuffTimer -= Time.deltaTime;
            if (attackSpeedDebuffTimer <= 0f)
            {
                attackSpeedDebuffMultiplier = 1f;
                attackSpeedDebuffTimer = 0f;
            }
        }

        // 攻撃速度デバフ中はクールダウンの進行自体を遅くする
        // （Enemy側のFrost実装 fireCooldown -= Time.deltaTime * slowMultiplier と対称）
        fireCooldown -= Time.deltaTime * attackSpeedDebuffMultiplier;

        // Step 3: 供給ネットワークの接続状態とOfflineグレースタイマーをフェーズを問わず更新する
        // （防衛フェーズ中の緊急Outpost設置による即時復帰を、その場のフレームで反映させるため）
        UpdateSupplyConnectionState();

        // Step 4-2: Interlink状態の変化を毎フレーム安価に検知し、変化した時だけ
        // UpdateStatsFromRewards()（fireRate等の再計算）を呼び直す（毎フレーム呼ばない）
        bool isInterlinkedNow = TowerManager.Instance != null && TowerManager.Instance.IsInterlinked(this);
        if (isInterlinkedNow != wasInterlinked)
        {
            wasInterlinked = isInterlinkedNow;
            UpdateStatsFromRewards();
        }

        // 準備フェーズ中は動かない
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase != GamePhase.Defense)
            return;

        // Step 3: Offline中は攻撃・回復を停止する。
        // 障害物としては残存し（MapManagerのセル占有は維持）、敵のターゲットにもなり続ける（優先度は変えない）
        if (isOffline) return;

        if (IsHealer)
        {
            if (fireCooldown <= 0)
            {
                HealTowersInRange();
                fireCooldown = 1.0f / fireRate;
            }
        }
        else
        {
            // C-2: ターゲットキャッシュ - 0.2秒ごとに再検索
            targetSearchCooldown -= Time.deltaTime;
            if (cachedTarget == null || targetSearchCooldown <= 0f)
            {
                // キャッシュが無効か射程外に出た場合に再検索
                if (cachedTarget != null)
                {
                    float dist = Vector3.Distance(transform.position, cachedTarget.transform.position);
                    if (dist > range) cachedTarget = null;
                }
                if (cachedTarget == null)
                {
                    cachedTarget = FindTarget();
                }
                targetSearchCooldown = 0.2f;
            }
            if (cachedTarget != null && fireCooldown <= 0)
            {
                Shoot(cachedTarget);
                fireCooldown = 1.0f / fireRate;
            }
        }
    }

    private Enemy FindTarget()
    {
        List<Enemy> activeEnemies = EnemySpawner.Instance != null ? EnemySpawner.Instance.GetActiveEnemies() : emptyEnemyList;
        return CombatUtils.FindNearestInRange(transform.position, range, activeEnemies);
    }

    private void Shoot(Enemy target)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[Tower] Bullet Prefab is not assigned.");
            return;
        }

        Bullet bullet = Bullet.Spawn(bulletPrefab, transform.position);
        if (bullet != null)
        {
            BulletEffects effects = new BulletEffects
            {
                // 報酬(Frost Action)とタワー固有デバフのうち、強い方／長い方を採用する
                FrostSlowPercent = Mathf.Max(frostSlowPercent, debuffSlowPercent),
                FrostSlowDuration = Mathf.Max(frostSlowDuration, debuffSlowDuration),
                PiercingEnabled = piercingEnabled,
                PiercingDamageRatio = piercingDamageRatio,
                SplashRadius = splashRadius,
                SplashDamageRatio = splashDamageRatio,
                // Step 4-1: 撃破ボーナスの帰属のため、発射元の所有者IDを弾に伝播する
                OwnerId = OwnerId,
            };
            bullet.Seek(target.gameObject, target, damage, effects);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // エディタ上で射程範囲を確認しやすくするデバッグ表示
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, range);
    }

    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalSpriteColor = spriteRenderer.color;
        }

        // Step 4-1: CO-OP時のみ、所有者カラーのアウトラインを生成する（シングルプレイ時は生成しない）
        CreateOwnerOutline();

        // Step 4-6: CO-OP時のみ、所有者カラーのバッジを生成する（アウトラインだけではスプライト色との
        // 同化で判別できないケースがあるための追加対策。シングルプレイ時は生成しない）
        CreateOwnerBadge();

        // Step 4-5: CO-OP時のみ、Outpost(バリケード)に識別番号ラベルを常時表示する
        // （Siege MarkerのHUD警告「BLUE OUTPOST (3)」等と照合できるようにするため）
        if (IsBarricade && GameManager.Instance != null && GameManager.Instance.IsCoop)
        {
            CreateOutpostNumberLabel();
        }

        // 累積獲得済みの報酬アップグレード効果（射程・攻撃力など）を適用
        UpdateStatsFromRewards();

        // Step 3 / 4-2: 配置確定時点で「配置者本人（OwnerId）の」Outpostが盤面に存在したかどうかを記録する。
        // Outpost自身はfalseのままでよい（IsBarricadeで別途常に対象外にするため）。
        // 相手のOutpostの有無ではなく必ず自分のOwnerIdで判定すること。そうしないと
        // 「相方がOutpostを置いた瞬間に、自分がOutpost無しで置いていたタワーが一斉Offlineになる」
        // という理不尽が発生する
        if (!IsBarricade)
        {
            requiresSupply = TowerManager.Instance != null && TowerManager.Instance.HasAnyOutpost(OwnerId);
        }

        if (GameManager.Instance != null)
        {
            placedWave = GameManager.Instance.CurrentWave;
        }
        if (TowerManager.Instance != null)
        {
            buildCost = TowerManager.Instance.GetPlacementCost(towerType);
        }

        if (!IsBarricade && RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated += UpdateStatsFromRewards;
        }

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.RegisterTower(this);
        }

        // Step 1: バリケードもHPを持つため、HP表示は全種別共通で生成する
        healthDisplay = gameObject.AddComponent<HealthDisplay>();
        healthDisplay.Init(new Vector3(0, 1.0f, -1.0f));

        // マウスホバー検出用のコライダー自動追加 (1x1タイル想定)
        UIUtils.EnsureTriggerCollider2D(gameObject, Vector2.one);

        if (!IsBarricade)
        {
            // 範囲表示用のオブジェクトを生成
            GameObject indicatorObj = new GameObject("RangeIndicator");
            indicatorObj.transform.SetParent(transform);
            indicatorObj.transform.localPosition = Vector3.zero;

            rangeIndicator = indicatorObj.AddComponent<TowerRangeIndicator>();
            // 半透明の少し青みがかった白で表示
            rangeIndicator.Init(range, new Color(0.2f, 0.5f, 1.0f, 0.35f));
            
            // セットアップフェーズであっても、初期状態では表示せずクリックまで非表示にする
            isRangeVisible = false;
            rangeIndicator.SetVisible(false);
        }

        // フェーズ変更イベントを登録
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            UpdateVisuals();
        }
    }

    private void OnDestroy()
    {
        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.UnregisterTower(this);
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated -= UpdateStatsFromRewards;
        }
    }

    public void TakeDamage(float damageAmount)
    {
        // Step 1: バリケードも通常のダメージ処理経路に統一する。
        // armorは0のままなので軽減されず、BarricadeBusterの9999ダメージは自然に一撃破壊になる。
        float finalDamage = CombatUtils.ApplyArmorReduction(damageAmount, armor);
        currentHp = Mathf.Max(0, currentHp - finalDamage);
        UpdateHPText();
        if (currentHp <= 0)
        {
            Die();
        }
    }

    public void Heal(float healAmount)
    {
        if (IsBarricade) return;

        // 1秒ごとに被回復許容量をリセット。複数Healerが同時に回復しても
        // このタワーが秒間で受け取れる回復量には上限を設ける
        if (Time.time - healBudgetWindowStart >= 1f)
        {
            healBudgetWindowStart = Time.time;
            healReceivedInWindow = 0f;
        }

        float healCap = maxHp * maxHealPercentPerSecond;
        float allowedHeal = Mathf.Max(0f, healCap - healReceivedInWindow);
        float actualHeal = Mathf.Min(healAmount, allowedHeal);
        if (actualHeal <= 0f) return;

        healReceivedInWindow += actualHeal;
        currentHp = Mathf.Min(maxHp, currentHp + actualHeal);
        UpdateHPText();
    }

    /// <summary>
    /// 攻撃速度を一定時間低下させる（敵デバッファー用）。
    /// 既に強いデバフがかかっている場合は、より強い方を維持する。
    /// </summary>
    public void ApplyAttackSpeedDebuff(float percent, float duration)
    {
        if (IsBarricade) return;

        float newMultiplier = 1f - Mathf.Clamp01(percent);
        // より強いデバフ（= より小さいmultiplier）を優先し、タイマーもリセット
        if (newMultiplier < attackSpeedDebuffMultiplier || attackSpeedDebuffTimer <= 0f)
        {
            attackSpeedDebuffMultiplier = newMultiplier;
        }
        attackSpeedDebuffTimer = Mathf.Max(attackSpeedDebuffTimer, duration);
    }

    private void HealTowersInRange()
    {
        List<Tower> activeTowers = TowerManager.Instance != null ? TowerManager.Instance.GetActiveTowers() : emptyTowerList;
        foreach (Tower tower in activeTowers)
        {
            if (tower == null || tower.IsBarricade) continue;

            float distance = Vector3.Distance(transform.position, tower.transform.position);
            if (distance <= range)
            {
                tower.Heal(damage);
            }
        }
    }

    private void UpdateHPText()
    {
        if (healthDisplay != null)
        {
            healthDisplay.UpdateHPText(currentHp, maxHp);
        }
    }

    private void Die()
    {
        Debug.Log($"[Tower] {gameObject.name} was destroyed.");
        if (MapManager.Instance != null)
        {
            Vector3Int cellPos = MapManager.Instance.WorldToGrid(transform.position);
            MapManager.Instance.SetTowerOccupant(cellPos, false);
        }

        // Step 4-2 Rescue: CO-OP時、Outpostが破壊された瞬間に両者のHUDへ警告バナーを表示する
        if (IsBarricade && GameManager.Instance != null && GameManager.Instance.IsCoop && HUDManager.Instance != null)
        {
            HUDManager.Instance.ShowOutpostDownWarning(OwnerId);
        }

        Destroy(gameObject);

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.NotifyEnemiesToRecalculatePath();
            // Step 2: このタワーが破壊されたことで供給ネットワークが変化する可能性があるため再計算する
            // （OnDestroy経由のUnregisterTower()でも再計算されるが、Destroy()の反映はフレーム末まで
            //   遅延することがあるため、ここでも明示的に呼んでおく）
            TowerManager.Instance.RecalculateSupplyNetwork();
        }
    }

    private void HandlePhaseChanged(GamePhase newPhase)
    {
        if (newPhase == GamePhase.Setup)
        {
            HealPartial(0.5f); // B-3: Setupフェーズで50%回復
        }

        UpdateVisuals();

        if (rangeIndicator == null) return;

        // フェーズ切り替え時は、Setupフェーズ含め一旦攻撃範囲表示はすべて非表示にする
        isRangeVisible = false;
        rangeIndicator.SetVisible(false);

        if (newPhase == GamePhase.Setup)
        {
            rangeIndicator.UpdateRange(range);
        }
    }

    // Step 3: 色の最終決定をこの1箇所に集約する。「フェーズによる基礎色」と「供給状態(Offline/グレース)による色」を
    // 合成してから1回だけ spriteRenderer.color に書き込む。複数箇所から直接色を書き換えると
    // フェーズ表示とOffline表示が競合してバグるため、色を変えたい場合は必ずこの経路を通すこと
    private void UpdateVisuals()
    {
        if (spriteRenderer == null) return;

        Color baseColor = ComputePhaseBaseColor();
        spriteRenderer.color = ComputeSupplyTintedColor(baseColor);

        // Step 4-1 / 4-2: 所有者アウトラインは既存の色合成経路(spriteRenderer.color)とは独立させつつ、
        // 過去ウェーブ配置時の半透明表示(75%)とは見た目を揃えるため、アルファだけ親に追随させる。
        // Interlink中は色自体もBlue/Orangeの間で脈動させる
        UpdateOwnerOutlineVisual();

        // Step 4-6: バッジもアウトラインと同じアルファ追随・脈動仕様を適用する
        UpdateOwnerBadgeVisual();
    }

    // Step 4-1: タワーの子オブジェクトとして所有者カラーのアウトラインを生成する。
    // 元スプライトと同じSpriteを使い、localScaleを約1.18倍、sortingOrderを親より1小さくすることで
    // 親スプライトの背後にわずかにはみ出す縁取りとして見せる。CO-OP時のみ生成し、シングルプレイでは生成しない
    private void CreateOwnerOutline()
    {
        if (spriteRenderer == null) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;

        GameObject outlineObj = new GameObject("OwnerOutline");
        outlineObj.transform.SetParent(transform);
        outlineObj.transform.localPosition = Vector3.zero;
        outlineObj.transform.localRotation = Quaternion.identity;
        outlineObj.transform.localScale = Vector3.one * 1.18f;

        ownerOutlineRenderer = outlineObj.AddComponent<SpriteRenderer>();
        ownerOutlineRenderer.sprite = spriteRenderer.sprite;
        ownerOutlineRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        ownerOutlineRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;

        Color ownerColor = OwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;
        ownerOutlineRenderer.color = new Color(ownerColor.r, ownerColor.g, ownerColor.b, spriteRenderer.color.a);
    }

    // Step 4-2: アウトラインの色とアルファを毎フレーム更新する。
    // アルファは親のspriteRenderer.colorのアルファに追随させる（過去ウェーブ配置時の75%半透明表示などと
    // 見た目を揃えるため）。Interlink中は、仕様書の「グラデーション」表現の代わりに、実装が容易で
    // 視認性の高いBlue/Orange間のLerp脈動で表現する（IsBarricadeは常にfalseなのでOutpost自身は対象外）
    private void UpdateOwnerOutlineVisual()
    {
        if (ownerOutlineRenderer == null) return;

        Color c = ComputeOwnerVisualColorRGB();
        c.a = spriteRenderer.color.a;
        ownerOutlineRenderer.color = c;
    }

    // Step 4-6: 所有者カラー(アルファ抜きのRGBのみ)を計算する。アウトラインとバッジの両方から呼ばれる
    // 共通経路として切り出したもの。Interlink中のBlue/Orange脈動を同一のTime.time計算式で行うことで、
    // アウトラインとバッジの脈動位相が必ず一致し、ちぐはぐに見えないようにする
    private Color ComputeOwnerVisualColorRGB()
    {
        bool interlinked = TowerManager.Instance != null && TowerManager.Instance.IsInterlinked(this);
        if (interlinked)
        {
            float t = (Mathf.Sin(Time.time * 3f) + 1f) * 0.5f; // 0..1で往復
            return Color.Lerp(OwnerColorBlue, OwnerColorOrange, t);
        }
        return OwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;
    }

    // Step 4-6: タワー本体より手前（sortingOrderが親+2）に、所有者カラーの正方形バッジを表示する。
    // 通常タワーのスプライトが青色のため、Blue所有者のアウトライン(CreateOwnerOutline)がスプライト自体の
    // 色と同化して視認できない不具合の対策。バッジはスプライト色に関係なく必ず判別できる。
    // CO-OP時のみ生成し、シングルプレイでは生成しない（既存の見た目を変えないため）
    private void CreateOwnerBadge()
    {
        if (spriteRenderer == null) return;
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;

        Sprite badgeSprite = GetOrCreateBadgeSprite();
        Vector3 badgeLocalPosition = new Vector3(0.32f, 0.32f, -1f); // タワー右上

        // 視認性を上げるため、バッジ本体の背後にわずかに大きい黒縁を重ねる
        GameObject borderObj = new GameObject("OwnerBadgeBorder");
        borderObj.transform.SetParent(transform);
        borderObj.transform.localPosition = badgeLocalPosition;
        borderObj.transform.localRotation = Quaternion.identity;
        borderObj.transform.localScale = Vector3.one * 0.36f;

        ownerBadgeBorderRenderer = borderObj.AddComponent<SpriteRenderer>();
        ownerBadgeBorderRenderer.sprite = badgeSprite;
        ownerBadgeBorderRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        ownerBadgeBorderRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
        ownerBadgeBorderRenderer.color = new Color(OwnerBadgeBorderColor.r, OwnerBadgeBorderColor.g, OwnerBadgeBorderColor.b, spriteRenderer.color.a);

        GameObject badgeObj = new GameObject("OwnerBadge");
        badgeObj.transform.SetParent(transform);
        badgeObj.transform.localPosition = badgeLocalPosition;
        badgeObj.transform.localRotation = Quaternion.identity;
        badgeObj.transform.localScale = Vector3.one * 0.3f;

        ownerBadgeRenderer = badgeObj.AddComponent<SpriteRenderer>();
        ownerBadgeRenderer.sprite = badgeSprite;
        ownerBadgeRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
        // タワー本体(親)より必ず手前に出す。アウトライン(親-1)より更に手前
        ownerBadgeRenderer.sortingOrder = spriteRenderer.sortingOrder + 2;

        Color ownerColor = OwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;
        ownerBadgeRenderer.color = new Color(ownerColor.r, ownerColor.g, ownerColor.b, spriteRenderer.color.a);
    }

    // Step 4-6: バッジ本体と黒縁の色・アルファを毎フレーム更新する。
    // アルファは親のspriteRenderer.colorのアルファに追随させ、Interlink中の脈動は
    // ComputeOwnerVisualColorRGB()を共有することでアウトラインと同じ位相にする（アウトラインと同じ扱い）
    private void UpdateOwnerBadgeVisual()
    {
        if (ownerBadgeRenderer == null) return;

        Color c = ComputeOwnerVisualColorRGB();
        c.a = spriteRenderer.color.a;
        ownerBadgeRenderer.color = c;

        if (ownerBadgeBorderRenderer != null)
        {
            Color borderColor = OwnerBadgeBorderColor;
            borderColor.a = spriteRenderer.color.a;
            ownerBadgeBorderRenderer.color = borderColor;
        }
    }

    // Step 4-6: バッジ用の白い正方形Spriteを実行時に生成する。プロジェクトに白い矩形スプライトの
    // アセットが存在しない可能性が高いため、外部アセットに依存せずTexture2Dから生成する。
    // 全タワーで1枚だけ共有する静的キャッシュとし、タワーごとの生成・破棄によるリークを避ける
    private static Sprite GetOrCreateBadgeSprite()
    {
        if (ownerBadgeSprite != null) return ownerBadgeSprite;

        const int size = 8;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "OwnerBadgeTexture";
        texture.filterMode = FilterMode.Point; // くっきりした縁の正方形に見せるため

        Color32[] pixels = new Color32[size * size];
        Color32 white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
        texture.SetPixels32(pixels);
        texture.Apply();

        // pixelsPerUnit = size とすることで、localScale = 1 のとき1ユニット(タワー1マス)分の正方形になる。
        // これにより、以降はlocalScaleだけでバッジサイズを調整できる
        ownerBadgeSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return ownerBadgeSprite;
    }

    // フェーズによる基礎色。Setupフェーズかつ過去ウェーブに配置されたタワー（売却不可）は
    // 暗く半透明に、それ以外は元の色を返す
    private Color ComputePhaseBaseColor()
    {
        if (GameManager.Instance == null) return originalSpriteColor;

        if (GameManager.Instance.CurrentPhase == GamePhase.Setup
            && placedWave != GameManager.Instance.CurrentWave)
        {
            // 過去のウェーブで設置されたタワー（売却不可）：暗く半透明に
            return originalSpriteColor * new Color(0.5f, 0.5f, 0.5f, 0.75f);
        }

        // 現在ウェーブに設置されたタワー、または防衛/報酬フェーズ中は元の色
        return originalSpriteColor;
    }

    // Step 3: 供給ネットワークの接続状態による色の合成。
    // Outpost(バリケード)自身と、Step 2の詰み対策で猶予されたタワー(requiresSupply=false)は常に無変化
    private Color ComputeSupplyTintedColor(Color baseColor)
    {
        if (IsBarricade || !requiresSupply) return baseColor;

        if (isOffline)
        {
            // Offline: 彩度を落として暗くグレーアウトする
            float gray = (baseColor.r + baseColor.g + baseColor.b) / 3f;
            Color desaturated = Color.Lerp(baseColor, new Color(gray, gray, gray, baseColor.a), 0.85f);
            return desaturated * new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        if (offlineGraceTimer >= 0f)
        {
            // グレース中: 警告色との間で明滅させ、Online/Offlineのどちらとも区別できるようにする
            float blink = (Mathf.Sin(Time.time * 10f) + 1f) * 0.5f; // 0..1
            Color warnColor = new Color(1f, 0.25f, 0.2f, baseColor.a);
            return Color.Lerp(baseColor, warnColor, blink * 0.7f);
        }

        return baseColor;
    }

    // Step 3: 供給ネットワークへの接続状態を毎フレーム監視し、グレースタイマーとOffline状態を更新する。
    // BFS自体はTowerManager.RecalculateSupplyNetwork()側でイベント駆動にキャッシュされているため、
    // ここではキャッシュ済みのIsTowerSupplied()を参照するだけで済み、毎フレーム呼んでも軽量
    private void UpdateSupplyConnectionState()
    {
        bool wasOffline = isOffline;

        if (!requiresSupply)
        {
            // Step 2の詰み対策で猶予されたタワー。供給ルールの対象外として恒久的にOnline扱いにする
            offlineGraceTimer = -1f;
            isOffline = false;
        }
        else
        {
            bool isSupplied = TowerManager.Instance != null && TowerManager.Instance.IsTowerSupplied(this);

            if (isSupplied)
            {
                // 再連結: グレース無しで即座にOnlineへ復帰する
                offlineGraceTimer = -1f;
                isOffline = false;
            }
            else if (!isOffline)
            {
                // 切断中: 猶予秒数をカウントダウンしてからOfflineへ遷移する
                // （Step 4-2: CO-OP時はEffectiveOfflineGraceDurationにより5秒に延長される＝Rescue猶予）
                if (offlineGraceTimer < 0f)
                {
                    offlineGraceTimer = EffectiveOfflineGraceDuration;
                }
                offlineGraceTimer -= Time.deltaTime;
                if (offlineGraceTimer <= 0f)
                {
                    isOffline = true;
                    offlineGraceTimer = -1f;
                }
            }
        }

        if (wasOffline != isOffline)
        {
            Debug.Log(isOffline
                ? $"[Tower] {gameObject.name} went Offline (disconnected from outpost supply network)."
                : $"[Tower] {gameObject.name} is back Online (reconnected to outpost supply network).");
        }

        // グレース中の点滅アニメーションのため、状態が変わらない間も毎フレーム見た目を更新する
        UpdateVisuals();
    }

    private void HealPartial(float ratio)
    {
        // Step 1: バリケードもSetup開始時の割合回復の対象にする
        // （回復しないと「削除して置き直す」作業をプレイヤーに強要してしまうため）
        float healAmount = maxHp * ratio;
        currentHp = Mathf.Min(maxHp, currentHp + healAmount);
        UpdateHPText();
        Debug.Log($"[Tower] {gameObject.name} healed {ratio*100}% ({currentHp:F1}/{maxHp:F1}).");
    }

    private void OnMouseEnter()
    {
        // UI操作中は表示しない
        if (UIUtils.IsPointerOverUI())
            return;

        bool isSetupPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Setup;

        if (rangeIndicator != null)
        {
            rangeIndicator.UpdateRange(range);
            if (isSetupPhase)
            {
                // Setupフェーズ中は、クリックによる表示状態を維持する
                rangeIndicator.SetVisible(isRangeVisible);
            }
            else
            {
                // それ以外のフェーズではホバー時に表示
                rangeIndicator.SetVisible(true);
            }
        }

        if (healthDisplay != null)
        {
            UpdateHPText();
            healthDisplay.SetVisible(true);
        }
    }

    private void OnMouseExit()
    {
        bool isSetupPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Setup;
        if (rangeIndicator != null)
        {
            if (!isSetupPhase)
            {
                rangeIndicator.SetVisible(false);
            }
            else
            {
                // Setupフェーズ中はクリックによる表示状態を維持
                rangeIndicator.SetVisible(isRangeVisible);
            }
        }

        if (healthDisplay != null)
        {
            healthDisplay.SetVisible(false);
        }
    }

    private void OnMouseOver()
    {
        // 準備フェーズ中かつ、UIの上でないことを確認
        bool isSetupPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Setup;

        if (isSetupPhase && !UIUtils.IsPointerOverUI())
        {
            // 右クリックを検知
            if (Input.GetMouseButtonDown(1))
            {
                // Step 4-1: CO-OP時は自分のタワーしか売却・削除できない（相手タワーへの誤操作事故を防ぐ）。
                // シングルプレイ時は従来どおり無制限
                if (GameManager.Instance != null && GameManager.Instance.IsCoop
                    && OwnerId != GameManager.Instance.ActiveOwnerId)
                {
                    if (HUDManager.Instance != null)
                    {
                        HUDManager.Instance.ShowToast("Not your tower");
                    }
                    return;
                }
                TryRefundAndDestroy();
            }
            // 左クリックを検知（射程表示トグルは所有者に関わらず常に許可する）
            else if (Input.GetMouseButtonDown(0))
            {
                ToggleRangeIndicator();
            }
        }
    }

    private void ToggleRangeIndicator()
    {
        if (rangeIndicator != null)
        {
            isRangeVisible = !isRangeVisible;
            rangeIndicator.UpdateRange(range);
            rangeIndicator.SetVisible(isRangeVisible);
            Debug.Log($"[Tower] Range indicator toggled. Visible: {isRangeVisible}");
        }
    }

    private void TryRefundAndDestroy()
    {
        if (GameManager.Instance == null) return;

        // 現在と同じウェーブ中に配置されたタワーだけが対象 (バリケードはウェーブ制限なし)
        if (IsBarricade || placedWave == GameManager.Instance.CurrentWave)
        {
            // コストの返還
            GameManager.Instance.AddCost(buildCost);

            // マップのグリッド占有状態を解除
            if (MapManager.Instance != null)
            {
                Vector3Int cellPos = MapManager.Instance.WorldToGrid(transform.position);
                MapManager.Instance.SetTowerOccupant(cellPos, false);
            }

            // TowerManagerからの除外
            if (TowerManager.Instance != null)
            {
                TowerManager.Instance.UnregisterTower(this);
                // 敵の経路の再計算を要求する
                TowerManager.Instance.NotifyEnemiesToRecalculatePath();
                // Step 2: 売却によって供給ネットワークが変化する可能性があるため再計算する
                // （UnregisterTower()内でも再計算されるが、ここでも明示的に呼んでおく）
                TowerManager.Instance.RecalculateSupplyNetwork();
            }

            // オブジェクトの破棄
            Destroy(gameObject);
            
            Debug.Log($"[Tower] Tower refunded! Refunded {buildCost} cost.");
        }
        else
        {
            Debug.Log("[Tower] Cannot refund tower placed in previous waves.");
        }
    }
}
