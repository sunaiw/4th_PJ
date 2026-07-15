using UnityEngine;

public class TowerRangeIndicator : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;

    public void Init(float range, Color color)
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        // 動的に円スプライトを生成
        spriteRenderer.sprite = CreateCircleSprite();
        spriteRenderer.color = color;
        // 地形の上、キャラクターやタワーの下になるように順序を設定 (Sorting Order 1)
        spriteRenderer.sortingOrder = 1;
        
        UpdateRange(range);
    }

    public void UpdateRange(float range)
    {
        // pixelsPerUnit = size / 2f で作られているスプライトのため、直径は2、半径は1ユニット。
        // 親オブジェクトのスケール変更による影響に備えつつ、損失スケールを考慮して設定する。
        if (transform.parent != null)
        {
            Vector3 parentScale = transform.parent.lossyScale;
            // 0除算防止
            float scaleX = parentScale.x != 0 ? range / parentScale.x : range;
            float scaleY = parentScale.y != 0 ? range / parentScale.y : range;
            transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }
        else
        {
            transform.localScale = new Vector3(range, range, 1f);
        }
    }

    public void SetColor(Color color)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = color;
        }
    }

    public void SetVisible(bool visible)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = visible;
        }
    }

    private static Sprite cachedCircleSprite = null;

    private Sprite CreateCircleSprite()
    {
        if (cachedCircleSprite != null) return cachedCircleSprite;

        int size = 256; // より精密な円を描くためにサイズを256に変更
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        
        float center = size / 2.0f;
        float innerRadius = center - 4f; // 境界を少し滑らかにする用

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (dist < center)
                {
                    // 境界付近でアンチエイリアシング
                    float alpha = 1.0f - Mathf.Clamp01((dist - innerRadius) / 4f);
                    
                    // 内側は非常に薄い塗りつぶし、エッジ部はくっきりした輪郭線にする
                    float fillAlpha = 0.12f;
                    if (dist >= center - 8f)
                    {
                        // 輪郭線のフェードイン/フェードアウト
                        fillAlpha = Mathf.Lerp(0.9f, 0.12f, (dist - (center - 8f)) / 8f);
                    }
                    
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, fillAlpha * alpha));
                }
                else
                {
                    texture.SetPixel(x, y, Color.clear);
                }
            }
        }
        texture.Apply();
        
        // pixelsPerUnit = size / 2f (半径を1ユニットにする)
        float pixelsPerUnit = size / 2f;
        cachedCircleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        return cachedCircleSprite;
    }
}
