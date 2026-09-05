using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// ESCキーで開閉するポーズメニュー。RESUME / SAVE(Setupフェーズ中のみ) / SAVE & TITLE / QUIT GAME を提供する。
// GameOverUI/TutorialUIがTime.timeScaleを0に固定して表示している間、およびGameOver中は開かない。
public class PauseMenuUI : MonoBehaviour
{
    private const string TitleSceneName = "TitleScene";

    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color ButtonColor = new Color(0.25f, 0.3f, 0.38f, 0.95f);
    private static readonly Color ButtonHoverColor = new Color(0.32f, 0.4f, 0.5f, 0.95f);
    private static readonly Color ButtonDisabledColor = new Color(0.25f, 0.3f, 0.38f, 0.4f);
    private static readonly Vector2 ButtonSize = new Vector2(280f, 72f);

    private GameObject canvasObj;
    private Button saveButton;
    private Button saveAndTitleButton;
    private TMP_Text saveAndTitleButtonText;
    private TMP_Text saveHintText;
    private TMP_Text statusText;

    private bool isOpen = false;
    private float savedTimeScale = 1f;
    private float statusHideTimer = -1f;

    private void Start()
    {
        CreateLayout();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isOpen)
            {
                Close();
            }
            else if (CanOpen())
            {
                Open();
            }
        }

        if (isOpen)
        {
            RefreshSaveState();
        }

        if (statusHideTimer >= 0f)
        {
            statusHideTimer -= Time.unscaledDeltaTime;
            if (statusHideTimer < 0f)
            {
                statusHideTimer = -1f;
                if (statusText != null) statusText.gameObject.SetActive(false);
            }
        }
    }

    // GameOver中、またはTutorial等の他UIがtimeScaleを0に固定して表示中は開かない
    // （自分がPause中でないのにtimeScale==0の場合は他UIが制御中と判断する。GameSpeedControllerと同じ判定）
    private bool CanOpen()
    {
        if (GameManager.Instance == null) return false;
        if (GameManager.Instance.CurrentPhase == GamePhase.GameOver) return false;
        if (Time.timeScale == 0f) return false;
        return true;
    }

    private void Open()
    {
        isOpen = true;
        savedTimeScale = Time.timeScale != 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        canvasObj.SetActive(true);
        RefreshSaveState();
    }

    private void Close()
    {
        isOpen = false;
        Time.timeScale = savedTimeScale != 0f ? savedTimeScale : 1f;
        canvasObj.SetActive(false);
    }

    private bool IsSaveAllowed()
    {
        if (GameManager.Instance == null) return false;
        if (GameManager.Instance.IsCoop) return false;
        return GameManager.Instance.CurrentPhase == GamePhase.Setup;
    }

    private void RefreshSaveState()
    {
        bool allowed = IsSaveAllowed();

        saveButton.interactable = allowed;
        Image saveImage = saveButton.GetComponent<Image>();
        if (saveImage != null) saveImage.color = allowed ? ButtonColor : ButtonDisabledColor;

        if (saveAndTitleButtonText != null)
        {
            saveAndTitleButtonText.text = allowed ? "SAVE & TITLE" : "TITLE";
        }

        if (saveHintText != null)
        {
            if (allowed)
            {
                saveHintText.text = "";
            }
            else if (GameManager.Instance != null && GameManager.Instance.IsCoop)
            {
                saveHintText.text = "Save is not available in CO-OP mode.";
            }
            else
            {
                saveHintText.text = "Save is available during the Setup phase.";
            }
        }
    }

    private void OnResumeClicked()
    {
        Close();
    }

    private void OnSaveClicked()
    {
        if (!IsSaveAllowed()) return;

        bool success = SaveSystem.Save(SaveSystem.CaptureCurrentState());
        ShowStatus(success ? "SAVED" : "SAVE FAILED");
    }

    private void OnSaveAndTitleClicked()
    {
        if (IsSaveAllowed())
        {
            SaveSystem.Save(SaveSystem.CaptureCurrentState());
        }

        Time.timeScale = savedTimeScale != 0f ? savedTimeScale : 1f;
        TutorialUI.SkipTutorial = false; // タイトルから改めて始める場合はチュートリアルを表示する
        GameManager.DestroyPersistentInstance();
        SceneManager.LoadScene(TitleSceneName);
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowStatus(string message)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusText.gameObject.SetActive(true);
        statusHideTimer = 1.5f;
    }

    private void CreateLayout()
    {
        canvasObj = new GameObject("PauseMenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // HUD(既定) < Tutorial(100) < AbilityLoadoutUI(200) < PauseMenu(250) < GameOver(300)
        canvas.sortingOrder = 250;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        GameObject overlayObj = new GameObject("Overlay");
        overlayObj.transform.SetParent(canvasObj.transform, false);
        Image overlayImage = overlayObj.AddComponent<Image>();
        overlayImage.color = OverlayColor;
        RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        TMP_Text titleText = CreateTextObject("PausedText", canvasObj.transform, new Vector2(0f, 230f), new Vector2(600f, 90f));
        titleText.text = "PAUSED";
        titleText.fontSize = 64;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = Color.white;

        statusText = CreateTextObject("StatusText", canvasObj.transform, new Vector2(0f, 160f), new Vector2(600f, 50f));
        statusText.fontSize = 28;
        statusText.fontStyle = FontStyles.Bold;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = new Color(0.4f, 1f, 0.5f);
        statusText.gameObject.SetActive(false);

        CreateButton(canvasObj.transform, "ResumeButton", "RESUME", new Vector2(0f, 80f), OnResumeClicked, out _);

        saveButton = CreateButton(canvasObj.transform, "SaveButton", "SAVE", new Vector2(0f, 0f), OnSaveClicked, out _);

        saveHintText = CreateTextObject("SaveHintText", canvasObj.transform, new Vector2(0f, -44f), new Vector2(500f, 40f));
        saveHintText.fontSize = 18;
        saveHintText.alignment = TextAlignmentOptions.Center;
        saveHintText.color = new Color(1f, 0.75f, 0.4f);

        saveAndTitleButton = CreateButton(canvasObj.transform, "SaveAndTitleButton", "SAVE & TITLE", new Vector2(0f, -100f), OnSaveAndTitleClicked, out saveAndTitleButtonText);

        CreateButton(canvasObj.transform, "QuitButton", "QUIT GAME", new Vector2(0f, -180f), OnQuitClicked, out _);

        canvasObj.SetActive(false);
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick, out TMP_Text buttonText)
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
        buttonRect.sizeDelta = ButtonSize;

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
        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = label;
        tmpText.fontSize = 30;
        tmpText.fontStyle = FontStyles.Bold;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = Color.white;
        tmpText.raycastTarget = false;
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        buttonText = tmpText;
        return button;
    }

    private TMP_Text CreateTextObject(string name, Transform parent, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmpText;
    }
}
