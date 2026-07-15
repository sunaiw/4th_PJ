using UnityEngine;

public class HealthDisplay : MonoBehaviour
{
    private TextMesh hpTextMesh;

    public void Init(Vector3 localPosition)
    {
        GameObject hpTextObj = new GameObject("HP_Text_Display");
        hpTextObj.transform.SetParent(transform);
        hpTextObj.transform.localPosition = localPosition;
        
        hpTextMesh = hpTextObj.AddComponent<TextMesh>();
        hpTextMesh.fontSize = 40;
        hpTextMesh.characterSize = 0.15f;
        hpTextMesh.anchor = TextAnchor.MiddleCenter;
        hpTextMesh.alignment = TextAlignment.Center;
        hpTextMesh.color = Color.green;

        MeshRenderer meshRenderer = hpTextObj.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingOrder = 100;
            meshRenderer.sortingLayerName = "Default";
        }
        
        hpTextObj.SetActive(false);
    }

    public void UpdateHPText(float currentHp, float maxHp)
    {
        if (hpTextMesh != null)
        {
            hpTextMesh.text = $"HP: {currentHp:F1}/{maxHp:F1}";
            float hpPercent = currentHp / maxHp;
            if (hpPercent > 0.5f)
                hpTextMesh.color = Color.green;
            else if (hpPercent > 0.2f)
                hpTextMesh.color = Color.yellow;
            else
                hpTextMesh.color = Color.red;
        }
    }

    public void SetVisible(bool isVisible)
    {
        if (hpTextMesh != null)
        {
            hpTextMesh.gameObject.SetActive(isVisible);
        }
    }
}
