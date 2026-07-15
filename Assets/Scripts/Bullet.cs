using System.Collections.Generic;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 5.0f;
    
    private GameObject targetObj;
    private IDamageable targetDamageable;
    private float damage;

    // C-3: プーリング用の参照
    [HideInInspector] public GameObject sourcePrefab;

    // Frost Action パラメータ
    private float frostSlowPercent = 0f;
    private float frostSlowDuration = 0f;

    // Piercing Shot パラメータ
    private bool piercingEnabled = false;
    private float piercingDamageRatio = 0f;

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
    /// 特殊効果なしのSeek（Enemy弾など）。プール再利用時対策として効果パラメータはリセットされる。
    /// </summary>
    public void Seek(GameObject target, IDamageable damageable, float dmg)
    {
        Seek(target, damageable, dmg, 0f, 0f, false, 0f);
    }

    /// <summary>
    /// タワー弾用Seek（Frost Action / Piercing Shot の効果パラメータ付き）
    /// </summary>
    public void Seek(GameObject target, IDamageable damageable, float dmg,
                     float slowPercent, float slowDuration,
                     bool piercing, float piercingRatio)
    {
        targetObj = target;
        targetDamageable = damageable;
        damage = dmg;
        frostSlowPercent = slowPercent;
        frostSlowDuration = slowDuration;
        piercingEnabled = piercing;
        piercingDamageRatio = piercingRatio;
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
            targetDamageable.TakeDamage(damage);

            // Frost Action: ターゲットにスロウ適用
            if (frostSlowPercent > 0f)
            {
                Enemy targetEnemy = targetObj.GetComponent<Enemy>();
                if (targetEnemy != null)
                {
                    targetEnemy.ApplySlow(frostSlowPercent, frostSlowDuration);
                }
            }
        }

        // Piercing Shot: 着弾地点の近隣の別の敵にも追加ダメージ
        if (piercingEnabled && piercingDamageRatio > 0f)
        {
            ApplyPiercingDamage(hitPosition);
        }

        ReturnToPool();
    }

    private void ApplyPiercingDamage(Vector3 hitPos)
    {
        if (EnemySpawner.Instance == null) return;

        List<Enemy> activeEnemies = EnemySpawner.Instance.GetActiveEnemies();
        float piercingRange = 1.5f; // 貫通範囲（セル単位）
        float piercingDamage = damage * piercingDamageRatio;

        Enemy closestOther = null;
        float closestDist = float.MaxValue;

        foreach (Enemy enemy in activeEnemies)
        {
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
            closestOther.TakeDamage(piercingDamage);

            // 貫通先にもFrost効果を適用
            if (frostSlowPercent > 0f)
            {
                closestOther.ApplySlow(frostSlowPercent, frostSlowDuration);
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
