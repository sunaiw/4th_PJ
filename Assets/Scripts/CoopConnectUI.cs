using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

// Step 4-0b-1: CO-OP専用の接続選択モーダル。ゲーム開始直後に1度だけ表示し、
// HOST GAME / JOIN GAME / PLAY ON THIS DEVICE のいずれかを選ばせる。
// シングルプレイ(GameManager.IsCoop==false)ではStart()の先頭で即returnし、GameObjectを一切生成しない。
// AbilityLoadoutUIの構築スタイル（コードでCanvas/CanvasScaler/GraphicRaycaster/全画面ブロッカーを生成する）を踏襲する。
public class CoopConnectUI : MonoBehaviour
{
    // 現状は固定ポート。将来的にUIから変更可能にする場合はここを起点に拡張する
    [SerializeField] private ushort port = 7777;

    // AbilityLoadoutUI(200)より必ず手前、GameOverUI(300)より必ず奥に表示する
    private const int SortingOrder = 250;

    // JOIN GAME接続試行のタイムアウト（実時間基準）。NGO側のClientConnectionBufferTimeout(既定10秒)と
    // 概ね揃えているが、こちらはUI側で独立して計測する保険的なタイムアウトである
    private const float ConnectTimeoutSeconds = 10f;

    // TutorialUI.PanelBgColorと同じ配色。全画面ブロッカー兼背景として使う
    private static readonly Color BlockerColor = new Color(0.05f, 0.06f, 0.08f, 0.92f);
    // HUDManagerのOwnerColorBlue/OwnerColorOrangeと同じ値（プレイヤー識別色の一貫性のため）
    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);
    // AbilityLoadoutUIのStartButtonBgColor/HoverColorと同じ配色
    private static readonly Color ButtonBgColor = new Color(0.2f, 0.25f, 0.3f, 0.9f);
    private static readonly Color ButtonHoverColor = new Color(0.28f, 0.35f, 0.42f, 0.9f);
    private static readonly Color ErrorColor = new Color(1f, 0.4f, 0.4f);
    private static readonly Color NeutralTextColor = new Color(1f, 1f, 1f, 0.85f);

    private enum UiState
    {
        Choice,
        HostWaiting,
        ClientConnecting,
    }

    private GameObject canvasObj;
    private GameObject choicePanel;
    private GameObject joinPanel;
    private GameObject cancelButtonObj;
    private TMP_Text statusText;
    private TMP_InputField ipInputField;

    private UiState state;
    private float connectStartTimeUnscaled;
    private bool subscribedToNetworkEvents;
    // HostWaiting中のグローバルIP取得コルーチン。キャンセル/接続成立/失敗等でHostWaitingを抜ける際は
    // 必ずStopGlobalIpFetch()で停止し、取得完了後にstatusTextを上書きしてしまう事故を防ぐ
    private Coroutine globalIpFetchCoroutine;

    private void Start()
    {
        // シングルプレイでは完全に無害化する: Canvas・GameObjectを一切生成せず即returnする
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;

        // AbilityLoadoutUIと同じ理由でTime.timeScaleは一切操作しない。
        // ゲーム開始直後のSetupフェーズはHUDのWave Startボタン待ちで進行が止まっており、
        // このUIの全画面ブロッカーが下layerのHUD/TutorialUIへのクリックを遮断するだけで
        // 進行を止められる。時間停止の責務はTutorialUI側に委ね、ここでは触れない
        CreateLayout();
        ShowChoiceState();
    }

    private void OnDestroy()
    {
        StopGlobalIpFetch();
        UnsubscribeFromNetworkEvents();
    }

    private void Update()
    {
        if (state != UiState.ClientConnecting) return;

        // Time.timeScaleに依存しない実時間でタイムアウトを計測する
        if (Time.unscaledTime - connectStartTimeUnscaled >= ConnectTimeoutSeconds)
        {
            HandleConnectionFailure("Connection timed out. Check the IP address and try again.");
        }
    }

    // ================= レイアウト構築 =================

    private void CreateLayout()
    {
        canvasObj = new GameObject("CoopConnectCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<GraphicRaycaster>();

        // 全画面ブロッカー: 下層のHUD/TutorialUIへのクリックを遮断する（背景も兼ねる）
        GameObject blockerObj = new GameObject("CoopConnectBlocker");
        blockerObj.transform.SetParent(canvasObj.transform, false);
        Image blockerImage = blockerObj.AddComponent<Image>();
        blockerImage.color = BlockerColor;
        RectTransform blockerRect = blockerObj.GetComponent<RectTransform>();
        blockerRect.anchorMin = Vector2.zero;
        blockerRect.anchorMax = Vector2.one;
        blockerRect.offsetMin = Vector2.zero;
        blockerRect.offsetMax = Vector2.zero;

        TMP_Text title = CreateText(canvasObj.transform, "Title", "CO-OP CONNECTION",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -120f), new Vector2(900f, 60f));
        title.fontSize = 44;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;

        // HostWaiting状態でGlobal IP/LAN IP/Port/注意書きの5～7行程度を表示するため、高さを160→260に拡張
        statusText = CreateText(canvasObj.transform, "StatusText", "SELECT A CONNECTION MODE",
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -220f), new Vector2(1400f, 260f));
        statusText.fontSize = 22;
        statusText.color = NeutralTextColor;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.textWrappingMode = TextWrappingModes.Normal; // IPアドレス一覧など複数行になり得るため折り返しを許可する

        CreateChoicePanel();
        CreateJoinPanel();
        CreateCancelButton();
    }

    private void CreateChoicePanel()
    {
        choicePanel = new GameObject("ChoicePanel");
        choicePanel.transform.SetParent(canvasObj.transform, false);
        RectTransform rect = choicePanel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        CreateButton(choicePanel.transform, "HostButton", "HOST GAME", new Vector2(0f, -20f), OnHostClicked);
        CreateButton(choicePanel.transform, "JoinButton", "JOIN GAME", new Vector2(0f, -130f), OnJoinPanelOpen);
        CreateButton(choicePanel.transform, "PlayLocalButton", "PLAY ON THIS DEVICE", new Vector2(0f, -240f), OnPlayOnThisDeviceClicked);
    }

    private void CreateJoinPanel()
    {
        joinPanel = new GameObject("JoinPanel");
        joinPanel.transform.SetParent(canvasObj.transform, false);
        RectTransform rect = joinPanel.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        ipInputField = CreateIpInputField(joinPanel.transform, new Vector2(0f, -20f));
        CreateButton(joinPanel.transform, "ConnectButton", "CONNECT", new Vector2(0f, -130f), OnConnectClicked);
        CreateButton(joinPanel.transform, "JoinBackButton", "BACK", new Vector2(0f, -240f), OnJoinPanelBack);

        joinPanel.SetActive(false);
    }

    private void CreateCancelButton()
    {
        // HostWaiting/ClientConnecting状態でのみ表示する共通のキャンセルボタン
        cancelButtonObj = CreateButton(canvasObj.transform, "CancelButton", "CANCEL", new Vector2(0f, -240f), OnCancelClicked);
        cancelButtonObj.SetActive(false);
    }

    private TMP_InputField CreateIpInputField(Transform parent, Vector2 anchoredPosition)
    {
        GameObject fieldObj = new GameObject("IpInputField");
        fieldObj.transform.SetParent(parent, false);

        Image bg = fieldObj.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.12f);

        RectTransform rect = fieldObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(420f, 64f);

        TMP_InputField inputField = fieldObj.AddComponent<TMP_InputField>();

        GameObject textArea = new GameObject("TextArea");
        textArea.transform.SetParent(fieldObj.transform, false);
        RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(16f, 6f);
        textAreaRect.offsetMax = new Vector2(-16f, -6f);
        textArea.AddComponent<RectMask2D>();

        TMP_Text placeholder = CreateText(textArea.transform, "Placeholder", "Enter host IP address",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.fontSize = 22;

        TMP_Text textComponent = CreateText(textArea.transform, "Text", "",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        textComponent.color = Color.white;
        textComponent.alignment = TextAlignmentOptions.Left;
        textComponent.fontSize = 22;

        inputField.textViewport = textAreaRect;
        inputField.textComponent = textComponent;
        inputField.placeholder = placeholder;
        inputField.text = "127.0.0.1";

        return inputField;
    }

    private GameObject CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);

        Image buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = ButtonBgColor;

        RectTransform rect = buttonObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = new Vector2(420f, 80f);

        Button button = buttonObj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = ButtonBgColor;
        colors.highlightedColor = ButtonHoverColor;
        colors.pressedColor = ButtonHoverColor;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        TMP_Text text = CreateText(buttonObj.transform, "Text", label,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(420f, 80f));
        text.fontSize = 26;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        return buttonObj;
    }

    private TMP_Text CreateText(Transform parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        // AbilityLoadoutUIと同じく既定は折り返し無効。複数行になり得るstatusTextのみ生成後に明示的に上書きする
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return tmp;
    }

    // ================= 状態遷移 =================

    private void ShowChoiceState()
    {
        state = UiState.Choice;
        choicePanel.SetActive(true);
        joinPanel.SetActive(false);
        cancelButtonObj.SetActive(false);
        statusText.text = "SELECT A CONNECTION MODE";
        statusText.color = NeutralTextColor;
    }

    private void OnJoinPanelOpen()
    {
        choicePanel.SetActive(false);
        joinPanel.SetActive(true);
        statusText.text = "ENTER THE HOST'S IP ADDRESS";
        statusText.color = NeutralTextColor;
    }

    private void OnJoinPanelBack()
    {
        ShowChoiceState();
    }

    private void OnHostClicked()
    {
        if (CoopNetworkManager.Instance == null)
        {
            Debug.LogError("[CoopConnectUI] CoopNetworkManager.Instance is null.");
            return;
        }

        SubscribeToNetworkEvents();
        CoopNetworkManager.Instance.StartHost(port);

        if (!CoopNetworkManager.Instance.IsNetworked)
        {
            HandleConnectionFailure("Failed to start hosting. The port may already be in use.");
            return;
        }

        state = UiState.HostWaiting;
        choicePanel.SetActive(false);
        joinPanel.SetActive(false);
        cancelButtonObj.SetActive(true);
        // まずLAN IPだけで即座に表示し、グローバルIPは非同期取得が終わってから追記する
        statusText.text = BuildHostWaitingText("fetching...");
        statusText.color = OwnerColorBlue;

        StopGlobalIpFetch();
        globalIpFetchCoroutine = StartCoroutine(FetchGlobalIpCoroutine());
    }

    private void OnConnectClicked()
    {
        if (CoopNetworkManager.Instance == null)
        {
            Debug.LogError("[CoopConnectUI] CoopNetworkManager.Instance is null.");
            return;
        }

        string address = string.IsNullOrWhiteSpace(ipInputField.text) ? "127.0.0.1" : ipInputField.text.Trim();

        SubscribeToNetworkEvents();
        bool started = CoopNetworkManager.Instance.StartClient(address, port);
        if (!started)
        {
            HandleConnectionFailure("Failed to start connecting. Please try again.");
            return;
        }

        state = UiState.ClientConnecting;
        connectStartTimeUnscaled = Time.unscaledTime;
        choicePanel.SetActive(false);
        joinPanel.SetActive(false);
        cancelButtonObj.SetActive(true);
        statusText.text = $"CONNECTING TO {address}:{port}...";
        statusText.color = OwnerColorOrange;
    }

    private void OnPlayOnThisDeviceClicked()
    {
        // ネットワークには一切触れず、既存の同一デバイス内Tab切り替え検証モードをそのまま使う
        Destroy(canvasObj);
        Destroy(this);
    }

    private void OnCancelClicked()
    {
        StopGlobalIpFetch();
        UnsubscribeFromNetworkEvents();
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.Disconnect();
        }
        ShowChoiceState();
    }

    private void SubscribeToNetworkEvents()
    {
        if (subscribedToNetworkEvents || CoopNetworkManager.Instance == null) return;
        CoopNetworkManager.Instance.OnConnectionStateChanged += HandleNetworkStateChanged;
        subscribedToNetworkEvents = true;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (!subscribedToNetworkEvents || CoopNetworkManager.Instance == null) return;
        CoopNetworkManager.Instance.OnConnectionStateChanged -= HandleNetworkStateChanged;
        subscribedToNetworkEvents = false;
    }

    private void HandleNetworkStateChanged()
    {
        if (CoopNetworkManager.Instance == null) return;
        // HostWaiting/ClientConnectingへ遷移する前（StartHost/StartClient呼び出し内から同期的に発火する分）は
        // stateがまだUiState.Choiceのため、以下の分岐はどちらも該当せず無視される（意図した挙動）。
        // このブランチは主にClientConnecting中、CoopNetworkManagerがホストからの接続拒否/切断を検知して
        // 自ら次フレームでシャットダウンした場合（pendingShutdown経由）に到達する。原因が
        // 「接続を確立できなかった」のか「拒否された」のか区別できないため、両方を示唆する文言にする
        if (!CoopNetworkManager.Instance.IsNetworked && (state == UiState.HostWaiting || state == UiState.ClientConnecting))
        {
            HandleConnectionFailure("Could not connect. The host may be unreachable or the session is full.");
            return;
        }

        if (CoopNetworkManager.Instance.IsPeerConnected && (state == UiState.HostWaiting || state == UiState.ClientConnecting))
        {
            ShowConnectedState();
        }
    }

    private void ShowConnectedState()
    {
        // state自体はHostWaiting/ClientConnectingのまま変化しないため、このタイミングで明示的に
        // 進行中のグローバルIP取得を止めておかないと、取得完了時に"CONNECTED..."の表示を上書きしてしまう
        StopGlobalIpFetch();
        UnsubscribeFromNetworkEvents();
        cancelButtonObj.SetActive(false);

        int ownerId = CoopNetworkManager.Instance.LocalOwnerId;
        if (ownerId == 0)
        {
            statusText.text = "CONNECTED — YOU ARE PLAYER 1 (BLUE)";
            statusText.color = OwnerColorBlue;
        }
        else
        {
            statusText.text = "CONNECTED — YOU ARE PLAYER 2 (ORANGE)";
            statusText.color = OwnerColorOrange;
        }

        StartCoroutine(CloseAfterDelay(1.5f));
    }

    private IEnumerator CloseAfterDelay(float seconds)
    {
        // Time.timeScaleに依存しない実時間待機（Setupフェーズがボタン待ちで停止していてもタイマーが動くように）
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < seconds)
        {
            yield return null;
        }

        if (canvasObj != null) Destroy(canvasObj);
        Destroy(this);
    }

    private void HandleConnectionFailure(string message)
    {
        StopGlobalIpFetch();
        UnsubscribeFromNetworkEvents();
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.Disconnect();
        }

        ShowChoiceState();
        statusText.text = message;
        statusText.color = ErrorColor;
    }

    // HostWaiting画面の表示文字列を一箇所に集約する。初回表示（"fetching..."）と
    // グローバルIP取得完了後の更新表示の両方からこれを呼ぶことで、文言の重複を避ける
    private string BuildHostWaitingText(string globalIpDisplay)
    {
        return "WAITING FOR PLAYER 2...\n" +
               $"Global IP (internet): {globalIpDisplay}\n" +
               $"LAN IP (same network): {GetLocalIPv4AddressesText()}\n" +
               $"Port: {port} (UDP)\n\n" +
               $"Internet play requires forwarding UDP port {port} on the host's router.";
    }

    // ホストのグローバル(公開)IPv4アドレスを外部サービス経由で非同期取得する。
    // api.ipify.orgをメインとし、失敗時のみcheckip.amazonaws.comへフォールバックする（どちらもプレーンテキストでIPのみ返す）。
    // 取得完了時にHostWaitingから既に抜けていた場合（キャンセル/接続成立/失敗等）はstatusTextを上書きしない
    private IEnumerator FetchGlobalIpCoroutine()
    {
        string globalIp = null;

        using (UnityWebRequest request = UnityWebRequest.Get("https://api.ipify.org"))
        {
            request.timeout = 5; // 実時間5秒（UnityWebRequestのタイムアウトはTime.timeScaleの影響を受けない）
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string candidate = request.downloadHandler.text.Trim();
                if (IPAddress.TryParse(candidate, out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                {
                    globalIp = candidate;
                }
            }
        }

        if (globalIp == null)
        {
            using (UnityWebRequest request = UnityWebRequest.Get("https://checkip.amazonaws.com"))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string candidate = request.downloadHandler.text.Trim();
                    if (IPAddress.TryParse(candidate, out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                    {
                        globalIp = candidate;
                    }
                }
            }
        }

        globalIpFetchCoroutine = null;

        // 取得中にCANCEL/接続成立/接続失敗等でHostWaitingを抜けていた場合は表示を上書きしない
        if (state != UiState.HostWaiting) yield break;

        statusText.text = BuildHostWaitingText(globalIp ?? "unavailable");
    }

    private void StopGlobalIpFetch()
    {
        if (globalIpFetchCoroutine != null)
        {
            StopCoroutine(globalIpFetchCoroutine);
            globalIpFetchCoroutine = null;
        }
    }

    private string GetLocalIPv4AddressesText()
    {
        try
        {
            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            List<string> addresses = new List<string>();
            foreach (IPAddress ip in hostEntry.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    addresses.Add(ip.ToString());
                }
            }

            if (addresses.Count == 0)
            {
                return "(unavailable — check your network settings)";
            }

            return string.Join("\n", addresses);
        }
        catch
        {
            return "(unavailable — check your network settings)";
        }
    }
}
