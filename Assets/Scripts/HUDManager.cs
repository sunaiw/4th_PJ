using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HUDManager : MonoBehaviour
{
    // Step 4-1: GameManager.Startからgameobject.AddComponent<HUDManager>()で生成されるため
    // SingletonBehaviour<T>は使わず、最小限の静的参照だけを持たせる（Tower.cs等からShowToast()を呼ぶため）
    public static HUDManager Instance { get; private set; }

    // 画面上下のHUDバーの高さ（CanvasScalerの基準解像度1920x1080におけるpx）
    public const float BarHeight = 54f;

    // 画面上端から続くHUD行の合計高さ。CO-OP時のみCoopResourceBar(42px)が1行増えるため、
    // その下に置く要素(PAUSE/倍速ボタン・Wave Startボタン・トースト)は必ずこの値を基準に配置する
    public static float TopStackHeight =>
        BarHeight + ((GameManager.Instance != null && GameManager.Instance.IsCoop) ? CoopBarHeight : 0f);

    // 共通カラー・サイズ定義
    private static readonly Color PanelBgColor = new Color(0.1f, 0.11f, 0.13f, 0.65f);
    private static readonly Color CardBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color LockedCardBgColor = new Color(0.2f, 0.25f, 0.3f, 0.35f);
    private static readonly Color LockedTextColor = new Color(1f, 1f, 1f, 0.4f);
    private static readonly Color IndicatorBgColor = new Color(0.12f, 0.16f, 0.12f, 0.85f);
    private static readonly Vector2 CardSize = new Vector2(140f, 44f);
    private const float CardGap = 10f;
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

    // GameSpeedControllerの一時停止/倍速ボタンと色味を揃える
    private static readonly Color WaveStartButtonBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color WaveStartButtonHoverColor = new Color(0.28f, 0.35f, 0.42f, 0.9f);
    private static readonly Vector2 WaveStartButtonSize = new Vector2(180f, 54f);

    // Step 2: 配置拒否時のトースト表示
    private static readonly Color ToastBgColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Vector2 ToastSize = new Vector2(460f, 40f);
    private const float ToastDuration = 1.6f;

    private TMP_Text costText;
    private TMP_Text waveText;
    private TMP_Text phaseText;
    private GameObject waveStartButton;
    private GameObject toastObj;
    private TMP_Text toastText;
    private float toastHideTime = -1f;

    // Step 4-1: CO-OP時のみ表示する、現在操作中プレイヤーのインジケータ
    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);
    private GameObject activeOwnerIndicatorObj;
    private TMP_Text activeOwnerIndicatorText;

    // Step 4-2 Rescue: Outpost破壊時に画面中央上部へ表示する警告バナー（CO-OP時のみ）。
    // Tower.coopOfflineGraceDurationの既定値(5.0秒)と表示上揃えている
    private const float OutpostWarningDuration = 5.0f;
    private GameObject outpostWarningObj;
    private TMP_Text outpostWarningText;
    private float outpostWarningTimer = -1f;
    private int outpostWarningOwnerId = 0;

    // Step 4-5: Siege Marker出現予告バナー（CO-OP時のみ）。OutpostWarningBanner(破壊警告)と同時に
    // 表示され得るため、別オブジェクト・別Y座標で用意する。数秒で自動的に消える（追跡マーカー自体は
    // Tower.SetSiegeMarked()側でSiege Markerが倒されるか対象が破壊されるまで維持される）
    private const float SiegeWarningDuration = 4.0f;
    private GameObject siegeWarningObj;
    private TMP_Text siegeWarningText;
    private float siegeWarningTimer = -1f;
    private readonly Dictionary<RewardType, RectTransform> rewardIndicatorRects = new Dictionary<RewardType, RectTransform>();
    private readonly Dictionary<RewardType, TMP_Text> rewardIndicatorTexts = new Dictionary<RewardType, TMP_Text>();

    // Step 4-3: 二層コスト(Personal Cost / Union Power)とTransfer残数を表示する専用バー。
    // トップバーのすぐ下に配置し、Step 4-1のPLAYER 1 (BLUE)インジケータ(トップバー中央)と競合しないようにする。
    // CO-OP時のみ表示し、シングルプレイでは非表示のまま（既存のCostTextをそのまま表示し続ける）
    public const float CoopBarHeight = 42f;
    private static readonly Color UnionGaugeFillColor = new Color(0.65f, 0.4f, 0.9f, 1f);
    private static readonly Color UnionGaugeBgColor = new Color(0.15f, 0.15f, 0.18f, 0.9f);
    // 配置カードのUnion系種別を区別する枠色（Union Gaugeと色を揃える）
    private static readonly Color UnionCardFrameColor = new Color(0.65f, 0.4f, 0.9f, 1f);
    private GameObject coopResourceBarObj;
    private TMP_Text ownCostText;
    private TMP_Text allyCostText;
    private TMP_Text unionGaugeText;
    private Image unionGaugeFillImage;
    private TMP_Text transferText;

    // Step 4-3: Union Power承認バナー（画面中央上部）。Pendingは常にSetupフェーズ中にしか存在せず、
    // Outpost破壊警告/Siege警告はDefenseフェーズ中にしか発生しないため、時間的に排他的な同じ位置を再利用する
    private GameObject unionApprovalBannerObj;
    private TMP_Text unionApprovalText;

    // 配置カードを種別ごとに保持し、解禁状況・残り設置数に応じて一括更新する
    private readonly Dictionary<TowerType, GameObject> placementCardObjs = new Dictionary<TowerType, GameObject>();
    private readonly Dictionary<TowerType, TMP_Text> placementCardTexts = new Dictionary<TowerType, TMP_Text>();
    private readonly Dictionary<TowerType, CanvasGroup> placementCardGroups = new Dictionary<TowerType, CanvasGroup>();

    // Step 4-4: Operator Ability バー（画面中央右、既存要素と重ならない空きスペースに配置）。
    // CO-OP時かつDefenseフェーズ中のみ表示する（アビリティ自体がDefense限定のため、非表示にして
    // 使えない期間にUIが場所を占有しないようにする）
    private const float AbilityBarWidth = 280f;
    private const float AbilitySlotHeight = 40f;
    private const float AbilitySlotGap = 8f;
    private static readonly Color AbilityGaugeFillColor = new Color(0.35f, 0.85f, 0.55f, 1f);
    private static readonly Color AbilityGaugeBgColor = new Color(0.15f, 0.15f, 0.18f, 0.9f);
    private static readonly Color AbilityGaugeGlowColor = new Color(1f, 0.85f, 0.3f, 0.9f);
    private static readonly Color ComboReadyColor = new Color(1f, 0.9f, 0.3f, 1f);
    private GameObject abilityBarObj;
    private readonly Image[] abilitySlotFillImages = new Image[2];
    private readonly Image[] abilitySlotBgImages = new Image[2];
    private readonly TMP_Text[] abilitySlotTexts = new TMP_Text[2];
    private GameObject comboReadyObj;
    private TMP_Text comboReadyText;

    // Step 4-4: Sync Combo成立時の大きな画面表示（Combo名。成功体験の可視化が最重要のため画面中央に表示する）
    private const float ComboBannerDuration = 2.5f;
    private GameObject comboBannerObj;
    private TMP_Text comboBannerText;
    private float comboBannerTimer = -1f;

    private void Start()
    {
        Instance = this;

        CreateHUDLayout();

        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.OnPlacedCountChanged += HandlePlacedCountChanged;
            // Step 2: 配置拒否理由をトーストとして表示する
            TowerManager.Instance.OnPlacementRejected += ShowToast;
            // Step 4-3: Pendingの開始/終了で全カードの活性/非活性を再評価する
            TowerManager.Instance.OnUnionPendingStateChanged += HandleUnionPendingStateChanged;
            UpdateAllCardStates();
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCostChanged += UpdateCost;
            GameManager.Instance.OnWaveNumberChanged += UpdateWave;
            GameManager.Instance.OnPhaseChanged += UpdatePhase;
            GameManager.Instance.OnActiveOwnerChanged += UpdateActiveOwnerIndicator;
            // Step 4-3
            GameManager.Instance.OnPersonalCostChanged += HandlePersonalCostChanged;
            GameManager.Instance.OnUnionPowerChanged += HandleUnionPowerChanged;
            GameManager.Instance.OnTransferRemainingChanged += UpdateTransferText;

            // 初期値を安全に反映
            UpdateCost(GameManager.Instance.Cost);
            UpdateWave(GameManager.Instance.CurrentWave);
            UpdatePhase(GameManager.Instance.CurrentPhase);
            UpdateActiveOwnerIndicator(GameManager.Instance.ActiveOwnerId);
            RefreshCoopResourceBar();
        }

        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated += UpdateRewardTexts;
            UpdateRewardTexts();
        }

        // Step 4-4: OperatorAbilityManagerはGameManager.Start()内でHUDManagerより後にAddComponent()されるが、
        // Instanceの確定はAwake()側で行っているため、この時点で既にInstanceは非nullになっている
        if (OperatorAbilityManager.Instance != null)
        {
            OperatorAbilityManager.Instance.OnComboTriggered += ShowComboBanner;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (OperatorAbilityManager.Instance != null)
        {
            OperatorAbilityManager.Instance.OnComboTriggered -= ShowComboBanner;
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnCostChanged -= UpdateCost;
            GameManager.Instance.OnWaveNumberChanged -= UpdateWave;
            GameManager.Instance.OnPhaseChanged -= UpdatePhase;
            GameManager.Instance.OnActiveOwnerChanged -= UpdateActiveOwnerIndicator;
            GameManager.Instance.OnPersonalCostChanged -= HandlePersonalCostChanged;
            GameManager.Instance.OnUnionPowerChanged -= HandleUnionPowerChanged;
            GameManager.Instance.OnTransferRemainingChanged -= UpdateTransferText;
        }
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.OnRewardsUpdated -= UpdateRewardTexts;
        }
        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.OnPlacedCountChanged -= HandlePlacedCountChanged;
            TowerManager.Instance.OnPlacementRejected -= ShowToast;
            TowerManager.Instance.OnUnionPendingStateChanged -= HandleUnionPendingStateChanged;
        }
    }

    // トーストの表示時間を計測する。ポーズ中(Time.timeScale=0)でも消えるようunscaledTimeを使う
    private void Update()
    {
        if (toastObj != null && toastObj.activeSelf && toastHideTime >= 0f && Time.unscaledTime >= toastHideTime)
        {
            toastObj.SetActive(false);
            toastHideTime = -1f;
        }

        // Step 4-2 Rescue: 警告バナーのカウントダウン。Tower側のOfflineグレースタイマーと同じく
        // Time.deltaTimeベースで進行させ、ゲーム速度倍率(x1.2〜x3.0)に自動追随させる
        if (outpostWarningTimer >= 0f)
        {
            outpostWarningTimer -= Time.deltaTime;
            if (outpostWarningTimer <= 0f)
            {
                outpostWarningTimer = -1f;
                if (outpostWarningObj != null) outpostWarningObj.SetActive(false);
            }
            else
            {
                UpdateOutpostWarningText();
            }
        }

        // Step 4-5: Siege Marker予告バナーのカウントダウン（Time.deltaTimeベース＝ゲーム速度倍率に自動追随）
        if (siegeWarningTimer >= 0f)
        {
            siegeWarningTimer -= Time.deltaTime;
            if (siegeWarningTimer <= 0f)
            {
                siegeWarningTimer = -1f;
                if (siegeWarningObj != null) siegeWarningObj.SetActive(false);
            }
        }

        // Step 4-3: Union Power承認バナーの表示・残り秒数はTowerManager側のPending状態を毎フレーム参照する
        // （タイマーの実体はTowerManager.UpdateUnionPendingState()側にあり、ここでは表示を追随させるだけ）
        UpdateUnionApprovalBanner();

        // Step 4-4: Sync Comboバナーのカウントダウン（Time.deltaTimeベース＝ゲーム速度倍率に自動追随）
        if (comboBannerTimer >= 0f)
        {
            comboBannerTimer -= Time.deltaTime;
            if (comboBannerTimer <= 0f)
            {
                comboBannerTimer = -1f;
                if (comboBannerObj != null) comboBannerObj.SetActive(false);
            }
        }

        // Step 4-4: アビリティバー（CD残り秒数・Combo可能インジケータ）は毎フレーム最新化する
        UpdateAbilityBar();
    }

    // Step 2: 配置拒否理由などを画面上部中央に短時間表示する（英語メッセージ）
    public void ShowToast(string message)
    {
        if (toastObj == null || toastText == null) return;
        toastText.text = message;
        toastObj.SetActive(true);
        toastHideTime = Time.unscaledTime + ToastDuration;
    }

    private void OnWaveStartButtonClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StartDefensePhase();
        }
    }

    private void UpdateCost(int cost)
    {
        if (costText != null)
        {
            int maxCost = GameManager.Instance != null ? GameManager.Instance.MaxCostForCurrentWave : 6;
            costText.text = $"COST: {cost}/{maxCost}";
        }

        // Step 4-3: OnCostChangedはSetupフェーズ開始時に必ず1回発火するため、
        // CO-OPバーの表示可否・数値をここでも合わせて最新化しておく
        RefreshCoopResourceBar();
    }

    private void UpdateWave(int wave)
    {
        if (waveText != null)
        {
            waveText.text = $"WAVE: {wave}";
        }

        UpdateAllCardStates();

        // Wave変更時は上限値も変わるため、COST表示の分母を最新化する
        if (GameManager.Instance != null)
        {
            UpdateCost(GameManager.Instance.Cost);
        }
    }

    // Step 4-3: CO-OP時のみCoopResourceBarを表示し、シングルプレイでは従来のCostTextのみを表示する
    private void RefreshCoopResourceBar()
    {
        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;

        if (coopResourceBarObj != null) coopResourceBarObj.SetActive(isCoop);
        if (costText != null) costText.gameObject.SetActive(!isCoop);

        if (!isCoop || GameManager.Instance == null) return;

        int ownId = GameManager.Instance.ActiveOwnerId;
        int allyId = ownId == 0 ? 1 : 0;
        UpdatePersonalCostTexts(ownId, allyId);
        UpdateUnionGaugeDisplay();
        UpdateTransferText(GameManager.Instance.TransferRemaining);
    }

    // Step 4-3: 「自分」= 現在操作中のプレイヤー(ActiveOwnerId)を大きく、「相手」を小さく所有者カラーで表示する
    private void UpdatePersonalCostTexts(int ownId, int allyId)
    {
        if (GameManager.Instance == null) return;

        int ownCost = GameManager.Instance.GetPersonalCost(ownId);
        int allyCost = GameManager.Instance.GetPersonalCost(allyId);
        int cap = GameManager.Instance.MaxPersonalCostForCurrentWave;

        if (ownCostText != null)
        {
            ownCostText.text = $"YOU: {ownCost}/{cap}";
            ownCostText.color = ownId == 1 ? OwnerColorOrange : OwnerColorBlue;
        }
        if (allyCostText != null)
        {
            allyCostText.text = $"ALLY: {allyCost}/{cap}";
            Color c = allyId == 1 ? OwnerColorOrange : OwnerColorBlue;
            c.a = 0.75f;
            allyCostText.color = c;
        }
    }

    // Step 4-3: Union Powerを中央の共有ゲージ（テキスト+塗りつぶしバー）として表示する
    private void UpdateUnionGaugeDisplay()
    {
        if (GameManager.Instance == null) return;

        int union = GameManager.Instance.UnionPower;
        int cap = GameManager.Instance.MaxUnionPowerForCurrentWave;

        if (unionGaugeText != null)
        {
            unionGaugeText.text = $"UNION: {union}/{cap}";
        }
        if (unionGaugeFillImage != null)
        {
            unionGaugeFillImage.fillAmount = cap > 0 ? Mathf.Clamp01((float)union / cap) : 0f;
        }
    }

    private void UpdateTransferText(int remaining)
    {
        if (transferText != null)
        {
            transferText.text = $"TRANSFER: {remaining} LEFT (T)";
        }
    }

    // Step 4-3: personalCostはownerId別に変化するが、表示は「自分/相手」基準のため、
    // どちらのownerIdの変更通知でも常に両方のテキストを引き直す
    private void HandlePersonalCostChanged(int ownerId, int amount)
    {
        if (GameManager.Instance == null) return;
        int ownId = GameManager.Instance.ActiveOwnerId;
        UpdatePersonalCostTexts(ownId, ownId == 0 ? 1 : 0);
    }

    private void HandleUnionPowerChanged(int amount)
    {
        UpdateUnionGaugeDisplay();
        // Union Powerの増減でUnion系カードの活性/非活性が変わるため再評価する
        UpdateAllCardStates();
    }

    // Step 4-3: Union PowerのPendingが開始/終了するたびに、全カードの活性/非活性を再評価する
    private void HandleUnionPendingStateChanged()
    {
        UpdateAllCardStates();
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

    // 解禁Waveと残り設置数の両方を評価して、1枚のカードの表示状態を決定する
    private void UpdateCardState(TowerType type)
    {
        if (TowerManager.Instance == null) return;
        if (!placementCardObjs.TryGetValue(type, out GameObject cardObj) || cardObj == null) return;

        TowerDefinition def = TowerManager.Instance.GetDefinition(type);
        if (def == null) return;

        int wave = GameManager.Instance != null ? GameManager.Instance.CurrentWave : 1;
        bool waveUnlocked = wave >= def.unlockWave;
        bool hasStock = TowerManager.Instance.CanPlaceMoreInCurrentSetup(type);

        // Step 1: 防衛フェーズ中はバリケード以外のカードを半透明・ドラッグ不可にする
        // （緊急復旧としてバリケードのみ設置可能にするため）
        GamePhase phase = GameManager.Instance != null ? GameManager.Instance.CurrentPhase : GamePhase.Setup;
        bool phaseAllowed = phase != GamePhase.Defense || type == TowerType.Barricade;

        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;
        bool isUnionType = TowerManager.IsUnionPoolType(type);

        // Step 4-3: Union Powerの承認待ちが1件でもある間は、種別を問わず全カードを一時的に操作不可にする
        bool pendingLocked = isCoop && TowerManager.Instance.HasPendingUnionRequest;

        // Step 4-3: Union系タワー(Healer/Splash/Frost)はCO-OP時のみ、Union Powerの残量で活性/非活性を切り替える
        bool hasEnoughUnionPower = !isCoop || !isUnionType || GameManager.Instance.UnionPower >= def.cost;

        string label;
        if (!waveUnlocked)
        {
            label = $"{def.displayName} (W{def.unlockWave})";
        }
        else if (def.maxPerSetup > 0)
        {
            int left = Mathf.Max(0, def.maxPerSetup - TowerManager.Instance.GetPlacedCountInCurrentSetup(type));
            label = left > 0 ? $"{def.displayName} ({left} Left)" : $"{def.displayName} (Max)";
        }
        else if (isCoop && isUnionType)
        {
            label = $"{def.displayName} (Union: {def.cost})";
        }
        else
        {
            label = $"{def.displayName} (Cost: {def.cost})";
        }

        bool available = waveUnlocked && hasStock && phaseAllowed && hasEnoughUnionPower && !pendingLocked;
        SetCardAvailability(cardObj, placementCardTexts[type], placementCardGroups[type], available, label);

        // Step 4-3: CO-OP時のみ、Union系カードはカード枠の色を変えて他と区別する
        UpdateCardFrameColor(cardObj, isCoop && isUnionType);
    }

    // Step 4-3: Union系カードの枠色をUIのOutlineコンポーネントで表現する。
    // 既存のImage.color(CardBgColor/LockedCardBgColor)はSetCardAvailability()側の活性/非活性表現と
    // 競合させたくないため、塗りつぶし色は変えずに枠だけ別要素として追加する
    private void UpdateCardFrameColor(GameObject cardObj, bool isUnion)
    {
        Outline outline = cardObj.GetComponent<Outline>();
        if (isUnion)
        {
            if (outline == null) outline = cardObj.AddComponent<Outline>();
            outline.effectColor = UnionCardFrameColor;
            outline.effectDistance = new Vector2(3f, -3f);
            outline.enabled = true;
        }
        else if (outline != null)
        {
            outline.enabled = false;
        }
    }

    private void UpdateAllCardStates()
    {
        if (TowerManager.Instance == null) return;
        foreach (TowerDefinition def in TowerManager.Instance.GetTowerDefinitions())
        {
            if (def != null) UpdateCardState(def.type);
        }
    }

    private void HandlePlacedCountChanged(TowerType type, int placedCount)
    {
        UpdateCardState(type);
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

        // Step 4-1: CO-OP時のみ表示する操作中プレイヤーのインジケータ（PhaseTextとCostTextの間の空きに配置）
        CreateActiveOwnerIndicator(panelObj.transform);

        // Step 4-3: 二層コスト(Personal Cost / Union Power)とTransfer残数を表示する専用バー。
        // トップバーのすぐ下に独立した行として配置するため、上記インジケータとは競合しない
        CreateCoopResourceBar(canvasObj.transform);

        // 4. 配置カードをデータ定義から生成（左端から順に並べる）
        if (TowerManager.Instance != null)
        {
            float cardX = 20f;
            foreach (TowerDefinition def in TowerManager.Instance.GetTowerDefinitions())
            {
                if (def == null) continue;

                GameObject cardObj = CreatePlacementCard(bottomPanelObj.transform, def.type + "Card", cardX,
                    def.displayName, def.type, addCanvasGroup: true, out TMP_Text cardText, out CanvasGroup cardGroup);

                placementCardObjs[def.type] = cardObj;
                placementCardTexts[def.type] = cardText;
                placementCardGroups[def.type] = cardGroup;

                cardX += CardSize.x + CardGap;
            }
        }

        // 5. Wave Startボタン (準備フェーズ中のみ画面中央付近に表示)
        waveStartButton = CreateWaveStartButton(canvasObj.transform);

        // 5.5. Step 2: 配置拒否理由などを表示するトースト（初期状態は非表示）
        CreateToast(canvasObj.transform);

        // 5.6. Step 4-2 Rescue: Outpost破壊警告バナー（初期状態は非表示。CO-OP時のみ表示される）
        CreateOutpostWarningBanner(canvasObj.transform);

        // 5.7. Step 4-5: Siege Marker出現予告バナー（初期状態は非表示。CO-OP時のみ表示される）
        CreateSiegeWarningBanner(canvasObj.transform);

        // 5.8. Step 4-3: Union Power承認バナー（初期状態は非表示。CO-OP時のPendingがある間のみ表示される）
        CreateUnionApprovalBanner(canvasObj.transform);

        // 5.9. Step 4-4: Operator Abilityバー（画面中央右の空きスペース。CO-OP時のDefenseフェーズ中のみ表示）
        CreateAbilityBar(canvasObj.transform);

        // 5.10. Step 4-4: Sync Combo成立時の大きな画面表示（画面中央。初期状態は非表示）
        CreateComboBanner(canvasObj.transform);

        // 6. 獲得した報酬情報のインディケータ表示 (右側)
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

    // 準備フェーズ終了→防衛フェーズ開始を行うボタンを、画面中央上寄りに配置する
    private GameObject CreateWaveStartButton(Transform parent)
    {
        GameObject buttonObj = new GameObject("WaveStartButton");
        buttonObj.transform.SetParent(parent, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = WaveStartButtonBgColor;

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 1f);
        buttonRect.anchorMax = new Vector2(0.5f, 1f);
        buttonRect.pivot = new Vector2(0.5f, 1f);
        buttonRect.anchoredPosition = new Vector2(0f, -(TopStackHeight + 36f));
        buttonRect.sizeDelta = WaveStartButtonSize;

        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = WaveStartButtonBgColor;
        colors.highlightedColor = WaveStartButtonHoverColor;
        colors.pressedColor = WaveStartButtonHoverColor;
        colors.disabledColor = new Color(WaveStartButtonBgColor.r, WaveStartButtonBgColor.g, WaveStartButtonBgColor.b, 0.35f);
        button.colors = colors;
        button.onClick.AddListener(OnWaveStartButtonClicked);

        TMP_Text buttonText = CreateTextObject("WaveStartButtonText", buttonObj.transform, "Wave Start",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, WaveStartButtonSize);
        buttonText.alignment = TextAlignmentOptions.Center;
        buttonText.color = Color.white;
        buttonText.fontSize = 22;

        return buttonObj;
    }

    // Step 2: トップバーのすぐ下、画面右上に短時間表示するトーストメッセージ用UIを作成する。
    // 画面上部中央にはWave Startボタンがあるため、それと重ならないよう右寄せにする
    private void CreateToast(Transform parent)
    {
        GameObject obj = new GameObject("ToastMessage");
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = ToastBgColor;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-20f, -(TopStackHeight + 16f));
        rect.sizeDelta = ToastSize;

        toastText = CreateTextObject("ToastText", obj.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, ToastSize);
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.color = Color.white;
        toastText.fontSize = 18;

        toastObj = obj;
        toastObj.SetActive(false);
    }

    // Step 4-3: トップバーのすぐ下に、二層コスト(Personal Cost / Union Power)とTransfer残数を表示する
    // 専用の行を作成する。初期状態は非表示（RefreshCoopResourceBar()がIsCoopに応じて切り替える）
    private void CreateCoopResourceBar(Transform parent)
    {
        GameObject barObj = new GameObject("CoopResourceBar");
        barObj.transform.SetParent(parent, false);

        Image bg = barObj.AddComponent<Image>();
        bg.color = PanelBgColor;

        RectTransform barRect = barObj.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 1f);
        barRect.anchorMax = new Vector2(1f, 1f);
        barRect.pivot = new Vector2(0.5f, 1f);
        barRect.anchoredPosition = new Vector2(0f, -BarHeight);
        barRect.sizeDelta = new Vector2(0f, CoopBarHeight);

        // 自分(操作中プレイヤー)のPersonal Cost。大きく、所有者カラーで表示する
        ownCostText = CreateTextObject("OwnCostText", barObj.transform, "YOU: 0/0",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(220f, 36f));
        ownCostText.alignment = TextAlignmentOptions.Left;
        ownCostText.fontSize = 22;

        // 相手のPersonal Cost。自分側と同サイズで表示し、所有者カラー（青/橙）とアルファ0.75のみで区別する
        allyCostText = CreateTextObject("AllyCostText", barObj.transform, "ALLY: 0/0",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(270f, 0f), new Vector2(180f, 30f));
        allyCostText.alignment = TextAlignmentOptions.Left;
        allyCostText.fontSize = 22;

        // Union Power共有ゲージ（中央）
        CreateUnionGauge(barObj.transform);

        // Transfer残数（右寄り）
        transferText = CreateTextObject("TransferText", barObj.transform, "TRANSFER: 0 LEFT (T)",
            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(260f, 30f));
        transferText.alignment = TextAlignmentOptions.Right;
        transferText.fontSize = 18;

        coopResourceBarObj = barObj;
        coopResourceBarObj.SetActive(false);
    }

    // Step 4-3: Union Powerを画面中央に共有ゲージとして表示する（背景バー＋塗りつぶしバー＋数値テキスト）
    private void CreateUnionGauge(Transform parent)
    {
        GameObject gaugeObj = new GameObject("UnionGauge");
        gaugeObj.transform.SetParent(parent, false);

        RectTransform gaugeRect = gaugeObj.AddComponent<RectTransform>();
        gaugeRect.anchorMin = new Vector2(0.5f, 0.5f);
        gaugeRect.anchorMax = new Vector2(0.5f, 0.5f);
        gaugeRect.pivot = new Vector2(0.5f, 0.5f);
        gaugeRect.anchoredPosition = Vector2.zero;
        gaugeRect.sizeDelta = new Vector2(240f, 30f);

        Image bg = gaugeObj.AddComponent<Image>();
        bg.color = UnionGaugeBgColor;

        GameObject fillObj = new GameObject("UnionGaugeFill");
        fillObj.transform.SetParent(gaugeObj.transform, false);
        RectTransform fillRect = fillObj.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        unionGaugeFillImage = fillObj.AddComponent<Image>();
        unionGaugeFillImage.color = UnionGaugeFillColor;
        unionGaugeFillImage.type = Image.Type.Filled;
        unionGaugeFillImage.fillMethod = Image.FillMethod.Horizontal;
        unionGaugeFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        unionGaugeFillImage.fillAmount = 0f;

        unionGaugeText = CreateTextObject("UnionGaugeText", gaugeObj.transform, "UNION: 0/0",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 30f));
        unionGaugeText.alignment = TextAlignmentOptions.Center;
        unionGaugeText.fontSize = 18;
        unionGaugeText.color = Color.white;
    }

    // Step 4-3: 画面中央上部にUnion Power承認バナーを作成する。Pendingは常にSetupフェーズ限定で
    // 表示され、同じくSetupフェーズ中はWave Startボタンが表示されている（CO-OP時はTopStackHeight+36pxの
    // 位置まで下がる）ため、その下に潜り込むよう-200fに配置する。OutpostWarningBanner/SiegeWarningBannerは
    // Defenseフェーズ限定でしか表示されずWave Startボタンとは時間的に競合しないため、位置(-170f)据え置きでよい
    private void CreateUnionApprovalBanner(Transform parent)
    {
        GameObject obj = new GameObject("UnionApprovalBanner");
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = ToastBgColor;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -200f);
        rect.sizeDelta = new Vector2(620f, 44f);

        unionApprovalText = CreateTextObject("UnionApprovalText", obj.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 44f));
        unionApprovalText.alignment = TextAlignmentOptions.Center;
        unionApprovalText.fontSize = 20;

        unionApprovalBannerObj = obj;
        unionApprovalBannerObj.SetActive(false);
    }

    // Step 4-3: TowerManager側のPending状態を毎フレーム参照し、バナーの表示・文言・残り秒数を更新する
    private void UpdateUnionApprovalBanner()
    {
        if (unionApprovalBannerObj == null || unionApprovalText == null) return;

        if (TowerManager.Instance == null || !TowerManager.Instance.HasPendingUnionRequest)
        {
            if (unionApprovalBannerObj.activeSelf) unionApprovalBannerObj.SetActive(false);
            return;
        }

        if (!unionApprovalBannerObj.activeSelf) unionApprovalBannerObj.SetActive(true);

        TowerType type = TowerManager.Instance.PendingUnionType;
        int requesterId = TowerManager.Instance.PendingUnionRequesterId;
        float secondsLeft = TowerManager.Instance.PendingUnionSecondsRemaining;

        string requesterLabel = requesterId == 1 ? "ORANGE" : "BLUE";
        Color requesterColor = requesterId == 1 ? OwnerColorOrange : OwnerColorBlue;

        TowerDefinition def = TowerManager.Instance.GetDefinition(type);
        string typeName = def != null ? def.displayName : type.ToString();

        unionApprovalText.text = $"{requesterLabel} requests {typeName} — APPROVE (F) / DENY (G) — {Mathf.CeilToInt(secondsLeft)}s";
        unionApprovalText.color = requesterColor;
    }

    // Step 4-4: 画面中央右（既存要素と重ならない空きスペース）にOperator Abilityバーを作成する。
    // 操作中プレイヤーの装備2種（1/2キーに対応）をCDゲージ付きで表示し、その下にCombo可能インジケータを置く。
    // 初期状態は非表示（CO-OP時のDefenseフェーズ中のみUpdateAbilityBar()が表示する）
    private void CreateAbilityBar(Transform parent)
    {
        GameObject barObj = new GameObject("AbilityBar");
        barObj.transform.SetParent(parent, false);

        RectTransform barRect = barObj.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(1f, 0.5f);
        barRect.anchorMax = new Vector2(1f, 0.5f);
        barRect.pivot = new Vector2(1f, 0.5f);
        barRect.anchoredPosition = new Vector2(-24f, 40f);
        barRect.sizeDelta = new Vector2(AbilityBarWidth, AbilitySlotHeight * 2 + AbilitySlotGap + 40f);

        Image barBg = barObj.AddComponent<Image>();
        barBg.color = PanelBgColor;

        for (int i = 0; i < 2; i++)
        {
            float y = -10f - i * (AbilitySlotHeight + AbilitySlotGap);

            GameObject slotObj = new GameObject($"AbilitySlot{i}");
            slotObj.transform.SetParent(barObj.transform, false);
            RectTransform slotRect = slotObj.AddComponent<RectTransform>();
            slotRect.anchorMin = new Vector2(0.5f, 1f);
            slotRect.anchorMax = new Vector2(0.5f, 1f);
            slotRect.pivot = new Vector2(0.5f, 1f);
            slotRect.anchoredPosition = new Vector2(0f, y);
            slotRect.sizeDelta = new Vector2(AbilityBarWidth - 20f, AbilitySlotHeight);

            Image slotBg = slotObj.AddComponent<Image>();
            slotBg.color = AbilityGaugeBgColor;
            abilitySlotBgImages[i] = slotBg;

            // CDゲージ（塗りつぶし。Union Gaugeと同じ「背景+全面ストレッチの子Image」構成を流用する）
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(slotObj.transform, false);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.color = AbilityGaugeFillColor;
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillAmount = 1f;
            abilitySlotFillImages[i] = fillImg;

            TMP_Text slotText = CreateTextObject($"AbilitySlot{i}Text", slotObj.transform, "",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(AbilityBarWidth - 20f, AbilitySlotHeight));
            slotText.alignment = TextAlignmentOptions.Center;
            slotText.fontSize = 15;
            slotText.color = Color.white;
            abilitySlotTexts[i] = slotText;
        }

        comboReadyText = CreateTextObject("ComboReadyText", barObj.transform, "SYNC COMBO READY!",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -10f - 2 * (AbilitySlotHeight + AbilitySlotGap)), new Vector2(AbilityBarWidth - 20f, 24f));
        comboReadyText.alignment = TextAlignmentOptions.Center;
        comboReadyText.fontSize = 14;
        comboReadyText.color = ComboReadyColor;
        comboReadyObj = comboReadyText.gameObject;
        comboReadyObj.SetActive(false);

        abilityBarObj = barObj;
        abilityBarObj.SetActive(false);
    }

    // Step 4-4: 操作中プレイヤーの装備2種のCD状況とCombo可能インジケータを毎フレーム最新化する。
    // CO-OP時かつDefenseフェーズ中のみ表示する（Setup/Rewardフェーズではアビリティ自体が使えないため隠す）
    private void UpdateAbilityBar()
    {
        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;
        bool isDefense = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Defense;
        bool show = isCoop && isDefense && OperatorAbilityManager.Instance != null;

        if (abilityBarObj != null) abilityBarObj.SetActive(show);
        if (!show) return;

        int ownerId = GameManager.Instance.ActiveOwnerId;
        OperatorAbilityManager mgr = OperatorAbilityManager.Instance;

        bool comboReady = mgr.IsComboWindowOpenFor(ownerId);

        for (int slot = 0; slot < 2; slot++)
        {
            OperatorAbilityType type = mgr.GetLoadout(ownerId, slot);
            float remaining = mgr.GetCooldownRemaining(ownerId, slot);
            float duration = OperatorAbilityManager.GetCooldownDuration(type);
            float ratio = duration > 0f ? Mathf.Clamp01(1f - remaining / duration) : 1f;

            if (abilitySlotFillImages[slot] != null) abilitySlotFillImages[slot].fillAmount = ratio;

            string keyLabel = slot == 0 ? "1" : "2";
            string abilityName = OperatorAbilityManager.GetAbilityLabel(type);
            if (abilitySlotTexts[slot] != null)
            {
                abilitySlotTexts[slot].text = remaining > 0f
                    ? $"[{keyLabel}] {abilityName}  {Mathf.CeilToInt(remaining)}s"
                    : $"[{keyLabel}] {abilityName}  READY";
                abilitySlotTexts[slot].color = remaining > 0f ? new Color(1f, 1f, 1f, 0.65f) : Color.white;
            }

            // Combo可能時は相手が直近アビリティを使ったことが分かるよう、装備アイコン(スロット背景)を発光させる
            if (abilitySlotBgImages[slot] != null)
            {
                abilitySlotBgImages[slot].color = comboReady ? AbilityGaugeGlowColor : AbilityGaugeBgColor;
            }
        }

        if (comboReadyObj != null) comboReadyObj.SetActive(comboReady);
        if (comboReady && comboReadyText != null)
        {
            float pulse = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
            comboReadyText.color = Color.Lerp(ComboReadyColor, Color.white, pulse);
        }
    }

    // Step 4-4: 画面中央にSync Combo成立時のCombo名を大きく表示する。初期状態は非表示。
    // OperatorAbilityManager.OnComboTriggeredから呼ばれ、ComboBannerDuration秒後にUpdate()内で自動的に消える
    private void CreateComboBanner(Transform parent)
    {
        GameObject obj = new GameObject("ComboBanner");
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 90f);
        rect.sizeDelta = new Vector2(800f, 90f);

        comboBannerText = CreateTextObject("ComboBannerText", obj.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 90f));
        comboBannerText.alignment = TextAlignmentOptions.Center;
        comboBannerText.fontSize = 48;
        comboBannerText.color = new Color(1f, 0.85f, 0.3f);

        comboBannerObj = obj;
        comboBannerObj.SetActive(false);
    }

    // Step 4-4: OperatorAbilityManager.OnComboTriggeredから呼ばれる。成功体験の可視化が最重要のため、
    // 画面中央に大きくコンボ名を表示する
    private void ShowComboBanner(string comboName)
    {
        if (comboBannerObj == null || comboBannerText == null) return;
        comboBannerText.text = $"⚡ SYNC COMBO: {comboName.ToUpperInvariant()} ⚡";
        comboBannerObj.SetActive(true);
        comboBannerTimer = ComboBannerDuration;
    }

    // Step 4-2 Rescue: 画面中央上部（Wave Startボタンの下）に、Outpost破壊警告を表示するバナーを作成する。
    // 初期状態は非表示。ShowOutpostDownWarning()から表示され、Update()でカウントダウンする
    private void CreateOutpostWarningBanner(Transform parent)
    {
        GameObject obj = new GameObject("OutpostWarningBanner");
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = ToastBgColor;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -170f);
        rect.sizeDelta = new Vector2(460f, 44f);

        outpostWarningText = CreateTextObject("OutpostWarningText", obj.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 44f));
        outpostWarningText.alignment = TextAlignmentOptions.Center;
        outpostWarningText.fontSize = 22;

        outpostWarningObj = obj;
        outpostWarningObj.SetActive(false);
    }

    // Step 4-2 Rescue: CO-OP時、Outpostが破壊された瞬間にTower.Die()から呼ばれる。
    // 所有者カラーの警告バナーを表示し、以後Update()でカウントダウンする
    public void ShowOutpostDownWarning(int ownerId)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;
        if (outpostWarningObj == null || outpostWarningText == null) return;

        outpostWarningOwnerId = ownerId;
        outpostWarningTimer = OutpostWarningDuration;
        outpostWarningObj.SetActive(true);
        UpdateOutpostWarningText();
    }

    // 残り秒数の表示を1箇所に集約する
    private void UpdateOutpostWarningText()
    {
        if (outpostWarningText == null) return;

        string ownerLabel = outpostWarningOwnerId == 1 ? "ORANGE" : "BLUE";
        Color ownerColor = outpostWarningOwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;
        int secondsLeft = Mathf.CeilToInt(Mathf.Max(0f, outpostWarningTimer));

        outpostWarningText.text = $"⚠ {ownerLabel} OUTPOST DOWN — {secondsLeft}s";
        outpostWarningText.color = ownerColor;
    }

    // Step 4-5: 画面中央上部、OutpostWarningBannerのすぐ下にSiege Marker出現予告バナーを作成する。
    // 初期状態は非表示。ShowSiegeInboundWarning()から表示され、Update()で数秒後に自動的に消える
    private void CreateSiegeWarningBanner(Transform parent)
    {
        GameObject obj = new GameObject("SiegeWarningBanner");
        obj.transform.SetParent(parent, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = ToastBgColor;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -220f);
        rect.sizeDelta = new Vector2(520f, 44f);

        siegeWarningText = CreateTextObject("SiegeWarningText", obj.transform, "",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 44f));
        siegeWarningText.alignment = TextAlignmentOptions.Center;
        siegeWarningText.fontSize = 22;

        siegeWarningObj = obj;
        siegeWarningObj.SetActive(false);
    }

    // Step 4-5: CO-OP時、Siege Marker出現の瞬間にEnemy.SetupSiegeMarker()から呼ばれる。
    // 「⚠ SIEGE INBOUND — BLUE OUTPOST (3)」の形式で、対象の所有者と識別番号を表示する
    public void ShowSiegeInboundWarning(int ownerId, int outpostNumber)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;
        if (siegeWarningObj == null || siegeWarningText == null) return;

        string ownerLabel = ownerId == 1 ? "ORANGE" : "BLUE";
        Color ownerColor = ownerId == 1 ? OwnerColorOrange : OwnerColorBlue;

        siegeWarningText.text = $"⚠ SIEGE INBOUND — {ownerLabel} OUTPOST ({outpostNumber})";
        siegeWarningText.color = ownerColor;
        siegeWarningObj.SetActive(true);
        siegeWarningTimer = SiegeWarningDuration;
    }

    // Step 4-1: 現在操作中プレイヤーを示すインジケータ（トップバー中央、PhaseTextとCostTextの間）を作成する。
    // 生成時点ではまだGameManager.Instance.IsCoopが確定していない可能性があるため、
    // 初期状態は非表示にしておき、UpdateActiveOwnerIndicator()で毎回表示可否を判定する
    private void CreateActiveOwnerIndicator(Transform parent)
    {
        // 幅400pxに設定：最長ラベル"PLAYER 2 (ORANGE)"が28pxボールドで折り返さない幅を確保する
        activeOwnerIndicatorText = CreateTextObject("ActiveOwnerIndicatorText", parent, "PLAYER 1 (BLUE)",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(400f, 50f));
        activeOwnerIndicatorText.alignment = TextAlignmentOptions.Center;
        activeOwnerIndicatorText.color = OwnerColorBlue;
        // 将来ラベルが長くなっても折り返さないよう明示的に無効化する
        activeOwnerIndicatorText.textWrappingMode = TextWrappingModes.NoWrap;

        activeOwnerIndicatorObj = activeOwnerIndicatorText.gameObject;
        activeOwnerIndicatorObj.SetActive(false);
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
        panelRect.sizeDelta = new Vector2(0f, BarHeight);
        return panelObj;
    }

    // ドラッグ＆ドロップ配置用のカードUIを作成する（左端基準のx位置指定）
    private GameObject CreatePlacementCard(Transform parent, string name, float x, string label,
        TowerType type, bool addCanvasGroup, out TMP_Text cardText, out CanvasGroup canvasGroup)
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
        dragHandler.towerType = type;

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
        rect.anchoredPosition = new Vector2(xOffset, BarHeight);
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

        // Step 1: フェーズ切り替え時に配置カードの操作可否を再評価する
        // （Defenseフェーズ中はバリケード以外を半透明・ドラッグ不可にするため）
        UpdateAllCardStates();

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
                // 画面中央にGameOverUIが大きく表示されるため、Phase表示欄には出さない
                phaseText.text = "";
                break;
        }
    }

    // Step 4-1: 操作中プレイヤーが切り替わるたびに呼ばれる（OnActiveOwnerChanged購読）。
    // CO-OP時のみ表示し、シングルプレイ時は常に非表示のまま
    private void UpdateActiveOwnerIndicator(int ownerId)
    {
        if (activeOwnerIndicatorObj == null || activeOwnerIndicatorText == null) return;

        bool isCoop = GameManager.Instance != null && GameManager.Instance.IsCoop;
        activeOwnerIndicatorObj.SetActive(isCoop);
        if (!isCoop) return;

        if (ownerId == 1)
        {
            activeOwnerIndicatorText.text = "PLAYER 2 (ORANGE)";
            activeOwnerIndicatorText.color = OwnerColorOrange;
        }
        else
        {
            activeOwnerIndicatorText.text = "PLAYER 1 (BLUE)";
            activeOwnerIndicatorText.color = OwnerColorBlue;
        }

        // Step 4-3: 「自分/相手」の表示はActiveOwnerId基準のため、Tabで操作プレイヤーが切り替わるたびに引き直す
        UpdatePersonalCostTexts(ownerId, ownerId == 0 ? 1 : 0);
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
            rect.anchoredPosition = new Vector2(xOffset, BarHeight);
            rewardIndicatorTexts[def].text = $"{RewardIndicatorLabel(def)}: {count}";
            xOffset -= IndicatorSize.x + 20f;
        }
    }
}
