using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Tower : MonoBehaviour, IDamageable
{
    private static readonly List<Enemy> emptyEnemyList = new List<Enemy>(0);
    private static readonly List<Tower> emptyTowerList = new List<Tower>(0);
    [Header("Tower Attributes")]
    [SerializeField] private float range = 3.0f;
    [SerializeField] private float fireRate = 1.0f; // 1秒間の弾数
    [SerializeField] private float damage = 2f;
    [SerializeField] private float maxHp = 5f;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private bool isBarricade = false;
    [SerializeField] private bool isHealer = false;

    public bool IsBarricade => isBarricade;
    public bool IsHealer => isHealer;

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

    // Piercing Shot パラメータ
    private bool piercingEnabled = false;
    private float piercingDamageRatio = 0f;

    private void Awake()
    {
        baseRange = range;
        baseFireRate = fireRate;
        baseDamage = damage;
        baseMaxHp = maxHp;
        baseArmor = armor;
    }

    public void UpdateStatsFromRewards()
    {
        if (isBarricade) return;
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

        // HPUP: 複利 1.05^hpCount (1スタックあたり5%上昇)
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

    private void Update()
    {
        if (isBarricade) return;
        fireCooldown -= Time.deltaTime;
        
        // 準備フェーズ中は動かない
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase != GamePhase.Defense)
            return;

        if (isHealer)
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
        Enemy bestTarget = null;
        float shortestDistance = float.MaxValue;

        foreach (Enemy enemy in activeEnemies)
        {
            if (enemy == null) continue;

            float distanceToEnemy = Vector3.Distance(transform.position, enemy.transform.position);
            if (distanceToEnemy <= range)
            {
                // 最も近い敵を狙う
                if (distanceToEnemy < shortestDistance)
                {
                    shortestDistance = distanceToEnemy;
                    bestTarget = enemy;
                }
            }
        }

        return bestTarget;
    }

    private void Shoot(Enemy target)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[Tower] Bullet Prefab is not assigned.");
            return;
        }

        // C-3: BulletPoolを使用（存在しない場合はInstantiateにフォールバック）
        GameObject bulletObj;
        if (BulletPool.Instance != null)
        {
            bulletObj = BulletPool.Instance.Get(bulletPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            bulletObj = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
        }
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.sourcePrefab = bulletPrefab;
            // Frost Action / Piercing Shot のパラメータを弾に渡す
            bullet.Seek(target.gameObject, target, damage,
                        frostSlowPercent, frostSlowDuration,
                        piercingEnabled, piercingDamageRatio);
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
            if (isBarricade)
                buildCost = TowerManager.Instance.BarricadeCost;
            else if (isHealer)
                buildCost = TowerManager.Instance.HealerCost;
            else
                buildCost = TowerManager.Instance.TowerCost;
        }

        if (!isBarricade && RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated += UpdateStatsFromRewards;
        }

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.RegisterTower(this);
        }

        if (!isBarricade)
        {
            healthDisplay = gameObject.AddComponent<HealthDisplay>();
            healthDisplay.Init(new Vector3(0, 1.0f, -1.0f));
        }

        // マウスホバー検出用のコライダーが存在するかチェック
        Collider2D col = GetComponent<Collider2D>();
        if (col == null)
        {
            BoxCollider2D boxCol = gameObject.AddComponent<BoxCollider2D>();
            boxCol.isTrigger = true;
            boxCol.size = Vector2.one; // 1x1タイル想定
        }

        if (!isBarricade)
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

    [SerializeField] private float armor = 0f;

    public float Armor
    {
        get => armor;
        set => armor = Mathf.Clamp(value, 0f, 100f);
    }

    public void TakeDamage(float damageAmount)
    {
        if (isBarricade && damageAmount < 9000f) return;

        if (isBarricade)
        {
            Die();
            return;
        }

        float damageReduction = Mathf.Clamp(armor, 0f, 100f) / 100.0f;
        float finalDamage = damageAmount * (1.0f - damageReduction);
        currentHp = Mathf.Max(0, currentHp - finalDamage);
        UpdateHPText();
        if (currentHp <= 0)
        {
            Die();
        }
    }

    public void Heal(float healAmount)
    {
        if (isBarricade) return;
        currentHp = Mathf.Min(maxHp, currentHp + healAmount);
        UpdateHPText();
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

    private void HealToFull()
    {
        currentHp = maxHp;
        UpdateHPText();
        Debug.Log($"[Tower] {gameObject.name} healed to full HP ({currentHp}/{maxHp}).");
    }

    private void HealPartial(float ratio)
    {
        if (isBarricade) return;
        float healAmount = maxHp * ratio;
        currentHp = Mathf.Min(maxHp, currentHp + healAmount);
        UpdateHPText();
        Debug.Log($"[Tower] {gameObject.name} healed {ratio*100}% ({currentHp:F1}/{maxHp:F1}).");
    }

    private void OnMouseEnter()
    {
        // UI操作中は表示しない
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
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
        bool isOverUI = UnityEngine.EventSystems.EventSystem.current != null && 
                        UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        if (isSetupPhase && !isOverUI)
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
        if (isBarricade || placedWave == GameManager.Instance.CurrentWave)
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
