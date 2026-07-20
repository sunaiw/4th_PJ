using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// GameOver時に画面中央へ大きく表示するUI。表示と同時にTime.timeScaleを0にしてEnemy等の進行を止める。
public class GameOverUI : MonoBehaviour
{
    private const string TitleSceneName = "TitleScene";

    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color ButtonColor = new Color(0.25f, 0.45f, 0.65f, 1f);
    private static readonly Color ButtonHoverColor = new Color(0.32f, 0.55f, 0.78f, 1f);

    private GameObject canvasObj;
    private float savedTimeScale = 1f;

    private void Start()
    {
        CreateLayout();

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
            Show();
        }
    }

    private void Show()
    {
        savedTimeScale = Time.timeScale != 0f ? Time.timeScale : savedTimeScale;
        Time.timeScale = 0f;
        canvasObj.SetActive(true);
    }

    // GameManagerはシーンをまたいで永続化されるため、再戦・タイトル遷移前に明示的に破棄し、
    // 遷移先で新しいGameManagerが正常に初期化されるようにする。
    private void ResetPersistentGameManager()
    {
        Time.timeScale = savedTimeScale != 0f ? savedTimeScale : 1f;
        if (GameManager.Instance != null)
        {
            DestroyImmediate(GameManager.Instance.gameObject);
        }
    }

    private void OnRetryClicked()
    {
        string currentScene = SceneManager.GetActiveScene().name;
        ResetPersistentGameManager();
        SceneManager.LoadScene(currentScene);
    }

    private void OnTitleClicked()
    {
        ResetPersistentGameManager();
        SceneManager.LoadScene(TitleSceneName);
    }

    private void CreateLayout()
    {
        canvasObj = new GameObject("GameOverCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300; // HUD(既定)やTutorial(100)より必ず手前に表示

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

        TMP_Text titleText = CreateTextObject("GameOverText", canvasObj.transform,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1200f, 160f));
        titleText.text = "GAME OVER";
        titleText.fontSize = 110;
        titleText.fontStyle = FontStyles.Bold;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(1f, 0.3f, 0.3f);

        CreateButton(canvasObj.transform, "RetryButton", "RETRY", new Vector2(-180f, -100f), OnRetryClicked);
        CreateButton(canvasObj.transform, "TitleButton", "TITLE", new Vector2(180f, -100f), OnTitleClicked);

        canvasObj.SetActive(false);
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
        buttonRect.sizeDelta = new Vector2(280f, 80f);

        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonHoverColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        TMP_Text buttonText = CreateTextObject(name + "Text", buttonObj.transform,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        buttonText.text = label;
        buttonText.fontSize = 32;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;

        return buttonObj;
    }

    private TMP_Text CreateTextObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmpText;
    }
}
