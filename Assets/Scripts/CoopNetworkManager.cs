using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

// Step 4-0b-1: CO-OP用LAN接続層。このステップのスコープは「接続の確立のみ」であり、
// ゲームプレイ状態の同期・コマンド送信は一切行わない（4-0b-2/4-0b-3で実装予定）。
// NGO(Netcode for GameObjects) 2.8.2 + UnityTransportを完全にコードから生成し、
// シーン・Prefabには一切手を加えない。固定2人（ホスト=Player1/Blue, 接続クライアント=Player2/Orange）
// のCO-OP専用設計であり、3人目以降の接続は承認拒否する。
//
// OperatorAbilityManagerと同じ理由（GameManager.Start()からAddComponentで動的生成されるため）で
// SingletonBehaviour<T>は使わず、最小限の静的参照だけを持たせる
public class CoopNetworkManager : MonoBehaviour
{
    public static CoopNetworkManager Instance { get; private set; }

    // Step 4-0b-2/4-0b-3で使うメッセージチャンネル名の予約。このステップでは登録・送信は一切行わない
    // （CustomMessageManager.RegisterNamedMessageHandler / SendNamedMessage の名称として後続ステップが使う）
    public const string CommandChannelName = "Coop_ClientToHost_Command"; // 4-0b-2: クライアント→ホストのコマンド送信用（予約のみ）
    public const string StateSnapshotChannelName = "Coop_HostToClient_StateSnapshot"; // 4-0b-3: ホスト→クライアントの10Hz状態スナップショット用（予約のみ）

    // ホスト=0(Blue)、接続クライアント=1(Orange)。未接続時は-1
    public int LocalOwnerId { get; private set; } = -1;

    // ホストまたはクライアントとして起動済み（StartHost/StartClientが成功した）かどうか
    public bool IsNetworked { get; private set; } = false;

    // ホストとして起動している場合のみtrue（シミュレーションの権威を持つ側）
    public bool IsHostAuthority { get; private set; } = false;

    // 相手側プレイヤーが実際に接続（承認）済みかどうか。ホスト側は相手クライアントの接続、
    // クライアント側は自分自身の接続承認完了で true になる
    public bool IsPeerConnected { get; private set; } = false;

    // 接続/切断/シャットダウンのたびに発火。CoopConnectUI・GameManager等のUI/ゲーム側が購読する
    public event Action OnConnectionStateChanged;

    private NetworkManager networkManager;
    private GameObject networkManagerObj;

    // クライアントとして動作中にホストから切断された（接続拒否・満員・ホスト側切断など理由は問わない）ことを
    // HandleClientDisconnected()内で検知した際に立てるフラグ。NGOのコールバックスタックの中から直接
    // ShutdownNetworkInternal()を呼ぶと危険なため、フラグだけ立てて実際のシャットダウンは次フレームの
    // Update()で行う
    private bool pendingShutdown = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (!pendingShutdown) return;

        Debug.Log("[CoopNetworkManager] Executing deferred shutdown after client-side disconnect.");
        ShutdownNetworkInternal();
        OnConnectionStateChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // Play Mode終了時やシーン再読み込み時にNetworkManagerのGameObjectを残さないよう、確実に破棄する
        ShutdownNetworkInternal();
    }

    // ホストとして起動する。呼び出し元(CoopConnectUI)がStartHost成功後の待機UI表示を担当する
    public void StartHost(ushort port)
    {
        if (IsNetworked)
        {
            Debug.LogWarning("[CoopNetworkManager] Already networked. Ignoring StartHost().");
            return;
        }

        CreateNetworkManagerObject();

        UnityTransport transport = networkManagerObj.GetComponent<UnityTransport>();
        // "0.0.0.0"を渡すと、SetConnectionData内部でServerListenAddressも同じ値にフォールバックするため、
        // ホストは特定のNICに縛られず全アドレスで待ち受ける（LAN内のどのIPからでも接続を受け付けられる）
        transport.SetConnectionData("0.0.0.0", port);

        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        bool started = networkManager.StartHost();
        if (started)
        {
            IsNetworked = true;
            IsHostAuthority = true;
            LocalOwnerId = 0; // ホスト = Player 1 (Blue)
            Debug.Log($"[CoopNetworkManager] Host started on port {port}.");
        }
        else
        {
            Debug.LogError("[CoopNetworkManager] Failed to start host (transport error, e.g. port already in use).");
            DestroyNetworkManagerObject();
        }

        OnConnectionStateChanged?.Invoke();
    }

    // クライアントとして指定アドレスへ接続を試みる。戻り値はトランスポートの起動可否であり、
    // 実際にホストへ接続・承認されたかどうかはIsPeerConnected / OnConnectionStateChangedで判定すること
    public bool StartClient(string address, ushort port)
    {
        if (IsNetworked)
        {
            Debug.LogWarning("[CoopNetworkManager] Already networked. Ignoring StartClient().");
            return false;
        }

        CreateNetworkManagerObject();

        UnityTransport transport = networkManagerObj.GetComponent<UnityTransport>();
        transport.SetConnectionData(address, port);

        // クライアント側ではConnectionApprovalCallbackはNGOから呼ばれないが、対称性のため設定だけしておく
        networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        bool started = networkManager.StartClient();
        if (started)
        {
            IsNetworked = true;
            IsHostAuthority = false;
            LocalOwnerId = 1; // 接続クライアント = Player 2 (Orange)。固定2人構成のため接続完了を待たず確定できる
            Debug.Log($"[CoopNetworkManager] Client connecting to {address}:{port}...");
        }
        else
        {
            Debug.LogError("[CoopNetworkManager] Failed to start client (transport error).");
            DestroyNetworkManagerObject();
        }

        OnConnectionStateChanged?.Invoke();
        return started;
    }

    // ユーザーが接続待ち/接続中の状態から手動で中断するためのAPI（例: CoopConnectUIのBACK/CANCELボタン）。
    // 試合中の切断からの復旧ではなく、接続確立前の単純な取り消しのみを想定する
    public void Disconnect()
    {
        if (!IsNetworked) return;
        Debug.Log("[CoopNetworkManager] Disconnect() called.");
        ShutdownNetworkInternal();
        OnConnectionStateChanged?.Invoke();
    }

    // 固定2人（ホスト+クライアント1人）構成のCO-OP設計: ホスト自身の接続は仕様上拒否できない
    // （NetworkManager.HostServerInitializeがApproved==falseでも警告を出して自動承認する）ため常にApprove、
    // 2人目以降の外部クライアントは接続済み人数で判定し、1人だけ承認・以降は拒否する
    private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        // PlayerPrefabを使わない設計のため、Player Objectは生成しない
        response.CreatePlayerObject = false;

        if (request.ClientNetworkId == NetworkManager.ServerClientId)
        {
            response.Approved = true;
            return;
        }

        int connectedCount = networkManager.ConnectedClientsIds.Count;
        response.Approved = connectedCount < 2;
        if (!response.Approved)
        {
            response.Reason = "This CO-OP session is full (2 players max).";
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null) return;

        if (networkManager.IsHost)
        {
            // ホスト自身の接続完了イベント（ClientId==ServerClientId）は無視し、相手クライアントの接続のみを扱う
            if (clientId == NetworkManager.ServerClientId) return;
            IsPeerConnected = true;
            Debug.Log($"[CoopNetworkManager] Player 2 (Orange) connected. NGO clientId={clientId}");
        }
        else
        {
            // クライアント側では自分自身の接続確立イベントのみ発火する
            IsPeerConnected = true;
            Debug.Log("[CoopNetworkManager] Connected to host as Player 2 (Orange).");
        }

        OnConnectionStateChanged?.Invoke();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager == null) return;

        if (IsHostAuthority)
        {
            // ホスト自身の接続に対する切断通知(clientId==ServerClientId)は無視し、相手クライアントの切断のみを扱う
            if (clientId == NetworkManager.ServerClientId) return;

            Debug.LogWarning($"[CoopNetworkManager] Peer disconnected (NGO clientId={clientId}).");
            IsPeerConnected = false;
            // ホスト側はシャットダウンせず待機状態に戻す。別のクライアントの接続を引き続き受け付けられるようにするため。
            // TODO: 試合中の切断からの再接続・ゲーム状態復旧はStep 4-0b-1のスコープ外。
            // 現時点では切断を検知してイベントを発火するのみに留める（後続ステップで対応）
            OnConnectionStateChanged?.Invoke();
        }
        else
        {
            // クライアント側: ホストから切断された（接続承認の拒否・セッション満員・ホスト側切断など理由は問わない）。
            // NGOはこの直後に自身でNetworkManager.Shutdown()を予約するが、こちら側の状態(IsNetworked等)や
            // 動的生成したNetworkManagerObjectの破棄は自前で行う必要がある。ただし、このメソッドはNGOの
            // コールバックスタックの中から呼ばれているため、ここで直接ShutdownNetworkInternal()
            // (networkManager.Shutdown()を含む)を呼ぶのは危険。フラグを立てるだけに留め、
            // 実際のシャットダウンはUpdate()で次フレームに行う
            Debug.LogWarning($"[CoopNetworkManager] Disconnected from host (NGO clientId={clientId}).");
            IsPeerConnected = false;
            pendingShutdown = true;
        }
    }

    private void CreateNetworkManagerObject()
    {
        networkManagerObj = new GameObject("NetworkManager");
        networkManager = networkManagerObj.AddComponent<NetworkManager>();
        UnityTransport transport = networkManagerObj.AddComponent<UnityTransport>();

        networkManager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            // このプロジェクトは敵を10Hzのスナップショット配列として同期する設計であり、NetworkObjectを
            // 一切スポーンしない。そのためPlayerPrefabはnullのままにする（デフォルトのプレイヤーオブジェクト生成は不要）
            PlayerPrefab = null,
            // シーン遷移は既存どおりSceneManager.LoadSceneで行い、NGOのネットワークシーン管理は使わない
            // （両者とも同じMainGameシーンで別々にゲームロジックを実行するだけで、NGO側でのシーン同期は不要）
            EnableSceneManagement = false,
            // 固定2人(ホスト+クライアント1人)構成のCO-OP設計のため、接続承認を有効にして人数を制御する
            ConnectionApproval = true,
        };
    }

    private void DestroyNetworkManagerObject()
    {
        if (networkManagerObj != null)
        {
            Destroy(networkManagerObj);
        }
        networkManager = null;
        networkManagerObj = null;
    }

    private void ShutdownNetworkInternal()
    {
        // OnDestroy()やDisconnect()から直接呼ばれた場合も含め、保留中のシャットダウン要求は必ずクリアする
        pendingShutdown = false;

        if (networkManager != null)
        {
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
        }

        DestroyNetworkManagerObject();

        IsNetworked = false;
        IsHostAuthority = false;
        IsPeerConnected = false;
        LocalOwnerId = -1;
    }
}
