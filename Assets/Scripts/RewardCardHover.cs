using UnityEngine;
using UnityEngine.EventSystems;

// Step 4-8: 報酬カードへのマウスホバーをRewardUIへ通知する小コンポーネント。
// RewardUIがStart()で各カードにAddComponentする。ホバーの共有可否（選択者本人のみ）はRewardUI/RewardManager側で判定する
public class RewardCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private RewardUI owner;
    private int cardIndex;

    public void Init(RewardUI ui, int index)
    {
        owner = ui;
        cardIndex = index;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner != null) owner.OnCardHoverEnter(cardIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (owner != null) owner.OnCardHoverExit(cardIndex);
    }
}
