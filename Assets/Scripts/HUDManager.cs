using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class HUDManager : MonoBehaviour
{
    private TMP_Text lifeText;
    private TMP_Text costText;
    private TMP_Text waveText;
    private TMP_Text phaseText;
    [SerializeField] private GameObject waveStartButton;
    private TMP_Text repairCountText;
    private TMP_Text damageCountText;
    private TMP_Text speedCountText;
    private TMP_Text rangeCountText;
    private TMP_Text hpCountText;

    // Healerのロック/アンロック制御用
    private GameObject healerCardObj;
    private TMP_Text healerText;
    private CanvasGroup healerCanvasGroup;

    // Barricadeのロック/アンロック制御用
    private GameObject barricadeCardObj;
    private TMP_Text barricadeText;
    private CanvasGroup barricadeCanvasGroup;

    private TMP_Text armorCountText;

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
            Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
            foreach (Button btn in buttons)
            {
                for (int i = 0; i < btn.onClick.GetPersistentEventCount(); i++)
                {
                    string methodName = btn.onClick.GetPersistentMethodName(i);
                    if (methodName == "StartDefensePhase")
                    {
                        waveStartButton = btn.gameObject;
                        break;
                    }
                }
                if (waveStartButton != null) break;
            }

            if (waveStartButton == null)
            {
                string[] possibleNames = { "StartButton", "WaveStartButton", "StartDefenseButton", "PlayButton" };
                foreach (string name in possibleNames)
                {
                    GameObject obj = GameObject.Find(name);
                    if (obj != null)
                    {
                        waveStartButton = obj;
                        break;
                    }
                }
            }
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnLifeChanged += UpdateLife;
            GameManager.Instance.OnCostChanged += UpdateCost;
            GameManager.Instance.OnWaveNumberChanged += UpdateWave;
            GameManager.Instance.OnPhaseChanged += UpdatePhase;

            // 初期値を安全に反映
            UpdateLife(GameManager.Instance.Life);
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
            GameManager.Instance.OnLifeChanged -= UpdateLife;
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

    private void UpdateLife(int life)
    {
        if (lifeText != null)
        {
            lifeText.text = $"❤️ LIFE: {life}";
        }
    }

    private void UpdateCost(int cost)
    {
        if (costText != null)
        {
            costText.text = $"⚡ COST: {cost}/6";
        }
    }

    private void UpdateWave(int wave)
    {
        if (waveText != null)
        {
            waveText.text = $"🌊 WAVE: {wave}";
        }

        UpdateHealerUnlockState(wave);
    }

    private void UpdateHealerUnlockState(int wave)
    {
        if (healerCardObj == null || healerText == null) return;

        bool isUnlocked = wave >= 3;
        
        // CanvasGroupが存在する場合のみ設定
        if (healerCanvasGroup != null)
        {
            healerCanvasGroup.blocksRaycasts = isUnlocked;
            healerCanvasGroup.alpha = isUnlocked ? 1.0f : 0.4f;
        }

        TowerDragHandler dragHandler = healerCardObj.GetComponent<TowerDragHandler>();
        if (dragHandler != null)
        {
            dragHandler.enabled = isUnlocked;
        }

        // 直接ImageおよびTextのアルファ・カラーを変更
        Image cardImage = healerCardObj.GetComponent<Image>();
        int hCost = TowerManager.Instance != null ? TowerManager.Instance.HealerCost : 2;

        if (isUnlocked)
        {
            if (cardImage != null)
            {
                cardImage.color = new Color(0.2f, 0.25f, 0.3f, 0.9f); // 通常背景色
            }
            healerText.text = $"💚 Healer (Cost: {hCost})";
            healerText.color = Color.white; // 通常テキスト色
        }
        else
        {
            if (cardImage != null)
            {
                cardImage.color = new Color(0.2f, 0.25f, 0.3f, 0.35f); // 半透明+ダーク化
            }
            healerText.text = $"🔒 Healer (Wave 3)";
            healerText.color = new Color(1f, 1f, 1f, 0.4f); // 半透明白テキスト
        }
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

        // 2. トップバー（パネル）の追加
        GameObject panelObj = new GameObject("HUDPanel");
        panelObj.transform.SetParent(canvasObj.transform, false);
        Image bgImage = panelObj.AddComponent<Image>();
        // 半透明のグラデーションのある黒（上品なダークカラー）
        bgImage.color = new Color(0.1f, 0.11f, 0.13f, 0.65f);
        
        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, 0f);
        panelRect.sizeDelta = new Vector2(0f, 54f); // 高さ54pxのスタイリッシュなバー (1マス分)

        // 3. テキストの作成とバインド
        // 左側：Wave
        waveText = CreateTextObject("WaveText", panelObj.transform, "🌊 WAVE: 1", 
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(300f, 50f));
        waveText.alignment = TextAlignmentOptions.Left;
        waveText.color = new Color(0.4f, 0.8f, 1f); // 水色

        // Waveの右隣：Phase表示
        phaseText = CreateTextObject("PhaseText", panelObj.transform, "🔧 PHASE: SETUP", 
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(380f, 0f), new Vector2(350f, 50f));
        phaseText.alignment = TextAlignmentOptions.Left;
        phaseText.color = new Color(0.4f, 0.8f, 1f); // 水色

        // 右側（少し寄り）：Life (※Core周辺への表示移行に伴い非表示)
        // lifeText = CreateTextObject("LifeText", parent: panelObj.transform, "❤️ LIFE: 10", 
        //     new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-360f, 0f), new Vector2(250f, 50f));
        // if (lifeText != null)
        // {
        //     lifeText.alignment = TextAlignmentOptions.Right;
        //     lifeText.color = new Color(1f, 0.35f, 0.35f); // 薄赤
        // }

        // 右端：Cost
        costText = CreateTextObject("CostText", panelObj.transform, "⚡ COST: 5/5", 
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(280f, 50f));
        costText.alignment = TextAlignmentOptions.Right;
        costText.color = new Color(0.4f, 1f, 0.8f); // シアン／ライトグリーン風味

        // 4. ボトムバー（パネル）の追加
        GameObject bottomPanelObj = new GameObject("HUDBottomPanel");
        bottomPanelObj.transform.SetParent(canvasObj.transform, false);
        Image bottomBgImage = bottomPanelObj.AddComponent<Image>();
        bottomBgImage.color = new Color(0.1f, 0.11f, 0.13f, 0.65f);
        
        RectTransform bottomPanelRect = bottomPanelObj.GetComponent<RectTransform>();
        bottomPanelRect.anchorMin = new Vector2(0f, 0f);
        bottomPanelRect.anchorMax = new Vector2(1f, 0f);
        bottomPanelRect.pivot = new Vector2(0.5f, 0f);
        bottomPanelRect.anchoredPosition = new Vector2(0f, 0f);
        bottomPanelRect.sizeDelta = new Vector2(0f, 54f); // 高さ54pxのバー (1マス分)

        // 5. タワー配置・ヒーラー配置・バリケード配置用のカードを追加 (左側、ドラッグ＆ドロップ用)
        // タワーカード
        GameObject cardObj = new GameObject("TowerCard");
        cardObj.transform.SetParent(bottomPanelObj.transform, false);
        
        Image cardBg = cardObj.AddComponent<Image>();
        cardBg.color = new Color(0.2f, 0.25f, 0.3f, 0.9f);
        
        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 0.5f);
        cardRect.anchorMax = new Vector2(0f, 0.5f);
        cardRect.pivot = new Vector2(0f, 0.5f);
        cardRect.anchoredPosition = new Vector2(20f, 0f);
        cardRect.sizeDelta = new Vector2(180f, 44f);
        
        int tCost = TowerManager.Instance != null ? TowerManager.Instance.TowerCost : 2;
        TMP_Text cardText = CreateTextObject("CardText", cardObj.transform, $"🏹 Tower (Cost: {tCost})", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(180f, 44f));
        cardText.alignment = TextAlignmentOptions.Center;
        cardText.color = Color.white;
        cardText.fontSize = 16;

        TowerDragHandler towerDrag = cardObj.AddComponent<TowerDragHandler>();
        towerDrag.placementType = TowerManager.PlacementType.Tower;

        // ヒーラーカード
        healerCardObj = new GameObject("HealerCard");
        healerCardObj.transform.SetParent(bottomPanelObj.transform, false);
        
        Image healerBg = healerCardObj.AddComponent<Image>();
        healerBg.color = new Color(0.2f, 0.25f, 0.3f, 0.9f);
        
        RectTransform healerRect = healerCardObj.GetComponent<RectTransform>();
        healerRect.anchorMin = new Vector2(0f, 0.5f);
        healerRect.anchorMax = new Vector2(0f, 0.5f);
        healerRect.pivot = new Vector2(0f, 0.5f);
        healerRect.anchoredPosition = new Vector2(220f, 0f);
        healerRect.sizeDelta = new Vector2(180f, 44f);
        
        int hCost = TowerManager.Instance != null ? TowerManager.Instance.HealerCost : 2;
        healerText = CreateTextObject("HealerText", healerCardObj.transform, $"💚 Healer (Cost: {hCost})", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(180f, 44f));
        healerText.alignment = TextAlignmentOptions.Center;
        healerText.color = Color.white;
        healerText.fontSize = 16;

        TowerDragHandler healerDrag = healerCardObj.AddComponent<TowerDragHandler>();
        healerDrag.placementType = TowerManager.PlacementType.Healer;

        healerCanvasGroup = healerCardObj.AddComponent<CanvasGroup>();

        // バリケードカード
        barricadeCardObj = new GameObject("BarricadeCard");
        barricadeCardObj.transform.SetParent(bottomPanelObj.transform, false);
        
        Image barricadeBg = barricadeCardObj.AddComponent<Image>();
        barricadeBg.color = new Color(0.2f, 0.25f, 0.3f, 0.9f);
        
        RectTransform barricadeRect = barricadeCardObj.GetComponent<RectTransform>();
        barricadeRect.anchorMin = new Vector2(0f, 0.5f);
        barricadeRect.anchorMax = new Vector2(0f, 0.5f);
        barricadeRect.pivot = new Vector2(0f, 0.5f);
        barricadeRect.anchoredPosition = new Vector2(420f, 0f);
        barricadeRect.sizeDelta = new Vector2(180f, 44f);
        
        barricadeText = CreateTextObject("BarricadeText", barricadeCardObj.transform, "🧱 Barricade (3 Left)", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(180f, 44f));
        barricadeText.alignment = TextAlignmentOptions.Center;
        barricadeText.color = Color.white;
        barricadeText.fontSize = 16;

        TowerDragHandler barricadeDrag = barricadeCardObj.AddComponent<TowerDragHandler>();
        barricadeDrag.placementType = TowerManager.PlacementType.Barricade;

        barricadeCanvasGroup = barricadeCardObj.AddComponent<CanvasGroup>();

        // 6. 獲得した報酬情報のインディケータ表示 (右側)
        // Damage (ATK+)
        GameObject damageObj = new GameObject("DamageIndicator");
        damageObj.transform.SetParent(bottomPanelObj.transform, false);
        Image damageBg = damageObj.AddComponent<Image>();
        damageBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform damageRect = damageObj.GetComponent<RectTransform>();
        damageRect.anchorMin = new Vector2(1f, 0.5f);
        damageRect.anchorMax = new Vector2(1f, 0.5f);
        damageRect.pivot = new Vector2(1f, 0.5f);
        damageRect.anchoredPosition = new Vector2(-20f, 0f);
        damageRect.sizeDelta = new Vector2(160f, 44f);
        damageCountText = CreateTextObject("DamageCountText", damageObj.transform, "💥 ATK+: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        damageCountText.alignment = TextAlignmentOptions.Center;
        damageCountText.color = Color.white;
        damageCountText.fontSize = 16;

        // Speed (SPD+)
        GameObject speedObj = new GameObject("SpeedIndicator");
        speedObj.transform.SetParent(bottomPanelObj.transform, false);
        Image speedBg = speedObj.AddComponent<Image>();
        speedBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform speedRect = speedObj.GetComponent<RectTransform>();
        speedRect.anchorMin = new Vector2(1f, 0.5f);
        speedRect.anchorMax = new Vector2(1f, 0.5f);
        speedRect.pivot = new Vector2(1f, 0.5f);
        speedRect.anchoredPosition = new Vector2(-200f, 0f);
        speedRect.sizeDelta = new Vector2(160f, 44f);
        speedCountText = CreateTextObject("SpeedCountText", speedObj.transform, "⚡ SPD+: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        speedCountText.alignment = TextAlignmentOptions.Center;
        speedCountText.color = Color.white;
        speedCountText.fontSize = 16;

        // Range (RNG+)
        GameObject rangeIndicatorObj = new GameObject("RangeIndicator");
        rangeIndicatorObj.transform.SetParent(bottomPanelObj.transform, false);
        Image rangeBg = rangeIndicatorObj.AddComponent<Image>();
        rangeBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform rangeIndicatorRect = rangeIndicatorObj.GetComponent<RectTransform>();
        rangeIndicatorRect.anchorMin = new Vector2(1f, 0.5f);
        rangeIndicatorRect.anchorMax = new Vector2(1f, 0.5f);
        rangeIndicatorRect.pivot = new Vector2(1f, 0.5f);
        rangeIndicatorRect.anchoredPosition = new Vector2(-380f, 0f);
        rangeIndicatorRect.sizeDelta = new Vector2(160f, 44f);
        rangeCountText = CreateTextObject("RangeCountText", rangeIndicatorObj.transform, "🔍 RNG+: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        rangeCountText.alignment = TextAlignmentOptions.Center;
        rangeCountText.color = Color.white;
        rangeCountText.fontSize = 16;

        // HP (HP+)
        GameObject hpObj = new GameObject("HPIndicator");
        hpObj.transform.SetParent(bottomPanelObj.transform, false);
        Image hpBg = hpObj.AddComponent<Image>();
        hpBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform hpRect = hpObj.GetComponent<RectTransform>();
        hpRect.anchorMin = new Vector2(1f, 0.5f);
        hpRect.anchorMax = new Vector2(1f, 0.5f);
        hpRect.pivot = new Vector2(1f, 0.5f);
        hpRect.anchoredPosition = new Vector2(-560f, 0f);
        hpRect.sizeDelta = new Vector2(160f, 44f);
        hpCountText = CreateTextObject("HPCountText", hpObj.transform, "❤️ HP+: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        hpCountText.alignment = TextAlignmentOptions.Center;
        hpCountText.color = Color.white;
        hpCountText.fontSize = 16;

        // Armor (ARM+)
        GameObject armorObj = new GameObject("ArmorIndicator");
        armorObj.transform.SetParent(bottomPanelObj.transform, false);
        Image armorBg = armorObj.AddComponent<Image>();
        armorBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform armorRect = armorObj.GetComponent<RectTransform>();
        armorRect.anchorMin = new Vector2(1f, 0.5f);
        armorRect.anchorMax = new Vector2(1f, 0.5f);
        armorRect.pivot = new Vector2(1f, 0.5f);
        armorRect.anchoredPosition = new Vector2(-740f, 0f);
        armorRect.sizeDelta = new Vector2(160f, 44f);
        armorCountText = CreateTextObject("ArmorCountText", armorObj.transform, "🛡️ ARM+: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        armorCountText.alignment = TextAlignmentOptions.Center;
        armorCountText.color = Color.white;
        armorCountText.fontSize = 16;

        // Repair
        GameObject repairObj = new GameObject("RepairIndicator");
        repairObj.transform.SetParent(bottomPanelObj.transform, false);
        Image repairBg = repairObj.AddComponent<Image>();
        repairBg.color = new Color(0.12f, 0.16f, 0.12f, 0.85f);
        RectTransform repairRect = repairObj.GetComponent<RectTransform>();
        repairRect.anchorMin = new Vector2(1f, 0.5f);
        repairRect.anchorMax = new Vector2(1f, 0.5f);
        repairRect.pivot = new Vector2(1f, 0.5f);
        repairRect.anchoredPosition = new Vector2(-920f, 0f);
        repairRect.sizeDelta = new Vector2(160f, 44f);
        repairCountText = CreateTextObject("RepairCountText", repairObj.transform, "🔧 Repair: 0", 
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(160f, 44f));
        repairCountText.alignment = TextAlignmentOptions.Center;
        repairCountText.color = Color.white;
        repairCountText.fontSize = 16;
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
                phaseText.text = "🔧 PHASE: SETUP";
                phaseText.color = new Color(0.4f, 0.8f, 1f); // 水色系
                break;
            case GamePhase.Defense:
                phaseText.text = "⚔️ PHASE: DEFENSE";
                phaseText.color = new Color(1f, 0.35f, 0.35f); // 赤系
                break;
            case GamePhase.Reward:
                phaseText.text = "🎁 PHASE: REWARD";
                phaseText.color = new Color(1f, 0.85f, 0.2f); // 黄色/金系
                break;
            case GamePhase.GameOver:
                phaseText.text = "💀 PHASE: GAME OVER";
                phaseText.color = Color.gray;
                break;
        }
    }

    private void UpdateRewardTexts()
    {
        if (RewardManager.Instance == null) return;
        var counts = RewardManager.Instance.GetAcquiredRewardCounts();
        
        if (repairCountText != null && counts.TryGetValue(RewardType.HealCore, out int healCount))
        {
            repairCountText.text = $"🔧 Repair: {healCount}";
        }
        if (damageCountText != null && counts.TryGetValue(RewardType.IncreaseTowerDamage, out int dmgCount))
        {
            damageCountText.text = $"💥 ATK+: {dmgCount}";
        }
        if (speedCountText != null && counts.TryGetValue(RewardType.IncreaseTowerFireRate, out int speedCount))
        {
            speedCountText.text = $"⚡ SPD+: {speedCount}";
        }
        if (rangeCountText != null && counts.TryGetValue(RewardType.IncreaseTowerRange, out int rangeCount))
        {
            rangeCountText.text = $"🔍 RNG+: {rangeCount}";
        }
        if (hpCountText != null && counts.TryGetValue(RewardType.IncreaseTowerMaxHP, out int hpCount))
        {
            hpCountText.text = $"❤️ HP+: {hpCount}";
        }
        if (armorCountText != null && counts.TryGetValue(RewardType.IncreaseTowerArmor, out int armorCount))
        {
            armorCountText.text = $"🛡️ ARM+: {armorCount}";
        }
    }

    private void UpdateBarricadeCount(int currentPlaced)
    {
        if (barricadeCardObj == null || barricadeText == null) return;

        int left = Mathf.Max(0, 3 - currentPlaced);
        bool hasBarricadesLeft = left > 0;

        if (barricadeCanvasGroup != null)
        {
            barricadeCanvasGroup.blocksRaycasts = hasBarricadesLeft;
            barricadeCanvasGroup.alpha = hasBarricadesLeft ? 1.0f : 0.4f;
        }

        TowerDragHandler dragHandler = barricadeCardObj.GetComponent<TowerDragHandler>();
        if (dragHandler != null)
        {
            dragHandler.enabled = hasBarricadesLeft;
        }

        Image cardImage = barricadeCardObj.GetComponent<Image>();
        if (hasBarricadesLeft)
        {
            if (cardImage != null)
            {
                cardImage.color = new Color(0.2f, 0.25f, 0.3f, 0.9f);
            }
            barricadeText.text = $"🧱 Barricade ({left} Left)";
            barricadeText.color = Color.white;
        }
        else
        {
            if (cardImage != null)
            {
                cardImage.color = new Color(0.2f, 0.25f, 0.3f, 0.35f);
            }
            barricadeText.text = $"🧱 Barricade (Max)";
            barricadeText.color = new Color(1f, 1f, 1f, 0.4f);
        }
    }
}
