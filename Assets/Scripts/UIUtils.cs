using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// UI・マウス入力まわりの共通処理。
/// </summary>
public static class UIUtils
{
    /// <summary>
    /// マウスポインタがUIの上にあるかどうか。
    /// </summary>
    public static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// マウスホバー検出用のトリガーコライダーが無ければ追加する。
    /// </summary>
    public static void EnsureTriggerCollider2D(GameObject go, Vector2 size)
    {
        if (go.GetComponent<Collider2D>() == null)
        {
            BoxCollider2D boxCol = go.AddComponent<BoxCollider2D>();
            boxCol.isTrigger = true;
            boxCol.size = size;
        }
    }
}
