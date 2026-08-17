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
    // 0より大きい場合、着弾地点の周囲のタワーにも範囲ダメージを与える（Bomber用）
    [SerializeField] private float attackSplashRadius = 0f;
    [SerializeField] private float attackSplashDamageRatio = 1.0f;
    // 0より大きい場合、命中したタワーの攻撃速度を低下させる（Disruptor用）
    [SerializeField] private float towerAttackSpeedDebuffPercent = 0f;
    [SerializeField] private float towerAttackSpeedDebuffDuration = 3f;
    [SerializeField] private bool lockTargetAndStopMoving = false;

    private Tower lockedAttackTarget = null;
    [SerializeField] private bool isBoss = false;
    [SerializeField] private bool isBarricadeBuster = false;
    public bool IsBarricadeBuster => isBarricadeBuster;

    // Step 4-5: Siege Marker（CO-OP専用）。出現時にOutpostを1つマークし、そのセルへ直行して
    // マーク対象のみを攻撃する。マーク対象が消失した場合(markedOutpostがUnityのfake-nullになった場合)は
    // 再マークせず、以後は通常のエネミーと同様にコアを目指すがタワーへの攻撃は行わない
    [SerializeField] private bool isSiegeMarker = false;
    private Tower markedOutpost = null;
    // Step 4-5 バグ修正: 出現時に一度でもマークに成功したかどうか（markedOutpostが最初からnullだった
    // 「出現時に盤面にOutpostが1つも無かった」ケースと、マーク成功後に対象を見失ったケースを区別するため）
    private bool hasMarkedOutpostOnSpawn = false;
    // マーク対象を見失った（markedOutpostがfake-nullになった）ことを検知し、コアへの経路を
    // 取り直す処理を1回だけ実行するためのフラグ
    private bool siegeTargetLost = false;

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
        set => armor = Mathf.Clamp(value, 0f, CombatUtils.MaxArmor);
    }

    public event Action OnEnemyDestroyed;

    private HealthDisplay healthDisplay;

    // Frost Action: スロウシステム
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // Step 4-1: 撃破ボーナスの帰属判定用。最後に自身へダメージを与えたプレイヤーのownerId（Last Hit方式）
    private int lastDamageOwnerId = 0;
    public void SetLastDamageOwner(int ownerId)
    {
        lastDamageOwnerId = ownerId;
    }

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

        // Step 4-5 バグ修正: マーク成功済みのSiege Markerが対象Outpostを見失った場合、
        // 古い経路（Outpostのセルで終端する経路）を持ったままになりReachCore()が盤面途中で
        // 呼ばれてしまう。ここで検知し、その場でコアへの経路を取り直す（1回だけ）
        if (isSiegeMarker && hasMarkedOutpostOnSpawn && markedOutpost == null && !siegeTargetLost)
        {
            siegeTargetLost = true;
            RecalculatePathToCoreAfterLosingTarget();
        }

        // 移動判定：バリケードバスター/シージマーカーでロックターゲットがある場合は移動を停止し、それ以外は通常どおり前進
        if ((isBarricadeBuster || isSiegeMarker) && lockedAttackTarget != null)
        {
            // 対象を検知している間は移動を停止する（その場で射撃・破壊を行う）
        }
        else if (!lockTargetAndStopMoving || lockedAttackTarget == null)
        {
            MoveAlongPath();
        }

        // Frost Action: 移動と同様に、攻撃クールダウンの進行もスロウ倍率で減速させる
        fireCooldown -= Time.deltaTime * slowMultiplier;

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

        // Step 4-5: シージマーカー専用。マーク対象のOutpostのみを見る（他のタワーは一切対象にしない）。
        // マーク対象が範囲内に入った時だけロックして停止し、範囲外・消失時はロックしない（＝移動を続ける）
        if (isSiegeMarker)
        {
            if (markedOutpost != null)
            {
                float distToMark = Vector3.Distance(transform.position, markedOutpost.transform.position);
                lockedAttackTarget = (distToMark <= attackRange) ? markedOutpost : null;
            }
            else
            {
                lockedAttackTarget = null;
            }
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

    // Step 4-5 バグ修正: マーク成功済みのSiege Markerがマーク対象Outpostを見失った際、
    // 現在位置からコアまでの経路を取り直す。UpdatePath()を使うのは、SetPath()と異なり
    // 「現在の物理位置が既に経路の途中である」ことを踏まえてcurrentPathIndexを補正してくれるため
    // （このメソッドを呼ぶ時点でエネミーは盤面の途中にいる。SetPath()を使うと
    // 　transform.positionが新しい経路の先頭にワープしてしまい不自然な瞬間移動になる）
    private void RecalculatePathToCoreAfterLosingTarget()
    {
        if (AStarPathfinding.Instance == null || MapManager.Instance == null) return;

        Vector3Int currentGridPos = MapManager.Instance.WorldToGrid(transform.position);
        List<Vector3> newPath = AStarPathfinding.Instance.FindPath(currentGridPos, MapManager.Instance.CoreGridPos, IgnoreTowers, AvoidThreats);

        // EnemySpawner.SpawnEnemy()と同様、経路が見つからない場合は初期経路にフォールバックする
        if (newPath == null || newPath.Count == 0)
        {
            newPath = MapManager.Instance.GetInitialPath(currentGridPos);
        }

        if (newPath != null && newPath.Count > 0)
        {
            UpdatePath(newPath);
        }
    }

    private void ReachCore()
    {
        // Step 4-5: シージマーカーがマーク対象Outpostを保持している間、A*の目標地点はコアではなく
        // マーク対象のセル（に隣接する到達可能セル）のため、経路の終端＝コア到達ではない。
        // ここでコアダメージ処理を行うと、Outpostへ辿り着いただけでコアが誤って被弾してしまうため、
        // マーク中は経路終端到達を無視してその場に留まる（次のターゲット探索でロックされるのを待つ）
        //
        // マーク対象を見失った後は、Update()内でRecalculatePathToCoreAfterLosingTarget()により
        // 経路がコアで終端するよう既に取り直されているため、ここに到達する時点では常にコアにいる
        if (isSiegeMarker && markedOutpost != null)
        {
            return;
        }

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
        if (GameManager.Instance != null) { GameManager.Instance.AddKill(lastDamageOwnerId); }
        DestroySelf();
    }

    /// <summary>
    /// Frost Action: 移動速度と攻撃速度を一定時間低下させる。既に強いスロウがかかっている場合はより強い方を適用。
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
        // Step 4-5: シージマーカーが倒された（またはコア到達等で消滅した）際、マーク対象Outpostの
        // 追跡マーカー表示を解除する。マーク対象が既に破壊済み（fake-null）の場合は何もしない
        if (isSiegeMarker && markedOutpost != null)
        {
            markedOutpost.SetSiegeMarked(false);
        }

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

        // Step 4-5: シージマーカーはマーク対象のOutpost以外を一切攻撃しない。
        // マーク対象が消失している場合は常にnull（＝タワーへの攻撃を行わない）
        if (isSiegeMarker)
        {
            if (markedOutpost == null) return null;
            float distToMark = Vector3.Distance(pos, markedOutpost.transform.position);
            return distToMark <= attackRange ? markedOutpost : null;
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

        // 3. Step 1: Healerも通常タワーもいなければ、最後の手段としてバリケードを探索する。
        // 優先度を最下位にすることで、すり抜けるEnemy2等が後方のバリケードを先に狩る
        // 「前線は無傷なのに拠点だけ落ちる」理不尽を防ぐ
        if (bestTarget == null)
        {
            bestTarget = CombatUtils.FindNearestInRange(pos, attackRange, activeTowers,
                                                        t => t.IsBarricade);
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
        if (bullet == null) return;

        bool hasSplash = attackSplashRadius > 0f;
        bool hasDebuff = towerAttackSpeedDebuffPercent > 0f;

        if (hasSplash || hasDebuff)
        {
            BulletEffects effects = new BulletEffects
            {
                SplashRadius = attackSplashRadius,
                SplashDamageRatio = attackSplashDamageRatio,
                TowerAttackSpeedDebuffPercent = towerAttackSpeedDebuffPercent,
                TowerAttackSpeedDebuffDuration = towerAttackSpeedDebuffDuration,
            };
            bullet.Seek(target.gameObject, target, damage, effects);
        }
        else
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
        // 射程4.0では通常タワーの射程3.0の外側から一方的にバリケードを破壊できてしまい、
        // プレイヤー側に対抗手段が無かったため、隣接マス相当まで縮小する。
        attackRange = 1.5f;
        // 射程縮小により停止位置がタワーの射程内に入るため、10秒(0.1)では迎撃されて
        // ほぼ機能しなくなる。3秒相当に短縮して「破壊されるまでの猶予」として成立させる。
        fireRate = 0.33f;
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

    // Step 4-5: Siege Marker（CO-OP専用）。専用Prefabは作らず、SetupBarricadeBuster()と同じ方式で
    // 通常Enemyプレハブからステータスと色を上書きする。出現時に盤面のOutpostから1つをランダムに
    // マークし、A*の目標をそのOutpostのセルにする（GetPathTargetGridPos()経由）。
    // 盤面にOutpostが1つも無い場合はマークせず、以後は通常のエネミーと同様にコアへ向かう
    // （タワーへの攻撃はFindTarget()/SearchForSpecialTargets()側でmarkedOutpost==nullとして扱われ、行わない）
    public void SetupSiegeMarker(int waveNumber)
    {
        isSiegeMarker = true;

        // ウェーブスケーリングを先に適用（通常Enemyと同じ複利成長）。基準値はEnemy3相当のHP12.0、攻撃力4.0
        maxHp = 12.0f * GetHpScaleMultiplier(waveNumber);
        damage = 4.0f * GetDamageScaleMultiplier(waveNumber);

        speed = 1.2f;            // 遅い。予告から到達までに反応時間を作るため
        fireRate = 0.5f;         // Outpost(HP20.0)を5発≒約10秒で破壊する設計値
        attackRange = 1.5f;      // BarricadeBusterと同じく、タワー射程3.0の内側で停止させる
        armor = 0f;
        coreDamage = 1;
        ignoreTowers = false;    // 障害物は迂回する
        avoidThreats = false;    // 砲火を避けず直行する（予告どおりに来ることが重要）

        currentHp = maxHp;
        gameObject.name = "SiegeMarkerEnemy";

        // 他エネミーと区別できる濃い紫〜マゼンタ系に変更（Enemy5 Disruptorの紫と紛らわしくない濃さにする）
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = new Color(0.55f, 0.0f, 0.5f);
        }

        // 出現時に盤面のOutpostから1つをランダムに選んでマークする
        markedOutpost = SelectRandomOutpost();
        if (markedOutpost != null)
        {
            markedOutpost.SetSiegeMarked(true);
            // Step 4-5 バグ修正: マークに成功したことを記録する。これにより、後で
            // markedOutpostがfake-nullになった場合を「出現時からOutpostが存在しなかった
            // （最初からコアへ向かう正しい経路を持っている）」ケースと区別できる
            hasMarkedOutpostOnSpawn = true;
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.ShowSiegeInboundWarning(markedOutpost.OwnerId, markedOutpost.OutpostNumber);
            }
        }
    }

    // 盤面上のOutpost（IsBarricade）からランダムに1つ選ぶ。1つも無ければnull
    private Tower SelectRandomOutpost()
    {
        if (TowerManager.Instance == null) return null;

        List<Tower> activeTowers = TowerManager.Instance.GetActiveTowers();
        List<Tower> outposts = new List<Tower>();
        foreach (Tower t in activeTowers)
        {
            if (t != null && t.IsBarricade) outposts.Add(t);
        }
        if (outposts.Count == 0) return null;

        // Enemy.csは"using System;"を含むため、UnityEngine.Randomを明示して曖昧さを避ける
        return outposts[UnityEngine.Random.Range(0, outposts.Count)];
    }

    // Step 4-5: A*経路探索・経路再計算(TowerManager.NotifyEnemiesToRecalculatePath())が
    // 目標地点を問い合わせるためのAPI。シージマーカーがマーク対象Outpostを持つ間だけ
    // そのOutpostのセルを返し、それ以外の全エネミー（マーク消失後のシージマーカーを含む）は
    // 常にコアを返す。これにより既存の「常にコアを目指す」挙動はこの変更で一切変わらない
    public Vector3Int GetPathTargetGridPos()
    {
        if (isSiegeMarker && markedOutpost != null && MapManager.Instance != null)
        {
            return MapManager.Instance.WorldToGrid(markedOutpost.transform.position);
        }
        return MapManager.Instance != null ? MapManager.Instance.CoreGridPos : Vector3Int.zero;
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
