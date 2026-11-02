using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI全体で共通のスタイル(ダークスレート＋ウォームゴールド)とスプライトを提供する静的ヘルパー。
/// スプライトは Resources/Art/UI/ にある明るい無彩色の9-sliceで、Image.color で着色して使う。
/// 金の縁取りは別の子Image("Frame")として重ねるため、Image.color(パネル色・ホバー色)の変更に影響されない。
/// Filledタイプのゲージ塗りつぶしには適用しないこと(fillAmountと9-sliceは併用できない)。
/// </summary>
public static class UITheme
{
    // ---- 共通パレット ----
    public static readonly Color Gold = new Color(1f, 0.84f, 0.45f, 1f);
    public static readonly Color GoldDim = new Color(0.78f, 0.62f, 0.3f, 1f);
    public static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.13f, 0.90f);
    public static readonly Color PanelBgSoft = new Color(0.07f, 0.09f, 0.13f, 0.72f);
    public static readonly Color PanelBgDeep = new Color(0.04f, 0.05f, 0.08f, 0.95f);
    public static readonly Color ButtonBg = new Color(0.20f, 0.27f, 0.38f, 0.96f);
    public static readonly Color ButtonHover = new Color(0.32f, 0.42f, 0.58f, 1f);
    public static readonly Color ButtonPressed = new Color(0.14f, 0.19f, 0.28f, 1f);
    public static readonly Color ButtonDisabled = new Color(0.20f, 0.27f, 0.38f, 0.4f);
    public static readonly Color FrameColor = new Color(1f, 0.86f, 0.52f, 0.9f);
    public static readonly Color FrameColorDim = new Color(0.8f, 0.64f, 0.34f, 0.7f);
    // Buttonコンポーネントは「Image.color × ColorBlockの各状態色」で最終色が決まる。
    // Image.colorとColorBlock.normalColorの両方へ同じ色を入れる既存の作りでは二乗されて暗くなるため、
    // その用途(Image+ColorBlockの併用)にはButtonBgの平方根にあたる下記ButtonTint系を使う。
    // (ButtonBg/ButtonDisabledはImage.colorだけで使うカード等向けの最終色)
    public static readonly Color ButtonTint = new Color(0.45f, 0.52f, 0.62f, 0.98f);
    public static readonly Color ButtonTintHover = new Color(0.71f, 0.81f, 0.94f, 1f);
    public static readonly Color ButtonTintDisabled = new Color(0.45f, 0.52f, 0.62f, 0.5f);
    public static readonly Color GaugeBg = new Color(0.05f, 0.06f, 0.09f, 0.95f);

    private const string UIPath = "Art/UI/";
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite LoadSprite(string name)
    {
        if (cache.TryGetValue(name, out Sprite s) && s != null) return s;
        s = Resources.Load<Sprite>(UIPath + name);
        cache[name] = s;
        return s;
    }

    /// <summary>パネル用の9-sliceスプライトを適用する。frame=trueで金の縁取りも重ねる。</summary>
    public static void ApplyPanel(Image image, bool frame = true)
    {
        Apply(image, "UI_Panel", "UI_PanelFrame", frame ? FrameColor : (Color?)null);
    }

    /// <summary>ボタン用の9-sliceスプライトを適用する。</summary>
    public static void ApplyButton(Image image, bool frame = true)
    {
        Apply(image, "UI_Button", "UI_ButtonFrame", frame ? FrameColor : (Color?)null);
    }

    /// <summary>ゲージ背景(溝)用の9-sliceスプライトを適用する。塗りつぶしImageには使わない。</summary>
    public static void ApplyBar(Image image, bool frame = true)
    {
        Apply(image, "UI_Bar", "UI_BarFrame", frame ? FrameColorDim : (Color?)null);
    }

    private static void Apply(Image image, string spriteName, string frameName, Color? frameColor)
    {
        if (image == null) return;
        Sprite sprite = LoadSprite(spriteName);
        if (sprite == null) return; // スプライトが無い場合は従来の単色表示のまま

        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;

        if (frameColor.HasValue)
        {
            AddFrame(image, LoadSprite(frameName), frameColor.Value);
        }
    }

    private static void AddFrame(Image host, Sprite frameSprite, Color color)
    {
        if (frameSprite == null) return;
        Transform existing = host.transform.Find("Frame");
        if (existing != null) return;

        GameObject frameObj = new GameObject("Frame");
        frameObj.transform.SetParent(host.transform, false);
        frameObj.transform.SetAsFirstSibling(); // 文字より背面に置く

        RectTransform rect = frameObj.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        // 親にLayoutGroupがあっても枠が1要素として並べられないようにする
        LayoutElement le = frameObj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        Image img = frameObj.AddComponent<Image>();
        img.sprite = frameSprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
    }

    /// <summary>タイトルロゴなどの文字に、金色の縁取りと影を付ける(フォントの追加なし)。</summary>
    public static void StyleLogoText(TMP_Text text, Color faceColor)
    {
        if (text == null) return;
        text.color = faceColor;
        text.fontStyle = FontStyles.Bold;
        text.outlineColor = new Color32(222, 160, 40, 255); // 金の縁取り
        text.outlineWidth = 0.3f;
        // 影はアンダーレイで表現する(マテリアルインスタンスを生成する)
        Material mat = text.fontMaterial;
        if (mat != null && mat.HasProperty(ShaderUtilities.ID_UnderlayColor))
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.8f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
        }
    }

    /// <summary>タワープレハブのスプライトをカード用アイコンとして取得する(専用アイコンアセットは持たない)。</summary>
    public static Sprite GetTowerIcon(GameObject towerPrefab)
    {
        if (towerPrefab == null) return null;
        SpriteRenderer sr = towerPrefab.GetComponent<SpriteRenderer>();
        return sr != null ? sr.sprite : null;
    }
}
