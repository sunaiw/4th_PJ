using System;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// Step 4-0b-2: クライアント→ホストのコマンド送信層（＋ホスト→クライアントの拒否トースト返送）。
// ホスト権威方式（CO-OPモード仕様書 1章）に従い、ネットワーク接続中のクライアントの「状態を変える入力」は
// すべて「要求」としてホストへ送られ、ホストが4-0aで公開済みのエントリポイント
// (TryPlaceTower / TryRespondToUnionRequest / TryActivateAbility / TryTransferCost / SetLoadout)を
// ownerId=1で呼び出して実行する。ホスト自身の入力は従来どおりエントリポイントを直接呼ぶ。
//
// 重要な設計判断:
// - ownerIdはペイロードに一切含めない。ホストは受信時の送信元clientIdから導出する
//   （ServerClientId以外=接続クライアント=ownerId 1）。クライアントが他人のownerIdを詐称する余地を作らない
// - 状態同期(4-0b-3)が未実装のため、クライアントはコマンドをローカルで実行しない（送るだけ）。
//   結果はホスト画面でのみ確認できる（暫定仕様）
// - CustomMessagingManagerはNetworkManagerがリッスン中のみ有効なため、CoopNetworkManagerの
//   OnConnectionStateChanged（開始/接続/切断/停止）のたびに「今のCustomMessagingManagerへ登録済みか」を
//   確認して登録し直す（Update()でも同じ確認を行う保険付き）。停止直前はBeforeNetworkShutdownで解除する
//
// OperatorAbilityManager/CoopNetworkManagerと同じ理由（GameManager.Start()からAddComponentで動的生成されるため）で
// SingletonBehaviour<T>は使わず、最小限の静的参照だけを持たせる
public class CoopCommandManager : MonoBehaviour
{
    public static CoopCommandManager Instance { get; private set; }

    // ===== クライアント→ホストのコマンド種別（CommandChannelNameの先頭1byte） =====
    private const byte CmdPlaceTower = 1;      // payload: byte towerType, int cellX, int cellY, int cellZ
    private const byte CmdRespondUnion = 2;    // payload: byte approve(0/1)
    private const byte CmdActivateAbility = 3; // payload: byte slotIndex, float x, float y
    private const byte CmdTransferCost = 4;    // payload: なし
    private const byte CmdSetLoadout = 5;      // payload: byte slot1Type, byte slot2Type

    // ホスト側の不正値対策。マップはこの範囲を十分に超えない（クランプではなく範囲外は破棄する）
    private const int MaxCellAbs = 200;
    private const float MaxWorldAbs = 500f;
    private const int MaxToastLength = 120;

    // ホストがリモートコマンドを実行している間だけ>=0になる（実行コンテキスト）。
    // HUDManager.ShowToast()がこれを見て、ホスト画面ではなく要求元クライアントへトーストを転送する
    private static int remoteExecutionOwnerId = -1;
    private static ulong remoteExecutionClientId = 0;

    private CustomMessagingManager registeredOn;
    private bool registeredAsHost;

    // クライアントが未接続の間にSetLoadoutが確定した場合の保留（接続成立時に送る）
    private bool hasPendingLoadout;
    private OperatorAbilityType pendingSlot1;
    private OperatorAbilityType pendingSlot2;

    // ネットワーク接続中のクライアント側かどうか。各入力ラッパーはこれがtrueなら送信、falseならローカル実行する。
    // シングルプレイ／PLAY ON THIS DEVICE／ホストはfalseのため従来どおりの挙動になる
    public static bool IsNetworkedClient
    {
        get { return CoopNetworkManager.Instance != null && CoopNetworkManager.Instance.IsNetworkedClient; }
    }

    // ホストがリモートコマンドを実行中かどうか
    public static bool IsExecutingRemoteCommand
    {
        get { return remoteExecutionOwnerId >= 0; }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.OnConnectionStateChanged += HandleConnectionStateChanged;
            CoopNetworkManager.Instance.BeforeNetworkShutdown += HandleBeforeNetworkShutdown;
        }
        EnsureRegistered();
    }

    private void OnDestroy()
    {
        if (CoopNetworkManager.Instance != null)
        {
            CoopNetworkManager.Instance.OnConnectionStateChanged -= HandleConnectionStateChanged;
            CoopNetworkManager.Instance.BeforeNetworkShutdown -= HandleBeforeNetworkShutdown;
        }
        UnregisterHandlers();
        remoteExecutionOwnerId = -1;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 状態変化イベントを取りこぼした場合の保険（CustomMessagingManagerの差し替わりを毎フレーム軽量に確認する）
        EnsureRegistered();
    }

    private void HandleConnectionStateChanged()
    {
        EnsureRegistered();
        FlushPendingLoadout();
    }

    private void HandleBeforeNetworkShutdown()
    {
        UnregisterHandlers();
    }

    // ===== ハンドラ登録 =====

    private void EnsureRegistered()
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        CustomMessagingManager current = net != null ? net.Messaging : null;

        if (current == registeredOn) return;

        // 別のセッション（旧マネージャは破棄済み）または停止状態へ変わった。古い参照は捨てる
        registeredOn = null;
        if (current == null) return;

        registeredAsHost = net.IsHostAuthority;
        if (registeredAsHost)
        {
            current.RegisterNamedMessageHandler(CoopNetworkManager.CommandChannelName, HandleCommandMessage);
        }
        else
        {
            current.RegisterNamedMessageHandler(CoopNetworkManager.FeedbackChannelName, HandleFeedbackMessage);
        }
        registeredOn = current;
        Debug.Log($"[CoopCommandManager] Registered {(registeredAsHost ? "command" : "feedback")} handler.");
    }

    private void UnregisterHandlers()
    {
        if (registeredOn == null) return;
        try
        {
            registeredOn.UnregisterNamedMessageHandler(registeredAsHost
                ? CoopNetworkManager.CommandChannelName
                : CoopNetworkManager.FeedbackChannelName);
        }
        catch (Exception e)
        {
            // 既に停止処理が進んでいる場合は解除に失敗しうるが、マネージャごと破棄されるため無害
            Debug.LogWarning($"[CoopCommandManager] Failed to unregister handler: {e.Message}");
        }
        registeredOn = null;
    }

    // ===== クライアント側: 送信API =====

    // 送信可能か。ネットワーククライアントで、かつホストへ接続済みの場合のみtrue。
    // 未接続での操作は黙って無視せずトーストで知らせる
    private bool CanSend(bool showToast = true)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworkedClient) return false;
        if (!net.IsPeerConnected || net.Messaging == null)
        {
            if (showToast && HUDManager.Instance != null) HUDManager.Instance.ShowToast("Not connected to host");
            return false;
        }
        return true;
    }

    public void SendPlaceTower(TowerType type, Vector3Int cellPos)
    {
        if (!CanSend()) return;
        using (FastBufferWriter w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteValueSafe(CmdPlaceTower);
            w.WriteValueSafe((byte)type);
            w.WriteValueSafe(cellPos.x);
            w.WriteValueSafe(cellPos.y);
            w.WriteValueSafe(cellPos.z);
            SendToHost(w);
        }
    }

    public void SendRespondUnion(bool approve)
    {
        // F/Gはホスト側で検証される。ペンディングが無い時に押されても無害なので、未接続時のトーストは出さない
        if (!CanSend(false)) return;
        using (FastBufferWriter w = new FastBufferWriter(8, Allocator.Temp))
        {
            w.WriteValueSafe(CmdRespondUnion);
            w.WriteValueSafe((byte)(approve ? 1 : 0));
            SendToHost(w);
        }
    }

    public void SendActivateAbility(int slotIndex, Vector3 castPosition)
    {
        if (!CanSend()) return;
        using (FastBufferWriter w = new FastBufferWriter(16, Allocator.Temp))
        {
            w.WriteValueSafe(CmdActivateAbility);
            w.WriteValueSafe((byte)Mathf.Clamp(slotIndex, 0, 1));
            w.WriteValueSafe(castPosition.x);
            w.WriteValueSafe(castPosition.y);
            SendToHost(w);
        }
    }

    public void SendTransferCost()
    {
        if (!CanSend()) return;
        using (FastBufferWriter w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe(CmdTransferCost);
            SendToHost(w);
        }
    }

    // AbilityLoadoutUIから呼ばれる。接続済みなら即送信、未接続なら保留して接続成立時に送る
    public void SendSetLoadout(OperatorAbilityType slot1, OperatorAbilityType slot2)
    {
        pendingSlot1 = slot1;
        pendingSlot2 = slot2;
        hasPendingLoadout = true;
        FlushPendingLoadout();
    }

    private void FlushPendingLoadout()
    {
        if (!hasPendingLoadout || !CanSend(false)) return;
        using (FastBufferWriter w = new FastBufferWriter(8, Allocator.Temp))
        {
            w.WriteValueSafe(CmdSetLoadout);
            w.WriteValueSafe((byte)pendingSlot1);
            w.WriteValueSafe((byte)pendingSlot2);
            SendToHost(w);
        }
        hasPendingLoadout = false;
    }

    private void SendToHost(FastBufferWriter writer)
    {
        CustomMessagingManager msg = CoopNetworkManager.Instance.Messaging;
        if (msg == null) return;
        msg.SendNamedMessage(CoopNetworkManager.CommandChannelName, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    // ===== ホスト側: 受信と実行 =====

    private void HandleCommandMessage(ulong senderClientId, FastBufferReader reader)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsHostAuthority) return;

        // ホスト自身(ServerClientId)から届くことは無い。届いても無視する。
        // ownerIdは送信元から導出する（ペイロードは信用しない）: ServerClientId以外=接続クライアント=1
        if (senderClientId == NetworkManager.ServerClientId) return;
        const int senderOwnerId = 1;

        try
        {
            reader.ReadValueSafe(out byte cmd);

            remoteExecutionOwnerId = senderOwnerId;
            remoteExecutionClientId = senderClientId;
            try
            {
                switch (cmd)
                {
                    case CmdPlaceTower: ExecutePlaceTower(reader, senderOwnerId); break;
                    case CmdRespondUnion: ExecuteRespondUnion(reader, senderOwnerId); break;
                    case CmdActivateAbility: ExecuteActivateAbility(reader, senderOwnerId); break;
                    case CmdTransferCost: ExecuteTransferCost(senderOwnerId); break;
                    case CmdSetLoadout: ExecuteSetLoadout(reader, senderOwnerId); break;
                    default:
                        Debug.LogWarning($"[CoopCommandManager] Unknown command type {cmd} from client {senderClientId}. Ignored.");
                        break;
                }
            }
            finally
            {
                // 例外が出てもコンテキストを必ず戻す。残るとホスト自身のトーストが全てクライアントへ流れてしまう
                remoteExecutionOwnerId = -1;
            }
        }
        catch (Exception e)
        {
            // 短すぎる/壊れたペイロード（ReadValueSafeがOverflowException等を投げる）は破棄する
            Debug.LogWarning($"[CoopCommandManager] Malformed command from client {senderClientId}: {e.Message}");
        }
    }

    private void ExecutePlaceTower(FastBufferReader reader, int ownerId)
    {
        reader.ReadValueSafe(out byte typeByte);
        reader.ReadValueSafe(out int x);
        reader.ReadValueSafe(out int y);
        reader.ReadValueSafe(out int z);

        if (!Enum.IsDefined(typeof(TowerType), (int)typeByte))
        {
            Debug.LogWarning($"[CoopCommandManager] PlaceTower: invalid tower type {typeByte}. Ignored.");
            return;
        }
        if (Mathf.Abs(x) > MaxCellAbs || Mathf.Abs(y) > MaxCellAbs || z != 0)
        {
            Debug.LogWarning($"[CoopCommandManager] PlaceTower: cell ({x},{y},{z}) out of range. Ignored.");
            return;
        }
        if (TowerManager.Instance == null || GameManager.Instance == null) return;

        TowerType type = (TowerType)typeByte;

        // StartDragPlacement()（クライアントのローカル入力側）が行っているアンロックWave判定は
        // TryPlaceTower()には無いため、ホストで受ける際はここで検証する（クライアントのローカルWaveは当てにならない）
        TowerDefinition def = TowerManager.Instance.GetDefinition(type);
        if (def == null)
        {
            Debug.LogWarning($"[CoopCommandManager] PlaceTower: no definition for {type}. Ignored.");
            return;
        }
        if (GameManager.Instance.CurrentWave < def.unlockWave)
        {
            Debug.Log($"[CoopCommandManager] PlaceTower: {type} locked until Wave {def.unlockWave}.");
            if (HUDManager.Instance != null) HUDManager.Instance.ShowToast($"{def.displayName} unlocks at Wave {def.unlockWave}");
            return;
        }

        TowerManager.Instance.TryPlaceTower(type, new Vector3Int(x, y, 0), ownerId);
    }

    private void ExecuteRespondUnion(FastBufferReader reader, int ownerId)
    {
        reader.ReadValueSafe(out byte approveByte);
        if (approveByte > 1) return;
        if (TowerManager.Instance == null) return;
        TowerManager.Instance.TryRespondToUnionRequest(ownerId, approveByte == 1);
    }

    private void ExecuteActivateAbility(FastBufferReader reader, int ownerId)
    {
        reader.ReadValueSafe(out byte slot);
        reader.ReadValueSafe(out float x);
        reader.ReadValueSafe(out float y);

        if (slot > 1) return;
        if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return;
        if (Mathf.Abs(x) > MaxWorldAbs || Mathf.Abs(y) > MaxWorldAbs) return;
        if (OperatorAbilityManager.Instance == null) return;

        OperatorAbilityManager.Instance.TryActivateAbility(ownerId, slot, new Vector3(x, y, 0f));
    }

    private void ExecuteTransferCost(int ownerId)
    {
        if (GameManager.Instance == null) return;
        GameManager.Instance.TryTransferCost(ownerId);
    }

    private void ExecuteSetLoadout(FastBufferReader reader, int ownerId)
    {
        reader.ReadValueSafe(out byte a);
        reader.ReadValueSafe(out byte b);

        if (!Enum.IsDefined(typeof(OperatorAbilityType), (int)a) || !Enum.IsDefined(typeof(OperatorAbilityType), (int)b))
        {
            Debug.LogWarning($"[CoopCommandManager] SetLoadout: invalid ability type ({a},{b}). Ignored.");
            return;
        }
        if (OperatorAbilityManager.Instance == null || GameManager.Instance == null) return;

        // 10章未決事項#3「試合中は固定」: Wave 1のSetup（まだ開始前）以外では受け付けない
        if (GameManager.Instance.CurrentWave > 1 || GameManager.Instance.CurrentPhase != GamePhase.Setup)
        {
            Debug.Log("[CoopCommandManager] SetLoadout rejected: the match has already started.");
            if (HUDManager.Instance != null) HUDManager.Instance.ShowToast("Ability loadout is locked");
            return;
        }

        OperatorAbilityManager.Instance.SetLoadout(ownerId, (OperatorAbilityType)a, (OperatorAbilityType)b);
    }

    // ===== ホスト→クライアント: フィードバック（トースト）返送 =====

    // HUDManager.ShowToast()の先頭から呼ばれる。ホストがリモートコマンドを実行中なら、
    // トーストはホスト画面ではなく要求元クライアントへ転送し、trueを返してローカル表示を抑止する
    public static bool TryRedirectToast(string message)
    {
        if (remoteExecutionOwnerId < 0) return false;
        if (Instance != null) Instance.SendFeedbackToast(remoteExecutionClientId, message);
        return true;
    }

    // ホスト起点の通知を接続中のクライアントへ送る（例: ホストのUnion要求をクライアントへ知らせる）。
    // クライアント側にはまだバナーを出す状態同期(4-0b-3)が無いための暫定手段。
    // ホストでないか、相手未接続の場合は何もしない
    public static void NotifyClientToast(string message)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (Instance == null || net == null || !net.IsHostAuthority || !net.IsPeerConnected) return;
        ulong clientId = net.PeerClientId;
        if (clientId == NetworkManager.ServerClientId) return;
        Instance.SendFeedbackToast(clientId, message);
    }

    private void SendFeedbackToast(ulong clientId, string message)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsHostAuthority || net.Messaging == null || string.IsNullOrEmpty(message)) return;
        if (message.Length > MaxToastLength) message = message.Substring(0, MaxToastLength);

        try
        {
            // UTF-8換算で最大 MaxToastLength*3 + 長さプレフィックス
            using (FastBufferWriter w = new FastBufferWriter(MaxToastLength * 3 + 16, Allocator.Temp))
            {
                w.WriteValueSafe(message);
                net.Messaging.SendNamedMessage(CoopNetworkManager.FeedbackChannelName, clientId, w, NetworkDelivery.ReliableSequenced);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CoopCommandManager] Failed to send feedback toast: {e.Message}");
        }
    }

    // クライアント側: ホストからのトースト。HUDManager.ShowToast()経由で表示する
    // （クライアントでは実行コンテキストが常に無いため転送されずローカル表示になる）
    private void HandleFeedbackMessage(ulong senderClientId, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out string message);
            if (string.IsNullOrEmpty(message)) return;
            if (message.Length > MaxToastLength) message = message.Substring(0, MaxToastLength);
            if (HUDManager.Instance != null) HUDManager.Instance.ShowToast(message);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CoopCommandManager] Malformed feedback message: {e.Message}");
        }
    }
}
