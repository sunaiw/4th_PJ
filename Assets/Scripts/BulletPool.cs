using System.Collections.Generic;
using UnityEngine;

// C-3: Bulletオブジェクトプーリングシステム
public class BulletPool : MonoBehaviour
{
    public static BulletPool Instance { get; private set; }

    [SerializeField] private int initialPoolSize = 30;

    private Dictionary<GameObject, Queue<GameObject>> pools = new Dictionary<GameObject, Queue<GameObject>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        if (!pools.ContainsKey(prefab))
        {
            pools[prefab] = new Queue<GameObject>();
        }

        Queue<GameObject> pool = pools[prefab];
        GameObject obj = null;

        // プールから無効なオブジェクトを除外しつつ取得
        while (pool.Count > 0)
        {
            obj = pool.Dequeue();
            if (obj != null) break;
            obj = null;
        }

        if (obj == null)
        {
            obj = Instantiate(prefab);
        }

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.SetActive(true);
        return obj;
    }

    public void Return(GameObject prefab, GameObject obj)
    {
        if (obj == null) return;
        obj.SetActive(false);

        if (prefab != null)
        {
            if (!pools.ContainsKey(prefab))
            {
                pools[prefab] = new Queue<GameObject>();
            }
            pools[prefab].Enqueue(obj);
        }
        else
        {
            Destroy(obj);
        }
    }
}
