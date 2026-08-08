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
        if (RewardManager.Instance == null) return;

        var counts = RewardManager.Instance.GetAcquiredRewardCounts();

        // 攻撃力UP: 獲得数*10%
        if (counts.TryGetValue(RewardType.IncreaseTowerDamage, out int dmgCount))
        {
            damage = baseDamage * (1f + dmgCount * 0.1f);
        }

        // 攻撃速度UP: 獲得数*10%
        if (counts.TryGetValue(RewardType.IncreaseTowerFireRate, out int frCount))
        {
            fireRate = baseFireRate * (1f + frCount * 0.1f);
        }

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

        // 準備フェーズ中は動かない
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase != GamePhase.Defense)
            return;

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

        // 累積獲得済みの報酬アップグレード効果（射程・攻撃力など）を適用
        UpdateStatsFromRewards();

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
            ApplyPhaseVisuals(GameManager.Instance.CurrentPhase);
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
        
        Destroy(gameObject);

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.NotifyEnemiesToRecalculatePath();
        }
    }

    private void HandlePhaseChanged(GamePhase newPhase)
    {
        if (newPhase == GamePhase.Setup)
        {
            HealPartial(0.5f); // B-3: Setupフェーズで50%回復
        }

        ApplyPhaseVisuals(newPhase);

        if (rangeIndicator == null) return;

        // フェーズ切り替え時は、Setupフェーズ含め一旦攻撃範囲表示はすべて非表示にする
        isRangeVisible = false;
        rangeIndicator.SetVisible(false);

        if (newPhase == GamePhase.Setup)
        {
            rangeIndicator.UpdateRange(range);
        }
    }

    private void ApplyPhaseVisuals(GamePhase phase)
    {
        if (spriteRenderer == null) return;

        if (phase == GamePhase.Setup)
        {
            if (GameManager.Instance != null)
            {
                if (placedWave == GameManager.Instance.CurrentWave)
                {
                    // 現在のウェーブで設置されたタワー（売却可能）：元の色
                    spriteRenderer.color = originalSpriteColor;
                }
                else
                {
                    // 過去のウェーブで設置されたタワー（売却不可）：暗く半透明に
                    spriteRenderer.color = originalSpriteColor * new Color(0.5f, 0.5f, 0.5f, 0.75f);
                }
            }
        }
        else
        {
            // 防衛フェーズやその他のフェーズ中には元のカラーへ復帰
            spriteRenderer.color = originalSpriteColor;
        }
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
                TryRefundAndDestroy();
            }
            // 左クリックを検知
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
