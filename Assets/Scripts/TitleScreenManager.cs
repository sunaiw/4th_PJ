using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// タイトル画面のUIを動的に生成し、ゲーム開始操作を扱う。
/// ロゴ・背景画像は未用意のため仮のオブジェクトで代替している。差し替え時はCreateTitleLayout内を更新すること。
/// </summary>
public class TitleScreenManager : MonoBehaviour
{
    private const string MainSceneName = "MainGame";

    private static readonly Color BackgroundColor = new Color(0.1f, 0.12f, 0.18f, 1f);
    private static readonly Color ButtonColor = new Color(0.2f, 0.5f, 0.35f, 0.9f);
    private static readonly Color ButtonHoverColor = new Color(0.25f, 0.6f, 0.42f, 0.9f);

    private void Start()
    {
        CreateTitleLayout();
    }

    private void CreateTitleLayout()
    {
        GameObject canvasObj = new GameObject("TitleCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 仮の背景(単色パネル)。背景画像が用意でき次第Imageのspriteを差し替える
        GameObject bgObj = new GameObject("BackgroundPlaceholder");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = BackgroundColor;
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // 仮のロゴ(テキスト)。ロゴ画像が用意でき次第Imageに差し替える
        GameObject logoObj = new GameObject("LogoPlaceholder");
        logoObj.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI logoText = logoObj.AddComponent<TextMeshProUGUI>();
        logoText.text = "TOWER DEFENSE";
        logoText.fontSize = 96;
        logoText.fontStyle = FontStyles.Bold;
        logoText.alignment = TextAlignmentOptions.Center;
        logoText.color = Color.white;
        RectTransform logoRect = logoObj.GetComponent<RectTransform>();
        logoRect.anchorMin = new Vector2(0.5f, 0.65f);
        logoRect.anchorMax = new Vector2(0.5f, 0.65f);
        logoRect.pivot = new Vector2(0.5f, 0.5f);
        logoRect.anchoredPosition = Vector2.zero;
        logoRect.sizeDelta = new Vector2(1200f, 200f);

        // STARTボタン
        CreateButton(canvasObj.transform, "StartButton", "START", new Vector2(0f, -80f), OnStartClicked);

        // QUITボタン (エディタでは非表示動作だが押下は可能にしておく)
        CreateButton(canvasObj.transform, "QuitButton", "QUIT", new Vector2(0f, -220f), OnQuitClicked);
    }

    private GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = ButtonColor;

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = new Vector2(320f, 90f);

        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonHoverColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        GameObject textObj = new GameObject(name + "Text");
        textObj.transform.SetParent(buttonObj.transform, false);
        TextMeshProUGUI buttonText = textObj.AddComponent<TextMeshProUGUI>();
        buttonText.text = label;
        buttonText.fontSize = 36;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return buttonObj;
    }

    private void OnStartClicked()
    {
        SceneManager.LoadScene(MainSceneName);
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
