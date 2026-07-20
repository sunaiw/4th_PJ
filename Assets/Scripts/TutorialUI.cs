using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Wave1のSetupフェーズ開始からWave2のSetupフェーズ開始までの間だけ表示されるチュートリアル。
// フェーズ切り替えごとにページ群を差し替え、ページ内は「次へ」ボタンで送る。
public class TutorialUI : MonoBehaviour
{
    // Retry時などシーンをまたいでチュートリアルを表示させたくない場合に、遷移前にtrueへセットする
    public static bool SkipTutorial = false;

    private static readonly Color PanelBgColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);
    private static readonly Color ButtonBgColor = new Color(0.25f, 0.45f, 0.65f, 1f);
    private static readonly Vector2 PanelSize = new Vector2(760f, 260f);

    private GameObject blockerObj;
    private GameObject panelObj;
    private TMP_Text bodyText;
    private TMP_Text pageIndicatorText;
    private GameObject nextButtonObj;
    private TMP_Text nextButtonText;
    private float savedTimeScale = 1f;

    private readonly Dictionary<GamePhase, string[]> pagesByPhase = new Dictionary<GamePhase, string[]>();
    private string[] wave2SetupPages;
    private string[] currentPages;
    private int currentPageIndex;
    private bool isFinalPageSet;
    private bool tutorialFinished;

    private void Start()
    {
        BuildPageContents();
        CreateLayout();

        if (SkipTutorial)
        {
            SkipTutorial = false;
            tutorialFinished = true;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            GameManager.Instance.OnWaveNumberChanged += HandleWaveNumberChanged;

            if (!tutorialFinished && GameManager.Instance.CurrentWave == 1)
            {
                ShowPages(pagesByPhase.TryGetValue(GameManager.Instance.CurrentPhase, out string[] pages) ? pages : null);
            }
            else
            {
                HidePanel();
            }
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            GameManager.Instance.OnWaveNumberChanged -= HandleWaveNumberChanged;
        }

        if (panelObj != null && panelObj.activeSelf)
        {
            Time.timeScale = savedTimeScale;
        }
    }

    private void HandlePhaseChanged(GamePhase phase)
    {
        if (tutorialFinished) return;

        if (phase == GamePhase.GameOver)
        {
            tutorialFinished = true;
            HidePanel();
            return;
        }

        // OnWaveNumberChanged(wave=2)がOnPhaseChanged(Setup)より先に発火するため、
        // Wave2のSetup開始時はここではなくHandleWaveNumberChanged側でページを出す。
        if (isFinalPageSet) return;
        if (GameManager.Instance == null || GameManager.Instance.CurrentWave != 1) return;

        ShowPages(pagesByPhase.TryGetValue(phase, out string[] pages) ? pages : null);
    }

    private void HandleWaveNumberChanged(int wave)
    {
        if (tutorialFinished) return;

        // Wave2のSetup開始直前に発火する。最終ページ（獲得リワード説明）を表示してから閉じる。
        if (wave == 2)
        {
            isFinalPageSet = true;
            ShowPages(wave2SetupPages);
        }
    }

    private void ShowPages(string[] pages)
    {
        if (pages == null || pages.Length == 0)
        {
            HidePanel();
            return;
        }

        currentPages = pages;
        currentPageIndex = 0;

        bool wasActive = panelObj.activeSelf;
        panelObj.SetActive(true);
        blockerObj.SetActive(true);
        if (!wasActive)
        {
            savedTimeScale = Time.timeScale != 0f ? Time.timeScale : savedTimeScale;
            Time.timeScale = 0f;
        }
        RefreshPageText();
    }

    private void RefreshPageText()
    {
        if (currentPages == null) return;

        bodyText.text = currentPages[currentPageIndex];
        pageIndicatorText.text = $"{currentPageIndex + 1} / {currentPages.Length}";

        bool isLastPage = currentPageIndex >= currentPages.Length - 1;
        nextButtonText.text = (isFinalPageSet && isLastPage) ? "Close" : "Next";
    }

    private void OnNextButtonClicked()
    {
        if (currentPages == null) return;

        if (currentPageIndex < currentPages.Length - 1)
        {
            currentPageIndex++;
            RefreshPageText();
            return;
        }

        if (isFinalPageSet)
        {
            tutorialFinished = true;
        }
        HidePanel();
    }

    private void HidePanel()
    {
        if (panelObj != null)
        {
            bool wasActive = panelObj.activeSelf;
            panelObj.SetActive(false);
            if (wasActive)
            {
                Time.timeScale = savedTimeScale;
            }
        }

        if (blockerObj != null)
        {
            blockerObj.SetActive(false);
        }
    }

    private void BuildPageContents()
    {
        pagesByPhase[GamePhase.Setup] = new[]
        {
            "The top bar shows WAVE (current wave number), PHASE (current phase), and COST (points available for placing towers).\nThe bottom bar shows cards for placing towers, barricades, and more.",
            "Placing a Tower: Drag a card from the bottom bar and drop it on the map. You can't place one where cost is insufficient or where it would block the enemy path.\nRemoving a Tower: During the Setup phase, right-click a tower to remove it and refund its cost (only towers placed in the current wave).",
            "Hover over a tower to see its attack range as a circle. Left-click a tower to toggle its range display on/off permanently.",
            "Barricades don't attack, but block the enemy's path instead. They can't be destroyed by normal attacks, and there's a limit to how many you can place per Setup phase.",
            "The Core at the center of the map is your base — if its HP reaches 0, it's game over. The EnemySpawner is where enemies appear and begin marching toward the Core.",
            "Once you're ready, press the button on screen to start the wave.",
        };

        pagesByPhase[GamePhase.Defense] = new[]
        {
            "The Defense phase has begun. Defeat all enemies (or wait until no more spawn and none remain) to clear the wave. Watch out — if the Core's life reaches 0, it's game over.",
            "Hover over a Tower or Enemy to see its HP displayed above it. Use this to track the battle.",
        };

        pagesByPhase[GamePhase.Reward] = new[]
        {
            "After clearing a wave, choose 1 of 3 rewards. The same reward type can be picked multiple times, stacking its effect further. Rewards you've acquired are shown in the bottom-right of the screen with their count.",
        };

        // Wave2のSetupフェーズ開始時に表示する最終ページ（HandleWaveNumberChanged経由でのみ参照）
        wave2SetupPages = new[]
        {
            "Rewards you acquired in the previous wave keep their effect for the rest of the game. Check the bottom-right indicators anytime to see which rewards you have and how many.",
        };
    }

    private void CreateLayout()
    {
        GameObject canvasObj = new GameObject("TutorialCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // HUDより手前に表示

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        blockerObj = new GameObject("TutorialInputBlocker");
        blockerObj.transform.SetParent(canvasObj.transform, false);
        Image blockerImage = blockerObj.AddComponent<Image>();
        blockerImage.color = new Color(0f, 0f, 0f, 0f); // 透明だがRaycastは受け止め、背後のUI/ワールド操作を封じる
        RectTransform blockerRect = blockerObj.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;
        blockerObj.SetActive(false);

        panelObj = new GameObject("TutorialPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        Image panelBg = panelObj.AddComponent<Image>();
        panelBg.color = PanelBgColor;

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 90f);
        panelRect.sizeDelta = PanelSize;

        bodyText = CreateTextObject("TutorialBodyText", panelObj.transform,
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(-40f, 170f));
        bodyText.alignment = TextAlignmentOptions.TopLeft;
        bodyText.fontSize = 24;
        bodyText.fontStyle = FontStyles.Normal;
        bodyText.color = Color.white;

        pageIndicatorText = CreateTextObject("TutorialPageIndicator", panelObj.transform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(30f, 20f), new Vector2(120f, 30f));
        pageIndicatorText.alignment = TextAlignmentOptions.Left;
        pageIndicatorText.fontSize = 18;
        pageIndicatorText.color = new Color(0.8f, 0.8f, 0.8f);

        nextButtonObj = new GameObject("TutorialNextButton");
        nextButtonObj.transform.SetParent(panelObj.transform, false);
        Image btnBg = nextButtonObj.AddComponent<Image>();
        btnBg.color = ButtonBgColor;
        RectTransform btnRect = nextButtonObj.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(1f, 0f);
        btnRect.anchorMax = new Vector2(1f, 0f);
        btnRect.pivot = new Vector2(1f, 0f);
        btnRect.anchoredPosition = new Vector2(-30f, 20f);
        btnRect.sizeDelta = new Vector2(140f, 44f);

        Button nextButton = nextButtonObj.AddComponent<Button>();
        nextButton.onClick.AddListener(OnNextButtonClicked);

        nextButtonText = CreateTextObject("TutorialNextButtonText", nextButtonObj.transform,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        nextButtonText.alignment = TextAlignmentOptions.Center;
        nextButtonText.text = "Next";
        nextButtonText.fontSize = 20;
        nextButtonText.color = Color.white;

        panelObj.SetActive(false);
    }

    private TMP_Text CreateTextObject(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmpText;
    }
}
