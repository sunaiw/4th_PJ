using UnityEngine;
using UnityEngine.EventSystems;

public class TowerDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public TowerManager.PlacementType placementType = TowerManager.PlacementType.Tower;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!enabled) return;
        bool isSetupPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Setup;
        if (isSetupPhase)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0.6f;
            }

            if (TowerManager.Instance != null)
            {
                TowerManager.Instance.StartDragPlacement(placementType);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Unity UI の仕様上、OnDrag が空でも実装されていないと Drag イベントが正しく伝搬しないことがあります。
        // ドラッグ中は、TowerManagerがマウスポジションを元にワールドプレビューを更新するため、UIパーツ自体の移動は行いません。
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1.0f;
        }

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.EndDragPlacement();
        }
    }
}
