using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 画面右下に倍速・一時停止ボタンを表示し、Time.timeScaleを制御する。
// GameOverUI/TutorialUIがtimeScaleを0に固定して表示している間は、操作不可にして競合を避ける。
public class GameSpeedController : MonoBehaviour
{
    private static readonly float[] SpeedSteps = { 1f, 1.2f, 1.5f, 2f, 3f };

    private static readonly Color ButtonBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color ButtonHoverColor = new Color(0.28f, 0.35f, 0.42f, 0.9f);
    private static readonly Color ButtonActiveColor = new Color(0.25f, 0.45f, 0.65f, 1f);
    private static readonly Vector2 ButtonSize = new Vector2(90f, 54f);

    private int speedIndex = 0;
    private bool isPaused = false;

    private Button speedButton;
    private TMP_Text speedButtonText;
    private Button pauseButton;
    private TMP_Text pauseButtonText;

    private void Start()
    {
        CreateLayout();

        // TutorialUI/GameOverUIが先にStart()でTime.timeScaleを0にしている場合は上書きしない。
        if (speedButtonText != null)
        {
            speedButtonText.text = $"x{FormatSpeed(SpeedSteps[speedIndex])}";
        }
        if (Time.timeScale != 0f)
        {
            ApplyTimeScale();
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
        }
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.GameOver)
        {
            SetInteractable(false);
        }
    }

    // GameOverUI/TutorialUIがTime.timeScaleを0に固定して表示している間はボタンを操作不可にする。
    private void Update()
    {
        bool blocked = IsBlockedByOtherUI();
        SetInteractable(!blocked);
    }

    // GameOverUI/TutorialUIはTime.timeScaleを直接0にして表示するため、
    // 自分がPause中でないのにtimeScaleが0になっている場合は他UIに制御されていると判断する。
    private bool IsBlockedByOtherUI()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.GameOver)
        {
            return true;
        }
        if (!isPaused && Time.timeScale == 0f)
        {
            return true;
        }
        return false;
    }

    private void SetInteractable(bool interactable)
    {
        if (speedButton != null) speedButton.interactable = interactable;
        if (pauseButton != null) pauseButton.interactable = interactable;
    }

    private void OnSpeedButtonClicked()
    {
        speedIndex = (speedIndex + 1) % SpeedSteps.Length;
        ApplyTimeScale();
    }

    private void OnPauseButtonClicked()
    {
        isPaused = !isPaused;
        ApplyTimeScale();
        pauseButtonText.text = isPaused ? "PLAY" : "PAUSE";

        Image pauseImage = pauseButton.GetComponent<Image>();
        ColorBlock colors = pauseButton.colors;
        colors.normalColor = isPaused ? ButtonActiveColor : ButtonBgColor;
        pauseButton.colors = colors;
        pauseImage.color = colors.normalColor;
    }

    private void ApplyTimeScale()
    {
        Time.timeScale = isPaused ? 0f : SpeedSteps[speedIndex];
        if (speedButtonText != null)
        {
            speedButtonText.text = $"x{FormatSpeed(SpeedSteps[speedIndex])}";
        }
    }

    private static string FormatSpeed(float speed)
    {
        return speed == Mathf.Floor(speed) ? speed.ToString("0") : speed.ToString("0.#");
    }

    private void CreateLayout()
    {
        GameObject canvasObj = new GameObject("GameSpeedCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 左上、トップバー(高さ54px)のすぐ下に配置し、Wave StartボタンやWAVE/PHASEテキスト、
        // 下部のカード・Reward表示のいずれとも重ならないようにする。
        float topY = -54f - 16f;
        pauseButton = CreateButton(canvasObj.transform, "PauseButton", "PAUSE",
            new Vector2(20f, topY), OnPauseButtonClicked, out pauseButtonText);
        speedButton = CreateButton(canvasObj.transform, "SpeedButton", "x1",
            new Vector2(20f + ButtonSize.x + 10f, topY), OnSpeedButtonClicked, out speedButtonText);
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick, out TMP_Text buttonText)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = ButtonBgColor;

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0f, 1f);
        buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = anchoredPosition;
        buttonRect.sizeDelta = ButtonSize;

        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonBgColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonHoverColor;
        colors.disabledColor = new Color(ButtonBgColor.r, ButtonBgColor.g, ButtonBgColor.b, 0.35f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        buttonText = CreateTextObject(name + "Text", buttonObj.transform, label);

        return button;
    }

    private TMP_Text CreateTextObject(string name, Transform parent, string label)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = label;
        tmpText.fontSize = 22;
        tmpText.fontStyle = FontStyles.Bold;
        tmpText.alignment = TextAlignmentOptions.Center;
        tmpText.color = Color.white;
        tmpText.raycastTarget = false;

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return tmpText;
    }
}
