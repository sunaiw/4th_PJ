using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// タイトル画面のUIを動的に生成し、ゲーム開始操作を扱う。
/// 背景は Resources/Art/UI/TitleBackground、ロゴは金縁取り付きのテキストで表現している。差し替え時はCreateTitleLayout内を更新すること。
/// </summary>
public class TitleScreenManager : MonoBehaviour
{
    private const string MainSceneName = "MainGame";

    private static readonly Color BackgroundColor = new Color(0.04f, 0.05f, 0.09f, 1f); // 背景画像が無い場合のフォールバック色
    private static readonly Color ButtonColor = UITheme.ButtonTint;
    private static readonly Color ButtonHoverColor = UITheme.ButtonTintHover;
    private static readonly Color ButtonDisabledColor = UITheme.ButtonTintDisabled;
    private static readonly Color OverlayColor = new Color(0.02f, 0.03f, 0.06f, 0.94f);

    private GameObject confirmOverlayObj;
    private GameObject modeSelectOverlayObj;

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

        // 背景画像(森・コア・スポナーの合成)。画像が無い場合は単色にフォールバックする
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = bgObj.AddComponent<Image>();
        Sprite bgSprite = UITheme.LoadSprite("TitleBackground");
        bgImage.sprite = bgSprite;
        bgImage.color = bgSprite != null ? Color.white : BackgroundColor;
        bgImage.raycastTarget = false;
        RectTransform bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // ロゴ(テキスト)。金の縁取りと影で装飾する。ロゴ画像が用意できたらImageに差し替える
        GameObject logoObj = new GameObject("Logo");
        logoObj.transform.SetParent(canvasObj.transform, false);
        TextMeshProUGUI logoText = logoObj.AddComponent<TextMeshProUGUI>();
        logoText.text = "TOWER DEFENSE";
        logoText.fontSize = 96;
        logoText.fontStyle = FontStyles.Bold;
        logoText.alignment = TextAlignmentOptions.Center;
        logoText.characterSpacing = 6f;
        UITheme.StyleLogoText(logoText, new Color(1f, 0.95f, 0.78f, 1f));
        RectTransform logoRect = logoObj.GetComponent<RectTransform>();
        logoRect.anchorMin = new Vector2(0.5f, 0.65f);
        logoRect.anchorMax = new Vector2(0.5f, 0.65f);
        logoRect.pivot = new Vector2(0.5f, 0.5f);
        logoRect.anchoredPosition = Vector2.zero;
        logoRect.sizeDelta = new Vector2(1200f, 200f);

        // ロゴ下の装飾線
        Sprite dividerSprite = UITheme.LoadSprite("UI_Divider");
        if (dividerSprite != null)
        {
            GameObject dividerObj = new GameObject("LogoDivider");
            dividerObj.transform.SetParent(canvasObj.transform, false);
            Image dividerImage = dividerObj.AddComponent<Image>();
            dividerImage.sprite = dividerSprite;
            dividerImage.raycastTarget = false;
            RectTransform dividerRect = dividerObj.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0.5f, 0.65f);
            dividerRect.anchorMax = new Vector2(0.5f, 0.65f);
            dividerRect.pivot = new Vector2(0.5f, 0.5f);
            dividerRect.anchoredPosition = new Vector2(0f, -52f);
            dividerRect.sizeDelta = new Vector2(900f, 40f);
        }

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
        CreateModeSelectOverlay(canvasObj.transform);
    }

    // SINGLE PLAYER / CO-OPを選ぶためのオーバーレイ（STARTボタン押下時に表示）。
    // 既存のConfirmOverwriteOverlayと同じ見た目（暗転＋中央ボタン）で統一する
    private void CreateModeSelectOverlay(Transform parent)
    {
        modeSelectOverlayObj = new GameObject("ModeSelectOverlay");
        modeSelectOverlayObj.transform.SetParent(parent, false);

        Image overlayImage = modeSelectOverlayObj.AddComponent<Image>();
        overlayImage.color = OverlayColor;
        RectTransform overlayRect = modeSelectOverlayObj.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        GameObject messageObj = new GameObject("Message");
        messageObj.transform.SetParent(modeSelectOverlayObj.transform, false);
        TextMeshProUGUI messageText = messageObj.AddComponent<TextMeshProUGUI>();
        messageText.text = "Select Mode";
        messageText.fontSize = 36;
        messageText.fontStyle = FontStyles.Bold;
        messageText.alignment = TextAlignmentOptions.Center;
        messageText.color = Color.white;
        RectTransform messageRect = messageObj.GetComponent<RectTransform>();
        messageRect.anchorMin = new Vector2(0.5f, 0.5f);
        messageRect.anchorMax = new Vector2(0.5f, 0.5f);
        messageRect.pivot = new Vector2(0.5f, 0.5f);
        messageRect.anchoredPosition = new Vector2(0f, 170f);
        messageRect.sizeDelta = new Vector2(900f, 120f);

        CreateButton(modeSelectOverlayObj.transform, "SinglePlayerButton", "SINGLE PLAYER", new Vector2(0f, 40f), OnSinglePlayerClicked);
        CreateButton(modeSelectOverlayObj.transform, "CoopButton", "CO-OP", new Vector2(0f, -70f), OnCoopClicked);
        CreateButton(modeSelectOverlayObj.transform, "ModeSelectBackButton", "BACK", new Vector2(0f, -180f), OnModeSelectBackClicked);

        modeSelectOverlayObj.SetActive(false);
    }

    // 既存セーブを上書きしてよいか確認するオーバーレイ（モード選択でSINGLE PLAYERを選び、既存セーブがある場合のみ表示）
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
        UITheme.ApplyButton(buttonImage);

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
        buttonText.color = new Color(1f, 0.96f, 0.85f, 1f);
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

        GameManager.LaunchCoopMode = false; // セーブデータはシングルプレイ専用のため明示的に固定する
        SaveSystem.PendingLoad = data;
        TutorialUI.SkipTutorial = true; // CONTINUE時はチュートリアルを再表示しない
        SceneManager.LoadScene(MainSceneName);
    }

    // STARTはまずモード選択オーバーレイを開く（SINGLE PLAYER / CO-OPの選択はここで確定する）
    private void OnStartClicked()
    {
        modeSelectOverlayObj.SetActive(true);
    }

    // モード選択: SINGLE PLAYER。既存セーブがある場合のみ上書き確認を挟み、無ければそのまま新規開始する
    private void OnSinglePlayerClicked()
    {
        GameManager.LaunchCoopMode = false;
        modeSelectOverlayObj.SetActive(false);

        if (SaveSystem.HasSave())
        {
            confirmOverlayObj.SetActive(true);
            return;
        }

        StartNewGame();
    }

    // モード選択: CO-OP。CO-OPはセーブ機能自体を使わない（PauseMenuUIがSAVEを非活性にする）ため、
    // シングルプレイ用の既存セーブは削除せずそのまま残す
    private void OnCoopClicked()
    {
        GameManager.LaunchCoopMode = true;
        modeSelectOverlayObj.SetActive(false);
        SceneManager.LoadScene(MainSceneName);
    }

    private void OnModeSelectBackClicked()
    {
        modeSelectOverlayObj.SetActive(false);
    }

    private void OnConfirmOverwriteYesClicked()
    {
        StartNewGame();
    }

    // CANCELは上書き確認の1つ手前（モード選択）へ戻す。このオーバーレイはSINGLE PLAYER経由でのみ表示されるため、
    // 戻り先は常にモード選択で一貫する
    private void OnConfirmOverwriteNoClicked()
    {
        confirmOverlayObj.SetActive(false);
        modeSelectOverlayObj.SetActive(true);
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
