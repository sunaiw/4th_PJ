using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Step 4-4 / 10章未決事項#3: CO-OP専用のOperator Ability選択モーダル。
// 「ゲーム開始前に1回だけ選択し、試合中は固定」という仕様書の決定を実現するため、
// GameManager.Start()でOperatorAbilityManagerの直後にAddComponent()され、起動直後に1度だけ表示される。
// 各プレイヤーが4種から重複なしで2種を選び、STARTボタンでOperatorAbilityManager.SetLoadout()へ反映して自壊する。
public class AbilityLoadoutUI : MonoBehaviour
{
    private static readonly OperatorAbilityType[] AllAbilityTypes =
    {
        OperatorAbilityType.Overcharge,
        OperatorAbilityType.FieldRepair,
        OperatorAbilityType.FreezeZone,
        OperatorAbilityType.TauntBeacon,
    };

    // TutorialUI.PanelBgColorと同じ配色。全画面ブロッカー兼背景として使う
    private static readonly Color BlockerColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);
    // HUDManagerのOwnerColorBlue/OwnerColorOrangeと同じ値（プレイヤー識別色の一貫性のため）
    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);
    // HUDManagerのCardBgColor/LockedCardBgColorを流用（選択中=通常カード濃度、未選択=薄いロック済み濃度）
    private static readonly Color CardBgColorSelected = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color CardBgColorUnselected = new Color(0.2f, 0.25f, 0.3f, 0.35f);
    // HUDManagerのWaveStartButtonBgColor/HoverColorと同じ配色
    private static readonly Color StartButtonBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color StartButtonHoverColor = new Color(0.28f, 0.35f, 0.42f, 0.9f);

    private static readonly Vector2 CardSize = new Vector2(700f, 90f);
    private const float CardGap = 16f;
    private const float ColumnLeftX = -480f;
    private const float ColumnRightX = 480f;
    private const float FirstCardTopY = -300f;

    private GameObject canvasObj;
    private Button startButton;

    private class PlayerColumn
    {
        public Color ownerColor;
        // 選択順=スロット順（先頭がslot1、2番目がslot2）。Removeで自動的に繰り上がる
        public readonly List<OperatorAbilityType> selected = new List<OperatorAbilityType>();
        public readonly Dictionary<OperatorAbilityType, Image> cardBgs = new Dictionary<OperatorAbilityType, Image>();
        public readonly Dictionary<OperatorAbilityType, Outline> cardOutlines = new Dictionary<OperatorAbilityType, Outline>();
        public readonly Dictionary<OperatorAbilityType, TMP_Text> cardBadges = new Dictionary<OperatorAbilityType, TMP_Text>();
    }

    private readonly PlayerColumn[] columns = new PlayerColumn[2];

    // Step 4-0b-2: 各プレイヤー列（見出し+カード4枚）をまとめる親。ネットワーク接続中は自分の列だけを表示するため。
    // Canvas全面にストレッチした透明な親にしておくことで、子のanchoredPosition基準はCanvas直下のときと同じになる
    private readonly RectTransform[] columnRoots = new RectTransform[2];

    private void Start()
    {
        // シングルプレイでは完全に無害化する: Canvas・GameObjectを一切生成せず即returnする
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;

        // Time.timeScaleはここでは一切操作しない。
        // ゲーム開始直後はGameManager.GameLoopCoroutine()のSetupフェーズ
        // （while (!setupPhaseFinished) yield return null;）で停止しており、
        // それを進めるHUDのWave StartボタンはこのUIの全画面ブロッカーで覆われクリック不能になる。
        // さらにWave1のSetupフェーズはTutorialUI.ShowPages()が既にTime.timeScale=0にして
        // 自分でHidePanel()時に復元する仕組みを持っているため、このUIまでTime.timeScaleを
        // save/restoreすると両者の復元処理が競合して壊れる（例: 先にこちらが1へ戻した直後に
        // TutorialUI側が保存していた古い値で上書きしてしまう等）。よって時間停止はTutorialUI（と、
        // そもそも進行がボタン待ちである設計）に委ね、このUIは触れない。
        columns[0] = new PlayerColumn { ownerColor = OwnerColorBlue };
        columns[1] = new PlayerColumn { ownerColor = OwnerColorOrange };

        // OperatorAbilityManagerのInspector既定値をプリセットする
        // (Player1: Overcharge+FieldRepair, Player2: FreezeZone+TauntBeacon)。
        // これによりSTARTボタンは表示直後から押下可能になる
        columns[0].selected.Add(OperatorAbilityType.Overcharge);
        columns[0].selected.Add(OperatorAbilityType.FieldRepair);
        columns[1].selected.Add(OperatorAbilityType.FreezeZone);
        columns[1].selected.Add(OperatorAbilityType.TauntBeacon);

        CreateLayout();
        RefreshColumnCards(0);
        RefreshColumnCards(1);

        // Step 4-0b-2: このモーダルはCoopConnectUI(sortingOrder 250)の背面(200)に作られ、接続選択の完了後に
        // 操作可能になるため、表示時点ではまだネットワーク状態が確定していない。接続状態の変化に追随して
        // 列の表示（ネットワーク時は自分の列のみ）を切り替える
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.OnConnectionStateChanged += ApplyNetworkLayout;
        }
        ApplyNetworkLayout();
    }

    private void OnDestroy()
    {
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.OnConnectionStateChanged -= ApplyNetworkLayout;
        }
    }

    // Step 4-0b-2: ネットワーク接続中は各デバイスが自分のownerIdの列だけを選ぶ（相手の列は相手のデバイスで選ぶ）。
    // 自分の列を中央へ寄せ、相手の列は非表示にする。接続失敗・切断で非ネットワークに戻れば両列を元の位置へ戻す
    // （PLAY ON THIS DEVICEは従来どおり1台で両プレイヤー分を選ぶ）
    private void ApplyNetworkLayout()
    {
        if (columnRoots[0] == null || columnRoots[1] == null) return;

        bool networked = IsNetworkedLoadout();
        int local = networked ? CoopNetworkManager.Instance.LocalOwnerId : -1;

        for (int i = 0; i < 2; i++)
        {
            bool visible = !networked || i == local;
            columnRoots[i].gameObject.SetActive(visible);

            // 自分の列だけを表示するときは画面中央へ寄せる（列のX座標オフセットを打ち消す）
            float shiftX = (networked && i == local) ? -(i == 0 ? ColumnLeftX : ColumnRightX) : 0f;
            columnRoots[i].offsetMin = new Vector2(shiftX, 0f);
            columnRoots[i].offsetMax = new Vector2(shiftX, 0f);
        }

        RefreshStartButtonInteractable();
    }

    private static bool IsNetworkedLoadout()
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        return net != null && net.IsNetworked && (net.LocalOwnerId == 0 || net.LocalOwnerId == 1);
    }

    private void CreateLayout()
    {
        canvasObj = new GameObject("AbilityLoadoutCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // TutorialUI(100)より必ず手前、GameOverUI(300)より必ず奥に表示する
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 全画面ブロッカー: HUDのWave StartボタンやTutorialUIのNextボタンへのクリックを遮断する（背景も兼ねる）
        GameObject blockerObj = new GameObject("AbilityLoadoutBlocker");
        blockerObj.transform.SetParent(canvasObj.transform, false);
        Image blockerImage = blockerObj.AddComponent<Image>();
        blockerImage.color = BlockerColor;
        RectTransform blockerRect = blockerObj.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;

        TMP_Text title = CreateText(canvasObj.transform, "Title", "SELECT ABILITIES",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -120f), new Vector2(900f, 60f));
        title.fontSize = 44;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;

        CreatePlayerColumn(0, ColumnLeftX, "PLAYER 1 (BLUE)");
        CreatePlayerColumn(1, ColumnRightX, "PLAYER 2 (ORANGE)");

        CreateHintAndStartButton();
    }

    private void CreatePlayerColumn(int ownerId, float xOffset, string headerLabel)
    {
        PlayerColumn col = columns[ownerId];

        GameObject rootObj = new GameObject($"P{ownerId}ColumnRoot", typeof(RectTransform));
        rootObj.transform.SetParent(canvasObj.transform, false);
        RectTransform rootRect = rootObj.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        columnRoots[ownerId] = rootRect;

        TMP_Text header = CreateText(rootObj.transform, $"P{ownerId}Header", headerLabel,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(xOffset, -220f), new Vector2(800f, 50f));
        header.fontSize = 30;
        header.fontStyle = FontStyles.Bold;
        header.color = col.ownerColor;
        header.alignment = TextAlignmentOptions.Center;

        for (int i = 0; i < AllAbilityTypes.Length; i++)
        {
            float cardTopY = FirstCardTopY - i * (CardSize.y + CardGap);
            CreateAbilityCard(ownerId, xOffset, cardTopY, AllAbilityTypes[i]);
        }
    }

    private void CreateAbilityCard(int ownerId, float xOffset, float topY, OperatorAbilityType type)
    {
        PlayerColumn col = columns[ownerId];

        GameObject cardObj = new GameObject($"P{ownerId}Card_{type}");
        cardObj.transform.SetParent(columnRoots[ownerId], false);

        Image bg = cardObj.AddComponent<Image>();
        RectTransform rect = cardObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(xOffset, topY);
        rect.sizeDelta = CardSize;

        // 選択中カードの縁取り。HUDManager.UpdateCardFrameColor()と同じOutline方式（塗りつぶし色とは独立させる）
        Outline outline = cardObj.AddComponent<Outline>();
        outline.effectColor = col.ownerColor;
        outline.effectDistance = new Vector2(3f, -3f);
        outline.enabled = false;

        Button button = cardObj.AddComponent<Button>();
        button.onClick.AddListener(() => OnCardClicked(ownerId, type));

        TMP_Text badge = CreateText(cardObj.transform, "Badge", "",
            new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(16f, 0f), new Vector2(60f, CardSize.y));
        badge.fontSize = 22;
        badge.fontStyle = FontStyles.Bold;
        badge.color = col.ownerColor;
        badge.alignment = TextAlignmentOptions.Center;

        TMP_Text nameText = CreateText(cardObj.transform, "Name", OperatorAbilityManager.GetAbilityLabel(type),
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(84f, -10f), new Vector2(600f, 34f));
        nameText.fontSize = 22;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Left;

        string desc = $"{OperatorAbilityManager.GetAbilityDescription(type)} (CD {OperatorAbilityManager.GetCooldownDuration(type):0}s)";
        TMP_Text descText = CreateText(cardObj.transform, "Desc", desc,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(84f, 10f), new Vector2(600f, 24f));
        descText.fontSize = 16;
        descText.color = new Color(1f, 1f, 1f, 0.8f);
        descText.alignment = TextAlignmentOptions.Left;

        col.cardBgs[type] = bg;
        col.cardOutlines[type] = outline;
        col.cardBadges[type] = badge;
    }

    private void CreateHintAndStartButton()
    {
        TMP_Text hint = CreateText(canvasObj.transform, "Hint",
            "TIP: If both players pick the same ability, its Sync Combo becomes a stronger same-type combo.",
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, 170f), new Vector2(1400f, 26f));
        hint.fontSize = 18;
        hint.color = new Color(1f, 1f, 1f, 0.8f);
        hint.alignment = TextAlignmentOptions.Center;

        GameObject buttonObj = new GameObject("StartButton");
        buttonObj.transform.SetParent(canvasObj.transform, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = StartButtonBgColor;

        RectTransform buttonRect = buttonObj.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 90f);
        buttonRect.sizeDelta = new Vector2(260f, 64f);

        startButton = buttonObj.AddComponent<Button>();
        // HUDManager.CreateWaveStartButton()と同じ「非活性=同色を薄く」のパターン
        ColorBlock colors = startButton.colors;
        colors.normalColor = StartButtonBgColor;
        colors.highlightedColor = StartButtonHoverColor;
        colors.pressedColor = StartButtonHoverColor;
        colors.disabledColor = new Color(StartButtonBgColor.r, StartButtonBgColor.g, StartButtonBgColor.b, 0.35f);
        startButton.colors = colors;
        startButton.onClick.AddListener(OnStartButtonClicked);

        TMP_Text buttonText = CreateText(buttonObj.transform, "Text", "START",
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(260f, 64f));
        buttonText.fontSize = 28;
        buttonText.fontStyle = FontStyles.Bold;
        buttonText.color = Color.white;
        buttonText.alignment = TextAlignmentOptions.Center;
    }

    private void OnCardClicked(int ownerId, OperatorAbilityType type)
    {
        PlayerColumn col = columns[ownerId];

        if (col.selected.Contains(type))
        {
            // Listから除去するとインデックスが自動的に繰り上がる
            // （slot1解除時にslot2がslot1へシフトする挙動をこれだけで満たす）
            col.selected.Remove(type);
        }
        else if (col.selected.Count < 2)
        {
            col.selected.Add(type);
        }
        // else: 既に2件選択済みなら何もしない（3件目のクリックは無視）

        RefreshColumnCards(ownerId);
        RefreshStartButtonInteractable();
    }

    private void RefreshColumnCards(int ownerId)
    {
        PlayerColumn col = columns[ownerId];
        foreach (OperatorAbilityType type in AllAbilityTypes)
        {
            int slotIndex = col.selected.IndexOf(type);
            bool selected = slotIndex >= 0;

            col.cardBgs[type].color = selected ? CardBgColorSelected : CardBgColorUnselected;
            col.cardOutlines[type].enabled = selected;
            col.cardBadges[type].text = selected ? $"[{slotIndex + 1}]" : "";
        }
    }

    private void RefreshStartButtonInteractable()
    {
        // Step 4-0b-2: ネットワーク接続中は自分の列だけが選択対象（相手の列は非表示で選択不能）のため、自分の列のみ判定する
        bool ready;
        if (IsNetworkedLoadout())
        {
            ready = columns[CoopNetworkManager.Instance.LocalOwnerId].selected.Count == 2;
        }
        else
        {
            ready = columns[0].selected.Count == 2 && columns[1].selected.Count == 2;
        }
        startButton.interactable = ready;
    }

    private void OnStartButtonClicked()
    {
        if (IsNetworkedLoadout())
        {
            // Step 4-0b-2: 自分のownerIdの列だけを反映する。ホストが自分の（既定値のままの）列1で
            // owner 1のロードアウトを上書きしてしまわないよう、相手の列には一切触れない。
            // ホストは列0をローカル適用し、クライアントは列1を適用した上でホストへ送る
            // （ホスト側のowner 1ロードアウトはホストがCoopCommandManager経由で受信して設定する）
            int local = CoopNetworkManager.Instance.LocalOwnerId;
            PlayerColumn col = columns[local];
            if (OperatorAbilityManager.Instance != null)
            {
                OperatorAbilityManager.Instance.SetLoadout(local, col.selected[0], col.selected[1]);
            }
            else
            {
                Debug.LogWarning("[AbilityLoadoutUI] OperatorAbilityManager.Instance is null. Falling back to Inspector default loadouts.");
            }

            if (local == 1 && CoopCommandManager.Instance != null)
            {
                CoopCommandManager.Instance.SendSetLoadout(col.selected[0], col.selected[1]);
            }
        }
        else if (OperatorAbilityManager.Instance != null)
        {
            OperatorAbilityManager.Instance.SetLoadout(0, columns[0].selected[0], columns[0].selected[1]);
            OperatorAbilityManager.Instance.SetLoadout(1, columns[1].selected[0], columns[1].selected[1]);
        }
        else
        {
            // ここでゲームを止めない: Instanceが無い場合はInspector既定値のまま進行させる
            Debug.LogWarning("[AbilityLoadoutUI] OperatorAbilityManager.Instance is null. Falling back to Inspector default loadouts.");
        }

        Destroy(canvasObj);
        Destroy(this);
    }

    private TMP_Text CreateText(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        // カード/見出し類は全て1行表示で幅を確保しているため、意図せぬ折り返しを明示的に無効化する
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmp;
    }
}
