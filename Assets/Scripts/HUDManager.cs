using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDManager : MonoBehaviour
{
    // 共通カラー・サイズ定義
    private static readonly Color PanelBgColor = new Color(0.1f, 0.11f, 0.13f, 0.65f);
    private static readonly Color CardBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color LockedCardBgColor = new Color(0.2f, 0.25f, 0.3f, 0.35f);
    private static readonly Color LockedTextColor = new Color(1f, 1f, 1f, 0.4f);
    private static readonly Color IndicatorBgColor = new Color(0.12f, 0.16f, 0.12f, 0.85f);
    private static readonly Vector2 CardSize = new Vector2(180f, 44f);
    private static readonly Vector2 IndicatorSize = new Vector2(160f, 44f);

    // 獲得報酬インジケータの表示定義 (右端からこの順に詰めて並ぶ)
    // ラベル文字列はRewardTypeNames(RewardManager.cs)を参照し、Reward選択画面のタイトルと共通化する
    private static readonly RewardType[] RewardIndicatorDefs =
    {
        RewardType.IncreaseTowerDamage,
        RewardType.IncreaseTowerFireRate,
        RewardType.IncreaseTowerRange,
        RewardType.IncreaseTowerMaxHP,
        RewardType.IncreaseTowerArmor,
        RewardType.FrostAction,
        RewardType.PiercingShot,
        RewardType.CoreShield,
        RewardType.HealCore,
    };

    private static string RewardIndicatorLabel(RewardType type) =>
        RewardTypeNames.Get(type);

    private TMP_Text costText;
    private TMP_Text waveText;
    private TMP_Text phaseText;
    [SerializeField] private GameObject waveStartButton;
    private readonly Dictionary<RewardType, RectTransform> rewardIndicatorRects = new Dictionary<RewardType, RectTransform>();
    private readonly Dictionary<RewardType, TMP_Text> rewardIndicatorTexts = new Dictionary<RewardType, TMP_Text>();

    // Healerのロック/アンロック制御用
    private GameObject healerCardObj;
    private TMP_Text healerText;
    private CanvasGroup healerCanvasGroup;

    // Barricadeのロック/アンロック制御用
    private GameObject barricadeCardObj;
    private TMP_Text barricadeText;
    private CanvasGroup barricadeCanvasGroup;

    private void Start()
    {
        CreateHUDLayout();

        // 初期状態でHealerのロック状態を設定 (Wave 1として安全に初期化)
        UpdateHealerUnlockState(1);

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.OnBarricadeCountChanged += UpdateBarricadeCount;
            UpdateBarricadeCount(TowerManager.Instance.PlacedBarricadesInCurrentSetup);
        }

        if (waveStartButton == null)
        {
            waveStartButton = FindWaveStartButton();
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCostChanged += UpdateCost;
            GameManager.Instance.OnWaveNumberChanged += UpdateWave;
            GameManager.Instance.OnPhaseChanged += UpdatePhase;

            // 初期値を安全に反映
            UpdateCost(GameManager.Instance.Cost);
            UpdateWave(GameManager.Instance.CurrentWave);
            UpdatePhase(GameManager.Instance.CurrentPhase);
        }

        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated += UpdateRewardTexts;
            UpdateRewardTexts();
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCostChanged -= UpdateCost;
            GameManager.Instance.OnWaveNumberChanged -= UpdateWave;
            GameManager.Instance.OnPhaseChanged -= UpdatePhase;
        }
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated -= UpdateRewardTexts;
        }
        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.OnBarricadeCountChanged -= UpdateBarricadeCount;
        }
    }

    // StartDefensePhaseを呼び出すボタンを、イベント登録→既知の名前の順に探す
    private static GameObject FindWaveStartButton()
    {
        Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
        foreach (Button btn in buttons)
        {
            for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
            {
                if (btn.onClick.GetPersistentMethodName(i) == "StartDefensePhase")
                {
                    return btn.gameObject;
                }
            }
        }

        string[] possibleNames = { "StartButton", "WaveStartButton", "StartDefenseButton", "PlayButton" };
        foreach (string name in possibleNames)
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null)
            {
                return obj;
            }
        }

        return null;
    }

    private void UpdateCost(int cost)
    {
        if (costText != null)
        {
            int maxCost = GameManager.Instance != null ? GameManager.Instance.MaxCostForCurrentWave : 6;
            costText.text = $"COST: {cost}/{maxCost}";
        }
    }

    private void UpdateWave(int wave)
    {
        if (waveText != null)
        {
            waveText.text = $"WAVE: {wave}";
        }

        UpdateHealerUnlockState(wave);

        // Wave変更時は上限値も変わるため、COST表示の分母を最新化する
        if (GameManager.Instance != null)
        {
            UpdateCost(GameManager.Instance.Cost);
        }
    }

    // カードの有効/無効に応じた見た目とドラッグ可否をまとめて切り替える
    private void SetCardAvailability(GameObject cardObj, TMP_Text cardText, CanvasGroup canvasGroup, bool available, string label)
    {
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = available;
            canvasGroup.alpha = available ? 1.0f : 0.4f;
        }

        TowerDragHandler dragHandler = cardObj.GetComponent<TowerDragHandler>();
        if (dragHandler != null)
        {
            dragHandler.enabled = available;
        }

        Image cardImage = cardObj.GetComponent<Image>();
        if (cardImage != null)
        {
            cardImage.color = available ? CardBgColor : LockedCardBgColor;
        }

        cardText.text = label;
        cardText.color = available ? Color.white : LockedTextColor;
    }

    private void UpdateHealerUnlockState(int wave)
    {
        if (healerCardObj == null || healerText == null) return;

        bool isUnlocked = wave >= TowerManager.HealerUnlockWave;
        int hCost = TowerManager.Instance != null ? TowerManager.Instance.HealerCost : 2;
        SetCardAvailability(healerCardObj, healerText, healerCanvasGroup, isUnlocked,
            isUnlocked ? $"Healer (Cost: {hCost})" : $"Healer (Wave {TowerManager.HealerUnlockWave})");
    }

    private void UpdateBarricadeCount(int currentPlaced)
    {
        if (barricadeCardObj == null || barricadeText == null) return;

        int left = Mathf.Max(0, TowerManager.MaxBarricadesPerSetup - currentPlaced);
        SetCardAvailability(barricadeCardObj, barricadeText, barricadeCanvasGroup, left > 0,
            left > 0 ? $"Barricade ({left} Left)" : "Barricade (Max)");
    }

    private void CreateHUDLayout()
    {
        // 1. Canvasの動的作成
        GameObject canvasObj = new GameObject("HUDCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 2. 上下のバー（パネル）の追加
        GameObject panelObj = CreateBarPanel(canvasObj.transform, "HUDPanel", isTop: true);
        GameObject bottomPanelObj = CreateBarPanel(canvasObj.transform, "HUDBottomPanel", isTop: false);

        // 3. トップバーのテキスト作成とバインド
        waveText = CreateTextObject("WaveText", panelObj.transform, "WAVE: 1",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(300f, 50f));
        waveText.alignment = TextAlignmentOptions.Left;
        waveText.color = new Color(0.4f, 0.8f, 1f); // 水色

        phaseText = CreateTextObject("PhaseText", panelObj.transform, "PHASE: SETUP",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(380f, 0f), new Vector2(350f, 50f));
        phaseText.alignment = TextAlignmentOptions.Left;
        phaseText.color = new Color(0.4f, 0.8f, 1f); // 水色

        costText = CreateTextObject("CostText", panelObj.transform, "COST: 5/5",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(280f, 50f));
        costText.alignment = TextAlignmentOptions.Right;
        costText.color = new Color(0.4f, 1f, 0.8f); // シアン／ライトグリーン風味

        // 4. タワー配置・ヒーラー配置・バリケード配置用のカードを追加 (左側、ドラッグ＆ドロップ用)
        int tCost = TowerManager.Instance != null ? TowerManager.Instance.TowerCost : 2;
        int hCost = TowerManager.Instance != null ? TowerManager.Instance.HealerCost : 2;

        CreatePlacementCard(bottomPanelObj.transform, "TowerCard", 20f, $"Tower (Cost: {tCost})",
            TowerManager.PlacementType.Tower, addCanvasGroup: false, out _, out _);
        healerCardObj = CreatePlacementCard(bottomPanelObj.transform, "HealerCard", 220f, $"Healer (Cost: {hCost})",
            TowerManager.PlacementType.Healer, addCanvasGroup: true, out healerText, out healerCanvasGroup);
        barricadeCardObj = CreatePlacementCard(bottomPanelObj.transform, "BarricadeCard", 420f,
            $"Barricade ({TowerManager.MaxBarricadesPerSetup} Left)",
            TowerManager.PlacementType.Barricade, addCanvasGroup: true, out barricadeText, out barricadeCanvasGroup);

        // 5. 獲得した報酬情報のインディケータ表示 (右側)
        // 全種類を非表示で作成しておき、獲得数が1以上のものだけ UpdateRewardTexts で右詰め表示する
        foreach (var def in RewardIndicatorDefs)
        {
            TMP_Text text = CreateRewardIndicator(bottomPanelObj.transform,
                def + "Indicator", def + "CountText", $"{RewardIndicatorLabel(def)}: 0", -20f);
            RectTransform rect = text.transform.parent.GetComponent<RectTransform>();
            rect.gameObject.SetActive(false);
            rewardIndicatorRects[def] = rect;
            rewardIndicatorTexts[def] = text;
        }
    }

    // 画面上端/下端いっぱいに広がる高さ54px（1マス分）の半透明バーを作成する
    private GameObject CreateBarPanel(Transform parent, string name, bool isTop)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent, false);
        Image bgImage = panelObj.AddComponent<Image>();
        bgImage.color = PanelBgColor;

        float anchorY = isTop ? 1f : 0f;
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, anchorY);
        panelRect.anchorMax = new Vector2(1f, anchorY);
        panelRect.pivot = new Vector2(0.5f, anchorY);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(0f, 54f);
        return panelObj;
    }

    // ドラッグ＆ドロップ配置用のカードUIを作成する（左端基準のx位置指定）
    private GameObject CreatePlacementCard(Transform parent, string name, float x, string label,
        TowerManager.PlacementType type, bool addCanvasGroup, out TMP_Text cardText, out CanvasGroup canvasGroup)
    {
        GameObject cardObj = new GameObject(name);
        cardObj.transform.SetParent(parent, false);

        Image cardBg = cardObj.AddComponent<Image>();
        cardBg.color = CardBgColor;

        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 0.5f);
        cardRect.anchorMax = new Vector2(0f, 0.5f);
        cardRect.pivot = new Vector2(0f, 0.5f);
        cardRect.anchoredPosition = new Vector2(x, 0f);
        cardRect.sizeDelta = CardSize;

        cardText = CreateTextObject(name + "Text", cardObj.transform, label,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, CardSize);
        cardText.alignment = TextAlignmentOptions.Center;
        cardText.color = Color.white;
        cardText.fontSize = 16;

        TowerDragHandler dragHandler = cardObj.AddComponent<TowerDragHandler>();
        dragHandler.placementType = type;

        canvasGroup = addCanvasGroup ? cardObj.AddComponent<CanvasGroup>() : null;
        return cardObj;
    }

    // 獲得報酬カウント表示用のインディケータを作成する（右端基準のxオフセット指定）
    private TMP_Text CreateRewardIndicator(Transform parent, string name, string textName, string label, float xOffset)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image bg = obj.AddComponent<Image>();
        bg.color = IndicatorBgColor;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(xOffset, 0f);
        rect.sizeDelta = IndicatorSize;

        TMP_Text text = CreateTextObject(textName, obj.transform, label,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, IndicatorSize);
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSize = 16;
        return text;
    }

    private TMP_Text CreateTextObject(string name, Transform parent, string initText, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);

        TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
        tmpText.text = initText;
        tmpText.fontSize = 28; // 基準解像度1920x1080に合わせた最適サイズ
        tmpText.fontStyle = FontStyles.Bold;

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(anchorMin.x, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmpText;
    }

    private void UpdatePhase(GamePhase phase)
    {
        if (phaseText == null) return;

        if (waveStartButton != null)
        {
            waveStartButton.SetActive(phase == GamePhase.Setup);
        }

        switch (phase)
        {
            case GamePhase.Setup:
                phaseText.text = "PHASE: SETUP";
                phaseText.color = new Color(0.4f, 0.8f, 1f); // 水色系
                break;
            case GamePhase.Defense:
                phaseText.text = "PHASE: DEFENSE";
                phaseText.color = new Color(1f, 0.35f, 0.35f); // 赤系
                break;
            case GamePhase.Reward:
                phaseText.text = "PHASE: REWARD";
                phaseText.color = new Color(1f, 0.85f, 0.2f); // 黄色/金系
                break;
            case GamePhase.GameOver:
                phaseText.text = "PHASE: GAME OVER";
                phaseText.color = Color.gray;
                break;
        }
    }

    // 獲得数が1以上の報酬だけを、右端から左へ詰めて表示する
    private void UpdateRewardTexts()
    {
        if (RewardManager.Instance == null) return;
        var counts = RewardManager.Instance.GetAcquiredRewardCounts();

        float xOffset = -20f;
        foreach (var def in RewardIndicatorDefs)
        {
            if (!rewardIndicatorRects.TryGetValue(def, out RectTransform rect) || rect == null) continue;

            counts.TryGetValue(def, out int count);
            if (count <= 0)
            {
                rect.gameObject.SetActive(false);
                continue;
            }

            rect.gameObject.SetActive(true);
            rect.anchoredPosition = new Vector2(xOffset, 0f);
            rewardIndicatorTexts[def].text = $"{RewardIndicatorLabel(def)}: {count}";
            xOffset -= IndicatorSize.x + 20f;
        }
    }
}
