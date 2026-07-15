using UnityEngine;

/// <summary>
/// シーン上に1つだけ存在するマネージャ用の共通シングルトン基底クラス。
/// 2つ目以降のインスタンスは自動的に破棄される。
/// </summary>
public abstract class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
{
    public static T Instance { get; private set; }

    // シーンをまたいで維持する場合はtrueを返すようオーバーライドする
    protected virtual bool PersistAcrossScenes => false;

    protected virtual void Awake()
    {
        if (Instance == null)
        {
            Instance = (T)this;
            if (PersistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
            OnSingletonAwake();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    // 初回インスタンス確定時のみ呼ばれる初期化フック
    protected virtual void OnSingletonAwake() { }
}
