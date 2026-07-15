using UnityEngine;

public class CoreHPDisplay : MonoBehaviour
{
    private HealthDisplay healthDisplay;
    private int initialLife = 10;

    private void Start()
    {
        // OnMouseEnter用のコライダー自動追加 (Coreは2x2なのでサイズ2x2にする)
        if (GetComponent<Collider2D>() == null)
        {
            BoxCollider2D boxColl = gameObject.AddComponent<BoxCollider2D>();
            boxColl.size = new Vector2(2f, 2f);
            boxColl.isTrigger = true;
        }

        if (GameManager.Instance != null)
        {
            initialLife = GameManager.Instance.InitialLife;
        }

        healthDisplay = gameObject.AddComponent<HealthDisplay>();
        healthDisplay.Init(new Vector3(-1.2f, 1.5f, -1.0f));

        UpdateLifeDisplay(GameManager.Instance != null ? GameManager.Instance.Life : initialLife);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLifeChanged += UpdateLifeDisplay;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLifeChanged -= UpdateLifeDisplay;
        }
    }

    private void UpdateLifeDisplay(int life)
    {
        if (healthDisplay != null)
        {
            healthDisplay.UpdateHPText(life, initialLife);
        }
    }

    private void OnMouseEnter()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        if (healthDisplay != null)
        {
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
}
