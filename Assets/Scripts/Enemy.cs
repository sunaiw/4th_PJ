using System;
using System.Collections.Generic;
using UnityEngine;

public class Enemy : MonoBehaviour, IDamageable
{
    private static readonly List<Tower> emptyTowerList = new List<Tower>(0);
    [Header("Enemy Attributes")]
    [SerializeField] private float speed = 2.0f;
    [SerializeField] private float maxHp = 10f;
    [SerializeField] private float armor = 0f;

    [SerializeField] private int coreDamage = 1;
    [SerializeField] private bool ignoreTowers = false;

    [Header("Wave Scaling (Compound Growth)")]
    [SerializeField] private float hpGrowthRatePerWave = 0.13f;
    [SerializeField] private float damageGrowthRatePerWave = 0.10f;

    public bool IgnoreTowers => ignoreTowers;

    private bool avoidThreats = true;
    public bool AvoidThreats => avoidThreats;

    [Header("Combat Attributes")]
    [SerializeField] private float attackRange = 2.0f;
    [SerializeField] private float fireRate = 0.5f; 
    [SerializeField] private float damage = 1f;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private bool lockTargetAndStopMoving = false;

    private Tower lockedAttackTarget = null;
    [SerializeField] private bool isBoss = false;
    [SerializeField] private bool isBarricadeBuster = false;
    public bool IsBarricadeBuster => isBarricadeBuster;

    // ボス専用: 同じターゲットに固定され続けるほど攻撃力が上昇するエンレイジ機構。
    // Healer等で被ダメージを無効化され続けて膠着状態になるのを防ぐ。
    [SerializeField] private float bossEnrageInterval = 5f;
    [SerializeField] private float bossEnrageDamageStep = 0.3f;
    private float bossLockedDuration = 0f;
    private int bossEnrageStacksApplied = 0;

    private float currentHp;
    private List<Vector3> path = new List<Vector3>();
    private int currentPathIndex = 0;
    private float fireCooldown = 0f;
    private float targetSearchCooldown = 0f;
    private Tower cachedTargetForNormal = null;
    private List<Tower> cachedValidTowersInRange = new List<Tower>();

    public float Armor
    {
        get => armor;
        set => armor = Mathf.Clamp(value, 0f, 100f);
    }

    public event Action OnEnemyDestroyed;

    private HealthDisplay healthDisplay;

    // Frost Action: スロウシステム
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    private void Awake()
    {
        if (ignoreTowers)
        {
            avoidThreats = false; // タワー無視移動ができる高速エネミーは脅威の大回り迂回も行わない
        }
    }

    private void Start()
    {
        currentHp = maxHp;

        // OnMouseEnter用のコライダー自動追加
        UIUtils.EnsureTriggerCollider2D(gameObject, Vector2.one);

        healthDisplay = gameObject.AddComponent<HealthDisplay>();
        healthDisplay.Init(new Vector3(0, 1.0f, -1.0f));
    }

    private void Update()
    {
        // スロウタイマー更新
        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f)
            {
                slowMultiplier = 1f;
                slowTimer = 0f;
            }
        }

        // 準備フェーズ中は攻撃しない＆ロック解除
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase != GamePhase.Defense)
        {
            lockedAttackTarget = null;
            MoveAlongPath();
            return;
        }

        targetSearchCooldown -= Time.deltaTime;

        // 指定間隔でターゲットを再検索
        if (targetSearchCooldown <= 0f)
        {
            SearchForSpecialTargets();
            targetSearchCooldown = 0.2f;
        }

        // 移動判定：バリケードバスターでロックターゲットがある場合は移動を停止し、それ以外は通常どおり前進
        if (isBarricadeBuster && lockedAttackTarget != null)
        {
            // バリケードを検知している間は移動を停止する（その場で射撃・破壊を行う）
        }
        else if (!lockTargetAndStopMoving || lockedAttackTarget == null)
        {
            MoveAlongPath();
        }

        fireCooldown -= Time.deltaTime;

        Tower target = null;
        if (isBoss)
        {
            target = lockedAttackTarget;
            UpdateBossEnrage(target);
        }
        else if (lockTargetAndStopMoving)
        {
            if (lockedAttackTarget == null)
            {
                lockedAttackTarget = FindTarget();
            }
            target = lockedAttackTarget;
        }
        else
        {
            // キャッシュを利用する通常のターゲット検索
            if (targetSearchCooldown <= 0f || cachedTargetForNormal == null)
            {
                cachedTargetForNormal = FindTarget();
            }
            target = cachedTargetForNormal;
        }

        if (target != null && fireCooldown <= 0)
        {
            Shoot(target);
            fireCooldown = 1.0f / fireRate;
        }
    }

    // ロック中のターゲットを倒せずにいる時間が長引くほど攻撃力を上げ、Healer等による
    // 膠着（回復が被ダメージを完全に上回り続ける状態）を許さないようにする
    private void UpdateBossEnrage(Tower target)
    {
        if (target == null)
        {
            bossLockedDuration = 0f;
            bossEnrageStacksApplied = 0;
            return;
        }

        bossLockedDuration += Time.deltaTime;
        int enrageLevel = Mathf.FloorToInt(bossLockedDuration / bossEnrageInterval);
        if (enrageLevel > bossEnrageStacksApplied)
        {
            int newStacks = enrageLevel - bossEnrageStacksApplied;
            damage *= Mathf.Pow(1f + bossEnrageDamageStep, newStacks);
            bossEnrageStacksApplied = enrageLevel;
            Debug.Log($"[Enemy] Boss enraged! Damage now {damage:F1} (enrage level: {enrageLevel})");
        }
    }

    private void SearchForSpecialTargets()
    {
        List<Tower> activeTowers = TowerManager.Instance != null ? TowerManager.Instance.GetActiveTowers() : emptyTowerList;

        // バリケードバスター専用の進路変更およびターゲット設定: 射程内にバリケードがいるならターゲットをそれにし、直線的に進む
        if (isBarricadeBuster)
        {
            lockedAttackTarget = CombatUtils.FindNearestInRange(transform.position, attackRange, activeTowers,
                                                                t => t.IsBarricade);
        }

        // ボス専用のロックオン条件：攻撃範囲内に3つ以上タワーがあるか？
        if (isBoss && lockedAttackTarget == null)
        {
            cachedValidTowersInRange.Clear();
            foreach (Tower t in activeTowers)
            {
                if (t != null && !t.IsBarricade)
                {
                    float dist = Vector3.Distance(transform.position, t.transform.position);
                    if (dist <= attackRange)
                    {
                        cachedValidTowersInRange.Add(t);
                    }
                }
            }

            if (cachedValidTowersInRange.Count >= 3)
            {
                // 最も近いタワーをターゲットにロック
                lockedAttackTarget = CombatUtils.FindNearestInRange(transform.position, attackRange, cachedValidTowersInRange);
            }
        }
    }

    public void SetPath(List<Vector3> newPath)
    {
        path = newPath;
        currentPathIndex = 0;
        if (path.Count > 0)
        {
            transform.position = path[0];
        }
    }

    // Step 2 での経路更新時に使用
    public void UpdatePath(List<Vector3> newPath)
    {
        path = newPath;
        currentPathIndex = 0;

        if (path.Count > 1)
        {
            // スタート位置（path[0]：現在マスの中心）と次のノード（path[1]：次のマスの中心）の方向
            Vector3 dir = path[1] - path[0];
            // スタート位置からエネミーの物理現在地へのベクトル
            Vector3 toEnemy = transform.position - path[0];

            // 内積が正 ＝ 既に最寄りのセルの中心を通り越して次のセル方向へ進んでいる場合
            if (Vector3.Dot(dir, toEnemy) > 0f)
            {
                currentPathIndex = 1;
            }
        }
    }

    private void MoveAlongPath()
    {
        if (path == null || path.Count == 0 || currentPathIndex >= path.Count)
            return;

        Vector3 targetPos = path[currentPathIndex];
        float step = speed * slowMultiplier * Time.deltaTime;

        Vector3 currentPos = transform.position;
        Vector3 nextPos = currentPos;

        // 浮動小数点の誤差を考慮し、微小な閾値で軸が一致しているか判定
        bool xAligned = Mathf.Abs(currentPos.x - targetPos.x) < 0.005f;
        bool yAligned = Mathf.Abs(currentPos.y - targetPos.y) < 0.005f;

        if (!xAligned)
        {
            nextPos.x = Mathf.MoveTowards(currentPos.x, targetPos.x, step);
        }
        else if (!yAligned)
        {
            nextPos.y = Mathf.MoveTowards(currentPos.y, targetPos.y, step);
        }

        transform.position = nextPos;

        // 目標点に十分近ければ、座標を完全スナップした上で次のインデックスへ
        if (Mathf.Abs(transform.position.x - targetPos.x) < 0.05f &&
            Mathf.Abs(transform.position.y - targetPos.y) < 0.05f)
        {
            transform.position = targetPos; // ズレ防止のスナップ
            currentPathIndex++;
            if (currentPathIndex >= path.Count)
            {
                ReachCore();
            }
        }
    }

    private void ReachCore()
    {
        Debug.Log($"[Enemy] Core Reached! Damaging core by {coreDamage}.");
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TakeDamage(coreDamage);
        }
        DestroySelf();
    }

    public void TakeDamage(float damage)
    {
        float finalDamage = CombatUtils.ApplyArmorReduction(damage, armor);
        currentHp -= finalDamage;
        UpdateHPText();
        if (currentHp <= 0)
        {
            Die();
        }
    }

    private void UpdateHPText()
    {
        if (healthDisplay != null)
        {
            healthDisplay.UpdateHPText(currentHp, maxHp);
        }
    }

    private void OnMouseEnter()
    {
        if (UIUtils.IsPointerOverUI())
            return;

        if (healthDisplay != null)
        {
            UpdateHPText();
            healthDisplay.SetVisible(true);
        }
    }

    private void OnMouseExit()
    {
        if (healthDisplay != null)
        {
            healthDisplay.SetVisible(false);
        }
    }

    private void Die()
    {
        Debug.Log("[Enemy] Slain!");
        if (GameManager.Instance != null) { GameManager.Instance.AddKill(); }
        DestroySelf();
    }

    /// <summary>
    /// Frost Action: 移動速度を一定時間低下させる。既に強いスロウがかかっている場合はより強い方を適用。
    /// </summary>
    public void ApplySlow(float percent, float duration)
    {
        float newMultiplier = 1f - Mathf.Clamp01(percent);
        // より強いスロウ（= より小さいmultiplier）を優先し、タイマーもリセット
        if (newMultiplier < slowMultiplier || slowTimer <= 0f)
        {
            slowMultiplier = newMultiplier;
        }
        slowTimer = Mathf.Max(slowTimer, duration);
    }

    private void DestroySelf()
    {
        OnEnemyDestroyed?.Invoke();
        if (EnemySpawner.Instance != null)
        {
            EnemySpawner.Instance.UnregisterEnemy(this);
        }
        Destroy(gameObject);
    }

    private Tower FindTarget()
    {
        List<Tower> activeTowers = TowerManager.Instance != null ? TowerManager.Instance.GetActiveTowers() : emptyTowerList;
        Vector3 pos = transform.position;

        if (isBarricadeBuster)
        {
            return CombatUtils.FindNearestInRange(pos, attackRange, activeTowers, t => t.IsBarricade);
        }

        // 1. 射程内のHealerを優先的に探索
        Tower bestTarget = CombatUtils.FindNearestInRange(pos, attackRange, activeTowers,
                                                          t => !t.IsBarricade && t.IsHealer);

        // 2. 射程内にHealerがいなければ通常のタワーを探索
        if (bestTarget == null)
        {
            bestTarget = CombatUtils.FindNearestInRange(pos, attackRange, activeTowers,
                                                        t => !t.IsBarricade && !t.IsHealer);
        }

        return bestTarget;
    }

    private void Shoot(Tower target)
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[Enemy] Bullet Prefab is not assigned.");
            return;
        }

        Bullet bullet = Bullet.Spawn(bulletPrefab, transform.position);
        if (bullet != null)
        {
            bullet.Seek(target.gameObject, target, damage);
        }
    }

    public void ScaleStats(int waveNumber)
    {
        if (ignoreTowers)
        {
            avoidThreats = false;
        }

        if (lockTargetAndStopMoving)
        {
            ignoreTowers = false; // 高速エネミー（Enemy2）以外はタワーをすり抜けない
            avoidThreats = false; // ただし脅威を迂回して大回りせず、物理的最短ルートを通る
        }

        maxHp = maxHp * GetHpScaleMultiplier(waveNumber);
        damage = damage * GetDamageScaleMultiplier(waveNumber);
        currentHp = maxHp;
        Debug.Log($"[Enemy] {gameObject.name} scaled for Wave {waveNumber}. MaxHP: {maxHp:F1}, Damage: {damage:F1}");
    }

    // Towerの強化が複利（HP+15%/stack等）で伸びるのに合わせ、Enemyも複利成長させる。
    private float GetHpScaleMultiplier(int waveNumber)
    {
        return Mathf.Pow(1f + hpGrowthRatePerWave, Mathf.Max(0, waveNumber - 1));
    }

    private float GetDamageScaleMultiplier(int waveNumber)
    {
        return Mathf.Pow(1f + damageGrowthRatePerWave, Mathf.Max(0, waveNumber - 1));
    }

    public void SetupBoss(int waveNumber)
    {
        isBoss = true;

        // ボス感を出すためにスケールを1.8倍にする
        transform.localScale = transform.localScale * 1.8f;
        
        // SpriteRendererの色を赤に変える
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(0.85f, 0.15f, 0.15f); // 暗めの赤
        }

        // HPはそのWaveの通常Enemy相当のHP（複利成長） * ボス補正
        // 攻撃力はそのWaveの通常Enemy相当の攻撃力（複利成長） * (1 + 現在のWave数 / 5 * 0.1)
        float standardWaveHP = maxHp * GetHpScaleMultiplier(waveNumber);
        float standardWaveDamage = damage * GetDamageScaleMultiplier(waveNumber);

        maxHp = standardWaveHP * Mathf.Sqrt(waveNumber) * 3f;
        damage = standardWaveDamage * (1.0f + (float)waveNumber / 5.0f * 0.1f);
        attackRange = 3.0f;
        fireRate = 1.0f;
        coreDamage = 10;
        speed = 2.0f;
        armor = 0f;
        lockTargetAndStopMoving = true;
        ignoreTowers = false; // ボスもタワー（バリケード）をすり抜けない
        avoidThreats = false; // 大回りせず最短直線で突き進む

        currentHp = maxHp;

        gameObject.name = $"BossEnemy_Wave{waveNumber}";
        
        Debug.Log($"[Enemy] BOSS Scaled for Wave {waveNumber}. MaxHP: {maxHp:F1}, Damage: {damage:F1}, Range: {attackRange}, FireRate: {fireRate}, Speed: {speed}, Armor: {armor}");
    }

    public void SetupBarricadeBuster(int waveNumber)
    {
        isBarricadeBuster = true;

        // ウェーブスケーリングを先に適用（通常Enemyと同じ複利成長）
        maxHp = 6.0f * GetHpScaleMultiplier(waveNumber); // 通常Enemy基準HP 6.0 をスケーリング

        speed = 2.0f;           // 通常Enemyと同じ
        coreDamage = 1;         // 通常Enemyと同じ
        attackRange = 4.0f;     // 射程範囲を4.0に変更（検知範囲が狭く難易度に寄与しにくかったため拡大）
        fireRate = 0.1f;        // 指定値
        damage = 9999f;         // バリケードを一撃で破壊するダメージ値
        ignoreTowers = false;   // タワー（バリケード）をすり抜けない
        avoidThreats = false;   // 脅威による大回りは行わない

        currentHp = maxHp;
        gameObject.name = "BarricadeBusterEnemy";

        // 通常エネミーと視覚的に見分けられるよう黄色に変更
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(1.0f, 0.92f, 0.016f);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryLockBossTarget(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryLockBossTarget(other);
    }

    // ボスが接触したタワー（バリケード以外）をロックオンする
    private void TryLockBossTarget(Collider2D other)
    {
        if (isBoss && lockedAttackTarget == null)
        {
            Tower tower = other.GetComponent<Tower>();
            if (tower != null && !tower.IsBarricade)
            {
                lockedAttackTarget = tower;
            }
        }
    }
}
