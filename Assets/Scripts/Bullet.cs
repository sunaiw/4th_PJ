using System.Collections.Generic;
using UnityEngine;

// 弾が着弾時に適用する追加効果。プール再利用時の取り残しを防ぐため、
// 既定値(default)がすべて「効果なし」を意味するように設計している。
public struct BulletEffects
{
    public float FrostSlowPercent;
    public float FrostSlowDuration;

    public bool PiercingEnabled;
    public float PiercingDamageRatio;

    // SplashRadius > 0 のときのみ範囲ダメージが発生する
    public float SplashRadius;
    public float SplashDamageRatio;

    // TowerAttackSpeedDebuffPercent > 0 のとき、命中したタワーの攻撃速度を低下させる
    public float TowerAttackSpeedDebuffPercent;
    public float TowerAttackSpeedDebuffDuration;

    // Step 4-1: 発射元プレイヤーのownerId（撃破ボーナスの帰属に使用）。既定値0はシングルプレイ/Player1相当
    public int OwnerId;
}

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 5.0f;

    private GameObject targetObj;
    private IDamageable targetDamageable;
    private float damage;

    // C-3: プーリング用の参照
    [HideInInspector] public GameObject sourcePrefab;

    private BulletEffects effects;

    /// <summary>
    /// 弾をプールから取得（プールが無い場合はInstantiate）し、発射元プレハブを記録して返す。
    /// プレハブにBulletが付いていない場合はnullを返す。
    /// </summary>
    public static Bullet Spawn(GameObject prefab, Vector3 position)
    {
        GameObject bulletObj = BulletPool.Instance != null
            ? BulletPool.Instance.Get(prefab, position, Quaternion.identity)
            : Instantiate(prefab, position, Quaternion.identity);

        Bullet bullet = bulletObj != null ? bulletObj.GetComponent<Bullet>() : null;
        if (bullet != null)
        {
            bullet.sourcePrefab = prefab;
        }
        return bullet;
    }

    /// <summary>
    /// 追加効果なしのSeek。プール再利用時対策として効果パラメータは既定値にリセットされる。
    /// </summary>
    public void Seek(GameObject target, IDamageable damageable, float dmg)
    {
        Seek(target, damageable, dmg, default);
    }

    /// <summary>
    /// 追加効果付きのSeek（Frost / Piercing / Splash）。
    /// </summary>
    public void Seek(GameObject target, IDamageable damageable, float dmg, BulletEffects bulletEffects)
    {
        targetObj = target;
        targetDamageable = damageable;
        damage = dmg;
        effects = bulletEffects;
    }

    private void Update()
    {
        if (targetObj == null)
        {
            // ターゲットが既に破壊された場合は弾をプールに返却
            ReturnToPool();
            return;
        }

        Vector3 dir = targetObj.transform.position - transform.position;
        float distanceThisFrame = speed * Time.deltaTime;

        if (dir.magnitude <= distanceThisFrame)
        {
            HitTarget();
            return;
        }

        transform.Translate(dir.normalized * distanceThisFrame, Space.World);
    }

    private void HitTarget()
    {
        Vector3 hitPosition = transform.position;

        if (targetDamageable != null && targetObj != null)
        {
            // Step 4-1: 直撃ダメージの直前に、対象がEnemyなら撃破帰属先(ownerId)を記録する。
            // IDamageableインターフェース自体は変更せず、型判定のみで済ませる最小侵襲な方式
            if (targetDamageable is Enemy targetEnemyForOwner)
            {
                targetEnemyForOwner.SetLastDamageOwner(effects.OwnerId);
            }
            targetDamageable.TakeDamage(damage);

            // Frost Action: ターゲットにスロウ適用
            if (effects.FrostSlowPercent > 0f)
            {
                Enemy targetEnemy = targetObj.GetComponent<Enemy>();
                if (targetEnemy != null)
                {
                    targetEnemy.ApplySlow(effects.FrostSlowPercent, effects.FrostSlowDuration);
                }
            }

            // 敵デバッファー: 命中したタワーの攻撃速度を低下させる
            if (effects.TowerAttackSpeedDebuffPercent > 0f)
            {
                Tower targetTower = targetObj.GetComponent<Tower>();
                if (targetTower != null)
                {
                    targetTower.ApplyAttackSpeedDebuff(effects.TowerAttackSpeedDebuffPercent,
                                                       effects.TowerAttackSpeedDebuffDuration);
                }
            }
        }

        // Piercing Shot: 着弾地点の近隣の別の敵にも追加ダメージ
        if (effects.PiercingEnabled && effects.PiercingDamageRatio > 0f)
        {
            ApplyPiercingDamage(hitPosition);
        }

        // Splash: 着弾地点の周囲へ範囲ダメージ
        if (effects.SplashRadius > 0f && effects.SplashDamageRatio > 0f)
        {
            ApplySplashDamage(hitPosition);
        }

        ReturnToPool();
    }

    private void ApplyPiercingDamage(Vector3 hitPos)
    {
        if (EnemySpawner.Instance == null) return;

        List<Enemy> activeEnemies = EnemySpawner.Instance.GetActiveEnemies();
        float piercingRange = 1.5f; // 貫通範囲（セル単位）
        float piercingDamage = damage * effects.PiercingDamageRatio;

        Enemy closestOther = null;
        float closestDist = float.MaxValue;

        for (int i = 0; i < activeEnemies.Count; i++)
        {
            Enemy enemy = activeEnemies[i];
            if (enemy == null) continue;
            // 元のターゲットは除外
            if (targetObj != null && enemy.gameObject == targetObj) continue;

            float dist = Vector3.Distance(hitPos, enemy.transform.position);
            if (dist <= piercingRange && dist < closestDist)
            {
                closestDist = dist;
                closestOther = enemy;
            }
        }

        if (closestOther != null)
        {
            // Step 4-1: 貫通ダメージで倒した敵も発射元に帰属させる
            closestOther.SetLastDamageOwner(effects.OwnerId);
            closestOther.TakeDamage(piercingDamage);

            // 貫通先にもFrost効果を適用
            if (effects.FrostSlowPercent > 0f)
            {
                closestOther.ApplySlow(effects.FrostSlowPercent, effects.FrostSlowDuration);
            }
        }
    }

    // 着弾地点の周囲に範囲ダメージを与える。
    // 攻撃対象がEnemyならEnemy群へ、Tower(=敵弾)ならTower群へ波及させる。
    private void ApplySplashDamage(Vector3 hitPos)
    {
        float splashDamage = damage * effects.SplashDamageRatio;
        if (splashDamage <= 0f) return;

        if (targetDamageable is Enemy)
        {
            if (EnemySpawner.Instance == null) return;
            List<Enemy> activeEnemies = EnemySpawner.Instance.GetActiveEnemies();
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                Enemy enemy = activeEnemies[i];
                if (enemy == null) continue;
                // 直撃対象は既にダメージ済みなので除外（二重ダメージ防止）
                if (targetObj != null && enemy.gameObject == targetObj) continue;
                if (Vector3.Distance(hitPos, enemy.transform.position) > effects.SplashRadius) continue;

                // Step 4-1: 範囲ダメージで倒した敵も発射元に帰属させる
                enemy.SetLastDamageOwner(effects.OwnerId);
                enemy.TakeDamage(splashDamage);
                if (effects.FrostSlowPercent > 0f)
                {
                    enemy.ApplySlow(effects.FrostSlowPercent, effects.FrostSlowDuration);
                }
            }
        }
        else
        {
            if (TowerManager.Instance == null) return;
            List<Tower> activeTowers = TowerManager.Instance.GetActiveTowers();
            for (int i = 0; i < activeTowers.Count; i++)
            {
                Tower tower = activeTowers[i];
                if (tower == null) continue;
                if (targetObj != null && tower.gameObject == targetObj) continue;
                // Step 1: バリケードも通常のダメージ処理経路になったため、範囲ダメージの対象に含める
                if (Vector3.Distance(hitPos, tower.transform.position) > effects.SplashRadius) continue;

                tower.TakeDamage(splashDamage);
            }
        }
    }

    private void ReturnToPool()
    {
        // C-3: プールに返却（プールが存在しない場合はDestroyにフォールバック）
        if (BulletPool.Instance != null && sourcePrefab != null)
        {
            targetObj = null;
            targetDamageable = null;
            BulletPool.Instance.Return(sourcePrefab, gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
