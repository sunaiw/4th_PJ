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
    private static readonly Color ButtonDisabledColor = new Color(0.2f, 0.5f, 0.35f, 0.3f);
    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.75f);

    private GameObject confirmOverlayObj;

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

        // CONTINUEボタン。オートセーブが存在しない場合は非活性にする
        bool hasSave = SaveSystem.HasSave();
        GameObject continueButtonObj = CreateButton(canvasObj.transform, "ContinueButton", "CONTINUE", new Vector2(0f, 60f), OnContinueClicked);
        Button continueButton = continueButtonObj.GetComponent<Button>();
        continueButton.interactable = hasSave;
        Image continueImage = continueButtonObj.GetComponent<Image>();
        continueImage.color = hasSave ? ButtonColor : ButtonDisabledColor;

        // STARTボタン
        CreateButton(canvasObj.transform, "StartButton", "START", new Vector2(0f, -80f), OnStartClicked);

        // QUITボタン (エディタでは非表示動作だが押下は可能にしておく)
        CreateButton(canvasObj.transform, "QuitButton", "QUIT", new Vector2(0f, -220f), OnQuitClicked);

        CreateConfirmOverlay(canvasObj.transform);
    }

    // 既存セーブを上書きしてよいか確認するオーバーレイ（STARTを既存セーブがある状態で押した場合のみ表示）
    private void CreateConfirmOverlay(Transform parent)
    {
        confirmOverlayObj = new GameObject("ConfirmOverwriteOverlay");
        confirmOverlayObj.transform.SetParent(parent, false);

        Image overlayImage = confirmOverlayObj.AddComponent<Image>();
        overlayImage.color = OverlayColor;
        RectTransform overlayRect = confirmOverlayObj.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        GameObject messageObj = new GameObject("Message");
        messageObj.transform.SetParent(confirmOverlayObj.transform, false);
        TextMeshProUGUI messageText = messageObj.AddComponent<TextMeshProUGUI>();
        messageText.text = "Starting a new game will overwrite your saved game.\nContinue?";
        messageText.fontSize = 32;
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.color = Color.white;
        RectTransform messageRect = messageObj.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.5f, 0.5f);
        messageRect.anchorMax = new Vector2(0.5f, 0.5f);
        messageRect.pivot = new Vector2(0.5f, 0.5f);
        messageRect.anchoredPosition = new Vector2(0f, 80f);
        messageRect.sizeDelta = new Vector2(900f, 160f);

        CreateButton(confirmOverlayObj.transform, "ConfirmYesButton", "START NEW GAME", new Vector2(-170f, -60f), OnConfirmOverwriteYesClicked);
        CreateButton(confirmOverlayObj.transform, "ConfirmNoButton", "CANCEL", new Vector2(170f, -60f), OnConfirmOverwriteNoClicked);

        confirmOverlayObj.SetActive(false);
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
        colors.disabledColor = ButtonDisabledColor;
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

    private void OnContinueClicked()
    {
        GameSaveData data = SaveSystem.Load();
        if (data == null) return;

        SaveSystem.PendingLoad = data;
        TutorialUI.SkipTutorial = true; // CONTINUE時はチュートリアルを再表示しない
        SceneManager.LoadScene(MainSceneName);
    }

    private void OnStartClicked()
    {
        if (SaveSystem.HasSave())
        {
            confirmOverlayObj.SetActive(true);
            return;
        }

        StartNewGame();
    }

    private void OnConfirmOverwriteYesClicked()
    {
        StartNewGame();
    }

    private void OnConfirmOverwriteNoClicked()
    {
        confirmOverlayObj.SetActive(false);
    }

    private void StartNewGame()
    {
        // 新規開始直後に落ちた場合に古いセーブが残り、CONTINUEが前周回の盤面を指したままになるのを防ぐ
        SaveSystem.DeleteSave();
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
