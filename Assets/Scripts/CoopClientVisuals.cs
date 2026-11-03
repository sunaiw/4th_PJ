using UnityEngine;

// Step 4-0b-4: クライアントのミラー表示用ローカル演出。
// ホストの発射イベント（CoopStateSyncManager.OnRemoteTowerFired）を受けて、見た目だけの弾を飛ばす。
// ダメージ等は一切発生しない（弾はBullet.SeekVisualの見た目専用モード）。
//
// CoopStateSyncManagerと同じ理由（GameManager.Start()からAddComponentで動的生成されるため）で
// SingletonBehaviour<T>は使わない。ホスト/シングルでは発射イベントが来ないため何も起きない
public class CoopClientVisuals : MonoBehaviour
{
    private CoopStateSyncManager subscribedTo;

    private void Start()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // CoopStateSyncManager.Instanceがまだ無かった場合に備え、購読できるまで毎フレーム再試行する
        if (subscribedTo == null) TrySubscribe();
    }

    private void TrySubscribe()
    {
        CoopStateSyncManager sync = CoopStateSyncManager.Instance;
        if (sync == null) return;
        sync.OnRemoteTowerFired += HandleRemoteTowerFired;
        subscribedTo = sync;
    }

    private void OnDestroy()
    {
        if (subscribedTo != null)
        {
            subscribedTo.OnRemoteTowerFired -= HandleRemoteTowerFired;
            subscribedTo = null;
        }
    }

    // effectKind（Splash/Slow/Piercing）は視覚化しない。ホストの弾も種別で見た目を変えていないため、
    // クライアントも同じ見た目の弾を飛ばすだけにしている
    private void HandleRemoteTowerFired(ushort towerNetId, ushort enemyNetId, byte effectKind)
    {
        CoopStateSyncManager sync = subscribedTo;
        if (sync == null) return;

        // イベントとスナップショットは別チャンネルなので、対応するプロキシがまだ無い/既に消えている場合がある。その時は何もしない
        if (!sync.TryGetTowerProxy(towerNetId, out Tower tower)) return;
        if (!sync.TryGetEnemyProxy(enemyNetId, out Enemy enemy)) return;

        GameObject prefab = tower.BulletPrefab;
        if (prefab == null) return;

        Bullet bullet = Bullet.Spawn(prefab, tower.transform.position);
        if (bullet == null) return;
        bullet.SeekVisual(enemy.gameObject);
    }
}
