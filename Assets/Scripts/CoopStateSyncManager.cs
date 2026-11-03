using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// Step 4-0b-3: ホスト→クライアントの状態同期層。
// ホスト権威方式（CO-OPモード仕様書 1章）に従い、ゲームを実際にシミュレーションするのはホストだけで、
// クライアントはホストから届く状態を「ミラー」として盤面に映すだけの描画専用インスタンスになる。
//
// 同期は2本のNamed Messageチャンネルで行う:
// - StateSnapshotChannelName: ホストが0.1秒ごと(Time.unscaledTime基準＝ポーズ/ゲーム速度の影響を受けない)に
//   「全状態スナップショット」を1メッセージで送る。
// - EventChannelName: 発射・コンボ成立・Outpost破壊警告・Siege Marker予告の単発イベント。
//   ホストがフレーム中に積み、LateUpdateで1回にまとめて送る。
//
// 重要な設計判断:
// - スナップショットは「差分」ではなく毎回の全状態。クライアントは「IDが載っていない＝消えた」と解釈して
//   プロキシを破棄するため、全スナップショットが欠けず順序通りに届く必要がある。
//   仕様書1章の「敵は10Hz・half・非信頼」案から次の2点を意図的に変えている:
//   (1) 配送は ReliableFragmentedSequenced（信頼・順序保証・断片化可）。敵80体+タワーで1メッセージがMTUを超えるため断片化が必須で、
//       取りこぼし＝誤ってプロキシが消える/残る不具合になるため信頼配送にした。LAN/一般的な回線の10Hz・数KBなら帯域は問題にならない。
//   (2) 座標・HPは half ではなく float。実装の単純さを優先した（1体あたり約21バイトで見積もりの約2倍だが実用範囲）
// - 先頭1バイトのフォーマットバージョンを持つ。不一致のスナップショットは丸ごと破棄する
// - クライアントは受信内容を全て検証し（enum範囲・座標範囲・NaN/Infinity・件数上限）、不正なら
//   スナップショット全体を破棄する（部分適用しない）
// - 供給ネットワークは同期しない。クライアントはタワーのプロキシがRegisterTowerされるたびに
//   TowerManager.RecalculateSupplyNetwork()をローカルで再計算する（仕様書1章）
// - 弾は同期しない。発射イベントはOnRemoteTowerFiredとして公開するだけで、ローカル弾の生成は4-0b-4が担当する
//
// CoopCommandManagerと同じ理由（GameManager.Start()からAddComponentで動的生成されるため）で
// SingletonBehaviour<T>は使わず、最小限の静的参照だけを持たせる
public class CoopStateSyncManager : MonoBehaviour
{
    public static CoopStateSyncManager Instance { get; private set; }

    // ===== 発射イベントのeffectKind（ビットフラグ。Tower.Shoot()でBulletEffectsから導出） =====
    public const byte EffectSplash = 1;   // SplashRadius > 0（範囲攻撃）
    public const byte EffectSlow = 2;     // FrostSlowPercent > 0（スロウ付与）
    public const byte EffectPiercing = 4; // PiercingEnabled（貫通）

    // クライアントのローカル弾演出用（Step 4-0b-4: CoopClientVisualsが購読する）。引数: (発射元タワーID, 目標敵ID, effectKind)。
    // IDは各プロキシのNetworkIdと対応する（対応するプロキシがまだ無い/既に消えている場合もあるので購読側で確認すること）
    public event Action<ushort, ushort, byte> OnRemoteTowerFired;

    // ホストが「相手クライアントへ状態を送れる状態」ならtrue。Tower.Shoot()等のホスト側フックは
    // 計算を始める前にこのstaticフラグだけを確認する（シングル/PLAY ON THIS DEVICE/クライアントでは常にfalseで追加コストなし）
    public static bool HostSyncActive { get; private set; }

    // ===== プロトコル定数 =====
    private const byte SnapshotFormatVersion = 2; // v2: 末尾に報酬提示（Step 4-8）を追加
    private const float SnapshotInterval = 0.1f; // 10Hz。Time.unscaledTimeで測る
    private const int SnapshotInitialSize = 4096;
    private const int SnapshotMaxSize = 65536;

    // イベント種別（EventChannelNameメッセージ内の各イベントの先頭1byte）
    private const byte EvtFire = 1;          // ushort towerNetId, ushort enemyNetId, byte effectKind
    private const byte EvtCombo = 2;         // string comboName, float x, float y（Step 4-0b-4: 発動位置を追加）
    private const byte EvtOutpostDown = 3;   // byte ownerId
    private const byte EvtSiegeInbound = 4;  // byte ownerId, byte outpostNumber

    // タワーのflags
    private const byte TowerFlagOffline = 1;
    private const byte TowerFlagRequiresSupply = 2;
    private const byte TowerFlagSiegeMarked = 4;
    private const byte TowerFlagOvercharged = 8;
    private const byte TowerFlagReinforced = 16;

    // 敵のspecial flags / status flags
    private const byte EnemySpecialBoss = 1;
    private const byte EnemySpecialBuster = 2;
    private const byte EnemySpecialSiege = 4;
    private const byte EnemyStatusSlowed = 1;
    private const byte EnemyStatusTaunted = 2;

    // 受信側の検証上限（これを超える/範囲外の値は不正としてスナップショットごと破棄する）
    private const int MaxCellAbs = 200;
    private const float MaxWorldAbs = 500f;
    private const int MaxTowers = 1000;
    private const int MaxEnemies = 2000;
    private const int MaxTableEntries = 32; // 設置数/報酬テーブルの最大エントリ数
    private const int MaxRewardOffers = 3;
    private const int MaxComboNameLength = 24;

    // イベント: 1メッセージあたりの件数上限と、1フレームに積める非Fireイベントの上限。
    // ReliableSequenced（非断片化）で安全に収まるよう、最悪ケース（コンボ名24文字×4件 + Fire 44件）でも
    // 約600バイト（コンボは位置8バイト追加で1件約40バイト）に収まる値にしている
    private const int MaxEventsPerMessage = 48;
    private const int MaxPendingEvents = 512;
    private const int MaxNonFireEventsPerFlush = 4;

    private static readonly TowerType[] AllTowerTypes = (TowerType[])Enum.GetValues(typeof(TowerType));
    private static readonly RewardType[] AllRewardTypes = (RewardType[])Enum.GetValues(typeof(RewardType));

    // ===== ホスト側: イベントキュー（staticなので、各フックはInstanceを経由せず呼べる） =====
    private struct PendingEvent
    {
        public byte type;
        public ushort a;
        public ushort b;
        public byte c;
        public string text;
        public float x; // Step 4-0b-4: Comboの発動位置
        public float y;
    }

    private static readonly List<PendingEvent> pendingEvents = new List<PendingEvent>(64);
    private static int pendingNonFireCount = 0;

    // Tower.Shoot()から呼ばれる。HostSyncActiveの確認は呼び出し側でも行うが、ここでも保険として確認する
    public static void NotifyTowerFired(ushort towerNetId, ushort enemyNetId, byte effectKind)
    {
        if (!HostSyncActive || pendingEvents.Count >= MaxPendingEvents) return;
        pendingEvents.Add(new PendingEvent { type = EvtFire, a = towerNetId, b = enemyNetId, c = effectKind });
    }

    // OperatorAbilityManagerでSync Comboが成立した時に呼ばれる。Step 4-0b-4: クライアントが同じ位置にフラッシュを出せるよう発動位置も送る
    public static void NotifyComboTriggered(string comboName, Vector3 castPosition)
    {
        if (!HostSyncActive || string.IsNullOrEmpty(comboName)) return;
        if (pendingNonFireCount >= MaxNonFireEventsPerFlush || pendingEvents.Count >= MaxPendingEvents) return;
        if (comboName.Length > MaxComboNameLength) comboName = comboName.Substring(0, MaxComboNameLength);
        pendingNonFireCount++;
        pendingEvents.Add(new PendingEvent { type = EvtCombo, text = comboName, x = castPosition.x, y = castPosition.y });
    }

    // Tower.Die()でOutpostが破壊された時に呼ばれる
    public static void NotifyOutpostDown(int ownerId)
    {
        if (!HostSyncActive) return;
        if (pendingNonFireCount >= MaxNonFireEventsPerFlush || pendingEvents.Count >= MaxPendingEvents) return;
        pendingNonFireCount++;
        pendingEvents.Add(new PendingEvent { type = EvtOutpostDown, c = (byte)Mathf.Clamp(ownerId, 0, 1) });
    }

    // Enemy.SetupSiegeMarker()でOutpostをマークした時に呼ばれる
    public static void NotifySiegeInbound(int ownerId, int outpostNumber)
    {
        if (!HostSyncActive) return;
        if (pendingNonFireCount >= MaxNonFireEventsPerFlush || pendingEvents.Count >= MaxPendingEvents) return;
        pendingNonFireCount++;
        pendingEvents.Add(new PendingEvent
        {
            type = EvtSiegeInbound,
            a = (ushort)Mathf.Clamp(ownerId, 0, 1),
            c = (byte)Mathf.Clamp(outpostNumber, 0, 255),
        });
    }

    // ===== クライアント側: プロキシの取得（Step 4-0b-4: CoopClientVisualsの弾演出用） =====
    public bool TryGetTowerProxy(ushort netId, out Tower tower)
    {
        tower = null;
        if (!towerProxies.TryGetValue(netId, out TowerProxyRecord record) || record.tower == null) return false;
        tower = record.tower;
        return true;
    }

    public bool TryGetEnemyProxy(ushort netId, out Enemy enemy)
    {
        enemy = null;
        if (!enemyProxies.TryGetValue(netId, out Enemy found) || found == null) return false;
        enemy = found;
        return true;
    }

    // ===== 登録状態 =====
    private CustomMessagingManager registeredOn;
    private bool registeredAsClient;

    private float nextSnapshotTime = 0f;

    // ===== クライアント側: プロキシの対応表（ホストのネットワークID → ローカルのプロキシ） =====
    private struct TowerProxyRecord
    {
        public Tower tower;
        public TowerType type;
        public Vector3Int cell;
        public int ownerId;
    }

    private readonly Dictionary<ushort, TowerProxyRecord> towerProxies = new Dictionary<ushort, TowerProxyRecord>();
    private readonly Dictionary<ushort, Enemy> enemyProxies = new Dictionary<ushort, Enemy>();
    private readonly HashSet<ushort> seenIds = new HashSet<ushort>();
    private readonly List<ushort> staleIds = new List<ushort>();

    // ===== クライアント側: 受信スナップショットの検証済みバッファ（毎回使い回してGCを避ける） =====
    private struct TowerEntry
    {
        public ushort id;
        public TowerType type;
        public Vector3Int cell;
        public int ownerId;
        public int outpostNumber;
        public int placedWave;
        public float hp;
        public byte flags;
    }

    private struct EnemyEntry
    {
        public ushort id;
        public byte kind;
        public byte special;
        public Vector3 position;
        public float hp;
        public float maxHp;
        public byte status;
    }

    private GamePhase snapPhase;
    private int snapWave, snapLife, snapInitialLife, snapCost;
    private int snapPersonal0, snapPersonal1, snapUnionPower, snapTransferRemaining;
    private int snapKills0, snapKills1, snapMapExpandedWave;
    private bool snapShield;
    private readonly int[] snapPlacedCounts = new int[MaxTableEntries];
    private bool snapUnionHas;
    private TowerType snapUnionType;
    private int snapUnionRequester;
    private Vector3Int snapUnionCell;
    private float snapUnionSeconds;
    private readonly OperatorAbilityType[] snapLoadouts = new OperatorAbilityType[4];
    private readonly float[] snapCooldowns = new float[4];
    private readonly int[] snapRewardCounts = new int[MaxTableEntries];
    private readonly List<TowerEntry> snapTowers = new List<TowerEntry>(64);
    private readonly List<EnemyEntry> snapEnemies = new List<EnemyEntry>(128);
    // Step 4-8: 報酬提示（Rewardフェーズ中のみ）
    private bool snapHasRewardOffer;
    private int snapRewardWave, snapRewardChooser, snapRewardHover, snapRewardOfferCount;
    private readonly RewardType[] snapRewardOfferTypes = new RewardType[MaxRewardOffers];
    private readonly int[] snapRewardOfferValues = new int[MaxRewardOffers];

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

        // staticなキュー/フラグはシーン再読み込み後に持ち越さない
        HostSyncActive = false;
        pendingEvents.Clear();
        pendingNonFireCount = 0;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 状態変化イベントを取りこぼした場合の保険（CustomMessagingManagerの差し替わりを毎フレーム軽量に確認する）
        EnsureRegistered();

        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null)
        {
            SetHostSyncActive(false);
            return;
        }

        // クライアント: 接続済みになった時点でミラーモードへ移行する（初回スナップショット受信でも移行するが、
        // 接続直後からローカルのゲーム進行を止めておくため、ここでも毎フレーム確認する）
        if (net.IsNetworkedClient)
        {
            SetHostSyncActive(false);
            TryEnterMirrorMode();
            return;
        }

        // ホスト: 相手が接続中でメッセージ送信可能な間だけ同期する
        bool canSend = net.IsHostAuthority && net.IsPeerConnected
            && net.PeerClientId != NetworkManager.ServerClientId && net.Messaging != null;
        if (canSend && !HostSyncActive)
        {
            // 接続直後はすぐ全状態を送る（クライアントが最初の盤面を待たされないように）
            nextSnapshotTime = 0f;
        }
        SetHostSyncActive(canSend);
        if (!canSend) return;

        if (Time.unscaledTime < nextSnapshotTime) return;
        nextSnapshotTime = Time.unscaledTime + SnapshotInterval;
        SendSnapshot(net);
    }

    private void LateUpdate()
    {
        if (pendingEvents.Count == 0) return;

        // 積んだ後に切断された等で送れない場合は破棄する
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (!HostSyncActive || net == null || net.Messaging == null)
        {
            pendingEvents.Clear();
            pendingNonFireCount = 0;
            return;
        }

        FlushEvents(net);
    }

    private void SetHostSyncActive(bool active)
    {
        if (HostSyncActive == active) return;
        HostSyncActive = active;
        if (!active)
        {
            pendingEvents.Clear();
            pendingNonFireCount = 0;
        }
    }

    private void HandleConnectionStateChanged()
    {
        EnsureRegistered();
        TryEnterMirrorMode();
    }

    private void HandleBeforeNetworkShutdown()
    {
        UnregisterHandlers();
        SetHostSyncActive(false);
    }

    private void TryEnterMirrorMode()
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworkedClient || !net.IsPeerConnected) return;
        if (GameManager.Instance == null || GameManager.Instance.IsClientMirror) return;
        GameManager.Instance.EnterClientMirrorMode();
    }

    // ===== ハンドラ登録（クライアントのみ。ホストは送信専用で何も登録しない） =====

    private void EnsureRegistered()
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        CustomMessagingManager current = net != null ? net.Messaging : null;

        if (current == registeredOn) return;

        // 別のセッション（旧マネージャは破棄済み）または停止状態へ変わった。古い参照は捨てる
        registeredOn = null;
        if (current == null) return;

        registeredAsClient = !net.IsHostAuthority;
        if (registeredAsClient)
        {
            current.RegisterNamedMessageHandler(CoopNetworkManager.StateSnapshotChannelName, HandleSnapshotMessage);
            current.RegisterNamedMessageHandler(CoopNetworkManager.EventChannelName, HandleEventMessage);
        }
        registeredOn = current;
        Debug.Log($"[CoopStateSyncManager] Registered as {(registeredAsClient ? "client (snapshot/event handlers)" : "host (send only)")}.");
    }

    private void UnregisterHandlers()
    {
        if (registeredOn == null) return;
        if (registeredAsClient)
        {
            try
            {
                registeredOn.UnregisterNamedMessageHandler(CoopNetworkManager.StateSnapshotChannelName);
                registeredOn.UnregisterNamedMessageHandler(CoopNetworkManager.EventChannelName);
            }
            catch (Exception e)
            {
                // 既に停止処理が進んでいる場合は解除に失敗しうるが、マネージャごと破棄されるため無害
                Debug.LogWarning($"[CoopStateSyncManager] Failed to unregister handlers: {e.Message}");
            }
        }
        registeredOn = null;
    }

    // ===== ホスト側: スナップショット送信 =====

    private void SendSnapshot(CoopNetworkManager net)
    {
        GameManager gm = GameManager.Instance;
        CustomMessagingManager msg = net.Messaging;
        if (gm == null || msg == null) return;

        try
        {
            // 1メッセージが16KB（Allocator.Tempの1確保あたりの目安上限）を超えうるため、Tempではなく
            // TempJobを使う（usingで即Disposeするため漏れない）。足りなければSnapshotMaxSizeまで自動拡張される
            using (FastBufferWriter w = new FastBufferWriter(SnapshotInitialSize, Allocator.TempJob, SnapshotMaxSize))
            {
                WriteSnapshot(w, gm);
                // 全スナップショットが順序通りに届くことが「IDが無い＝消えた」解釈の前提のため、信頼・順序保証・断片化可
                msg.SendNamedMessage(CoopNetworkManager.StateSnapshotChannelName, net.PeerClientId, w, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CoopStateSyncManager] Failed to send snapshot: {e.Message}");
        }
    }

    private void WriteSnapshot(FastBufferWriter w, GameManager gm)
    {
        // 1. バージョン + スカラー
        w.WriteValueSafe(SnapshotFormatVersion);
        w.WriteValueSafe((byte)gm.CurrentPhase);
        w.WriteValueSafe(gm.CurrentWave);
        w.WriteValueSafe(gm.Life);
        w.WriteValueSafe(gm.InitialLife);
        w.WriteValueSafe(gm.Cost);
        w.WriteValueSafe(gm.GetPersonalCost(0));
        w.WriteValueSafe(gm.GetPersonalCost(1));
        w.WriteValueSafe(gm.UnionPower);
        w.WriteValueSafe(gm.TransferRemaining);
        w.WriteValueSafe(gm.GetKillCount(0));
        w.WriteValueSafe(gm.GetKillCount(1));
        w.WriteValueSafe((byte)(gm.CoreShieldActive ? 1 : 0));
        w.WriteValueSafe(gm.MapExpandedWave);

        // 2. 今のSetupでの種別ごとの設置数（maxPerSetupカードの表示用）。(type, count)の組をenumの全値分送る
        TowerManager tm = TowerManager.Instance;
        w.WriteValueSafe((byte)AllTowerTypes.Length);
        for (int i = 0; i < AllTowerTypes.Length; i++)
        {
            int placed = tm != null ? tm.GetPlacedCountInCurrentSetup(AllTowerTypes[i]) : 0;
            w.WriteValueSafe((byte)(int)AllTowerTypes[i]);
            w.WriteValueSafe((byte)Mathf.Clamp(placed, 0, 255));
        }

        // 3. Union承認待ち（仮表示と承認バナー用。残り秒数はバナーの表示用）
        bool hasPending = tm != null && tm.HasPendingUnionRequest;
        w.WriteValueSafe((byte)(hasPending ? 1 : 0));
        if (hasPending)
        {
            Vector3Int pendingCell = tm.PendingUnionCellPos;
            w.WriteValueSafe((byte)(int)tm.PendingUnionType);
            w.WriteValueSafe((byte)Mathf.Clamp(tm.PendingUnionRequesterId, 0, 1));
            w.WriteValueSafe(pendingCell.x);
            w.WriteValueSafe(pendingCell.y);
            w.WriteValueSafe(tm.PendingUnionSecondsRemaining);
        }
        else
        {
            w.WriteValueSafe((byte)0);
            w.WriteValueSafe((byte)0);
            w.WriteValueSafe(0);
            w.WriteValueSafe(0);
            w.WriteValueSafe(0f);
        }

        // 4. Operator Abilityの装備とCD残り秒数（owner 0..1 x slot 0..1）
        OperatorAbilityManager oam = OperatorAbilityManager.Instance;
        for (int p = 0; p < 2; p++)
        {
            for (int s = 0; s < 2; s++)
            {
                byte abilityType = oam != null ? (byte)(int)oam.GetLoadout(p, s) : (byte)0;
                float cooldown = oam != null ? oam.GetCooldownRemaining(p, s) : 0f;
                w.WriteValueSafe(abilityType);
                w.WriteValueSafe(cooldown);
            }
        }

        // 5. 報酬の獲得数。(type, count)の組をenumの全値分送る
        RewardManager rm = RewardManager.Instance;
        w.WriteValueSafe((byte)AllRewardTypes.Length);
        Dictionary<RewardType, int> rewardCounts = rm != null ? rm.GetAcquiredRewardCounts() : null;
        for (int i = 0; i < AllRewardTypes.Length; i++)
        {
            int count = 0;
            if (rewardCounts != null) rewardCounts.TryGetValue(AllRewardTypes[i], out count);
            w.WriteValueSafe((byte)(int)AllRewardTypes[i]);
            w.WriteValueSafe((ushort)Mathf.Clamp(count, 0, ushort.MaxValue));
        }

        // 6. タワー（NetworkId採番済みのものだけ）
        List<Tower> towers = tm != null ? tm.GetActiveTowers() : null;
        int towerCount = 0;
        if (towers != null)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                if (towers[i] != null && towers[i].NetworkId != 0 && !towers[i].IsNetworkProxy) towerCount++;
            }
        }
        w.WriteValueSafe((ushort)towerCount);
        if (towers != null && MapManager.Instance != null)
        {
            for (int i = 0; i < towers.Count; i++)
            {
                Tower t = towers[i];
                if (t == null || t.NetworkId == 0 || t.IsNetworkProxy) continue;

                Vector3Int cell = MapManager.Instance.WorldToGrid(t.transform.position);
                byte flags = 0;
                if (t.IsOffline) flags |= TowerFlagOffline;
                if (t.RequiresSupply) flags |= TowerFlagRequiresSupply;
                if (t.IsSiegeMarked) flags |= TowerFlagSiegeMarked;
                if (t.IsOvercharged) flags |= TowerFlagOvercharged;
                if (t.IsReinforced) flags |= TowerFlagReinforced;

                w.WriteValueSafe(t.NetworkId);
                w.WriteValueSafe((byte)(int)t.Type);
                w.WriteValueSafe((short)cell.x);
                w.WriteValueSafe((short)cell.y);
                w.WriteValueSafe((byte)Mathf.Clamp(t.OwnerId, 0, 1));
                w.WriteValueSafe((byte)Mathf.Clamp(t.OutpostNumber, 0, 255));
                w.WriteValueSafe((ushort)Mathf.Clamp(t.PlacedWave, 0, ushort.MaxValue));
                w.WriteValueSafe(t.CurrentHp);
                w.WriteValueSafe(flags);
            }
        }

        // 7. 敵（NetworkId採番済みのものだけ）
        List<Enemy> enemies = EnemySpawner.Instance != null ? EnemySpawner.Instance.GetActiveEnemies() : null;
        int enemyCount = 0;
        if (enemies != null)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] != null && enemies[i].NetworkId != 0 && !enemies[i].IsNetworkProxy) enemyCount++;
            }
        }
        w.WriteValueSafe((ushort)enemyCount);
        if (enemies != null)
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy e = enemies[i];
                if (e == null || e.NetworkId == 0 || e.IsNetworkProxy) continue;

                byte special = 0;
                if (e.IsBoss) special |= EnemySpecialBoss;
                if (e.IsBarricadeBuster) special |= EnemySpecialBuster;
                if (e.IsSiegeMarker) special |= EnemySpecialSiege;
                byte status = 0;
                if (e.IsSlowed) status |= EnemyStatusSlowed;
                if (e.IsTaunted) status |= EnemyStatusTaunted;

                Vector3 pos = e.transform.position;
                w.WriteValueSafe(e.NetworkId);
                w.WriteValueSafe(e.NetKind);
                w.WriteValueSafe(special);
                w.WriteValueSafe(pos.x);
                w.WriteValueSafe(pos.y);
                w.WriteValueSafe(e.CurrentHp);
                w.WriteValueSafe(e.MaxHp);
                w.WriteValueSafe(status);
            }
        }

        // 8. 報酬提示（Step 4-8）。Rewardフェーズ中で提示が残っている間だけ書く
        bool hasOffer = rm != null && rm.HasActiveOffers && gm.CurrentPhase == GamePhase.Reward && rm.CurrentOffers.Count > 0;
        w.WriteValueSafe((byte)(hasOffer ? 1 : 0));
        if (hasOffer)
        {
            int offerCount = Mathf.Min(rm.CurrentOffers.Count, MaxRewardOffers);
            w.WriteValueSafe((ushort)Mathf.Clamp(rm.CurrentRewardWave, 0, ushort.MaxValue));
            w.WriteValueSafe((byte)Mathf.Clamp(rm.CurrentChooserOwnerId, 0, 1));
            w.WriteValueSafe((sbyte)Mathf.Clamp(rm.ChooserHoverIndex, -1, MaxRewardOffers - 1));
            w.WriteValueSafe((byte)offerCount);
            for (int i = 0; i < offerCount; i++)
            {
                RewardOffer offer = rm.CurrentOffers[i];
                w.WriteValueSafe((byte)(int)offer.Type);
                w.WriteValueSafe(offer.Value);
            }
        }
    }

    // ===== ホスト側: イベント送信 =====

    private void FlushEvents(CoopNetworkManager net)
    {
        CustomMessagingManager msg = net.Messaging;
        ulong peer = net.PeerClientId;

        int index = 0;
        while (index < pendingEvents.Count)
        {
            int chunk = Mathf.Min(MaxEventsPerMessage, pendingEvents.Count - index);
            try
            {
                using (FastBufferWriter w = new FastBufferWriter(1200, Allocator.Temp))
                {
                    w.WriteValueSafe((byte)chunk);
                    for (int i = 0; i < chunk; i++)
                    {
                        PendingEvent ev = pendingEvents[index + i];
                        w.WriteValueSafe(ev.type);
                        switch (ev.type)
                        {
                            case EvtFire:
                                w.WriteValueSafe(ev.a);
                                w.WriteValueSafe(ev.b);
                                w.WriteValueSafe(ev.c);
                                break;
                            case EvtCombo:
                                w.WriteValueSafe(ev.text ?? "");
                                w.WriteValueSafe(ev.x);
                                w.WriteValueSafe(ev.y);
                                break;
                            case EvtOutpostDown:
                                w.WriteValueSafe(ev.c);
                                break;
                            case EvtSiegeInbound:
                                w.WriteValueSafe((byte)ev.a); // ownerId
                                w.WriteValueSafe(ev.c);       // outpostNumber
                                break;
                        }
                    }
                    msg.SendNamedMessage(CoopNetworkManager.EventChannelName, peer, w, NetworkDelivery.ReliableSequenced);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CoopStateSyncManager] Failed to send events: {e.Message}");
            }
            index += chunk;
        }

        pendingEvents.Clear();
        pendingNonFireCount = 0;
    }

    // ===== クライアント側: イベント受信 =====

    private void HandleEventMessage(ulong senderClientId, FastBufferReader reader)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworkedClient) return;

        try
        {
            reader.ReadValueSafe(out byte count);
            if (count > MaxEventsPerMessage) return;

            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out byte type);
                switch (type)
                {
                    case EvtFire:
                    {
                        reader.ReadValueSafe(out ushort towerId);
                        reader.ReadValueSafe(out ushort enemyId);
                        reader.ReadValueSafe(out byte effectKind);
                        OnRemoteTowerFired?.Invoke(towerId, enemyId, effectKind);
                        break;
                    }
                    case EvtCombo:
                    {
                        reader.ReadValueSafe(out string comboName);
                        reader.ReadValueSafe(out float comboX);
                        reader.ReadValueSafe(out float comboY);
                        if (string.IsNullOrEmpty(comboName)) break;
                        if (comboName.Length > MaxComboNameLength) comboName = comboName.Substring(0, MaxComboNameLength);
                        // Step 4-0b-4: 位置が不正なイベントは（バナーも含め）破棄する。ペイロードは読み切っているので後続イベントには影響しない
                        if (!IsFiniteWithin(comboX, MaxWorldAbs) || !IsFiniteWithin(comboY, MaxWorldAbs)) break;
                        if (OperatorAbilityManager.Instance != null)
                        {
                            OperatorAbilityManager.Instance.RaiseComboTriggered(comboName);
                            OperatorAbilityManager.Instance.ShowComboFlash(new Vector3(comboX, comboY, 0f));
                        }
                        break;
                    }
                    case EvtOutpostDown:
                    {
                        reader.ReadValueSafe(out byte ownerId);
                        if (ownerId > 1) break;
                        if (HUDManager.Instance != null) HUDManager.Instance.ShowOutpostDownWarning(ownerId);
                        break;
                    }
                    case EvtSiegeInbound:
                    {
                        reader.ReadValueSafe(out byte ownerId);
                        reader.ReadValueSafe(out byte outpostNumber);
                        if (ownerId > 1) break;
                        if (HUDManager.Instance != null) HUDManager.Instance.ShowSiegeInboundWarning(ownerId, outpostNumber);
                        break;
                    }
                    default:
                        // 種別が分からないとペイロード長も分からず後続を読めないため、このメッセージの残りは捨てる
                        Debug.LogWarning($"[CoopStateSyncManager] Unknown event type {type}. Remaining events in this message dropped.");
                        return;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CoopStateSyncManager] Malformed event message: {e.Message}");
        }
    }

    // ===== クライアント側: スナップショット受信 =====

    private void HandleSnapshotMessage(ulong senderClientId, FastBufferReader reader)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworkedClient) return;

        try
        {
            // 先に全体を読み取って検証し、全て妥当な場合のみ適用する（部分適用しない）
            if (!TryParseSnapshot(reader)) return;
            ApplySnapshot();
        }
        catch (Exception e)
        {
            // 短すぎる/壊れたペイロード（ReadValueSafeがOverflowException等を投げる）は破棄する
            Debug.LogWarning($"[CoopStateSyncManager] Malformed snapshot: {e.Message}");
        }
    }

    private static bool IsFiniteWithin(float value, float maxAbs)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) <= maxAbs;
    }

    private bool Reject(string reason)
    {
        Debug.LogWarning($"[CoopStateSyncManager] Snapshot rejected: {reason}");
        return false;
    }

    private bool TryParseSnapshot(FastBufferReader reader)
    {
        // 1. バージョン + スカラー
        reader.ReadValueSafe(out byte version);
        if (version != SnapshotFormatVersion) return Reject($"unsupported format version {version}");

        reader.ReadValueSafe(out byte phaseByte);
        reader.ReadValueSafe(out int wave);
        reader.ReadValueSafe(out int life);
        reader.ReadValueSafe(out int initialLife);
        reader.ReadValueSafe(out int cost);
        reader.ReadValueSafe(out int personal0);
        reader.ReadValueSafe(out int personal1);
        reader.ReadValueSafe(out int unionPower);
        reader.ReadValueSafe(out int transferRemaining);
        reader.ReadValueSafe(out int kills0);
        reader.ReadValueSafe(out int kills1);
        reader.ReadValueSafe(out byte shield);
        reader.ReadValueSafe(out int mapExpandedWave);

        if (!Enum.IsDefined(typeof(GamePhase), (int)phaseByte)) return Reject("invalid phase");
        if (wave < 1 || wave > 9999) return Reject("wave out of range");
        if (life < 0 || life > 100000 || initialLife < 1 || initialLife > 100000) return Reject("life out of range");
        if (cost < 0 || cost > 10000 || personal0 < 0 || personal0 > 10000 || personal1 < 0 || personal1 > 10000) return Reject("cost out of range");
        if (unionPower < 0 || unionPower > 10000 || transferRemaining < 0 || transferRemaining > 1000) return Reject("resource out of range");
        if (kills0 < 0 || kills0 > 10000000 || kills1 < 0 || kills1 > 10000000) return Reject("kills out of range");
        if (shield > 1) return Reject("invalid shield flag");
        if (mapExpandedWave < 0 || mapExpandedWave > 9999) return Reject("mapExpandedWave out of range");

        snapPhase = (GamePhase)phaseByte;
        snapWave = wave;
        snapLife = life;
        snapInitialLife = initialLife;
        snapCost = cost;
        snapPersonal0 = personal0;
        snapPersonal1 = personal1;
        snapUnionPower = unionPower;
        snapTransferRemaining = transferRemaining;
        snapKills0 = kills0;
        snapKills1 = kills1;
        snapShield = shield == 1;
        snapMapExpandedWave = mapExpandedWave;

        // 2. 設置数
        reader.ReadValueSafe(out byte placedEntryCount);
        if (placedEntryCount > MaxTableEntries) return Reject("too many placed-count entries");
        Array.Clear(snapPlacedCounts, 0, snapPlacedCounts.Length);
        for (int i = 0; i < placedEntryCount; i++)
        {
            reader.ReadValueSafe(out byte typeByte);
            reader.ReadValueSafe(out byte count);
            if (typeByte < MaxTableEntries && Enum.IsDefined(typeof(TowerType), (int)typeByte))
            {
                snapPlacedCounts[typeByte] = count;
            }
        }

        // 3. Union承認待ち
        reader.ReadValueSafe(out byte unionHas);
        reader.ReadValueSafe(out byte unionTypeByte);
        reader.ReadValueSafe(out byte unionRequester);
        reader.ReadValueSafe(out int unionX);
        reader.ReadValueSafe(out int unionY);
        reader.ReadValueSafe(out float unionSeconds);
        if (unionHas > 1) return Reject("invalid union flag");
        if (unionHas == 1)
        {
            if (!Enum.IsDefined(typeof(TowerType), (int)unionTypeByte)) return Reject("invalid union tower type");
            if (unionRequester > 1) return Reject("invalid union requester");
            if (Mathf.Abs(unionX) > MaxCellAbs || Mathf.Abs(unionY) > MaxCellAbs) return Reject("union cell out of range");
            if (!IsFiniteWithin(unionSeconds, 1000f)) return Reject("invalid union seconds");
        }
        snapUnionHas = unionHas == 1;
        snapUnionType = snapUnionHas ? (TowerType)unionTypeByte : TowerType.Normal;
        snapUnionRequester = unionRequester;
        snapUnionCell = new Vector3Int(unionX, unionY, 0);
        snapUnionSeconds = snapUnionHas ? Mathf.Max(0f, unionSeconds) : 0f;

        // 4. Operator Ability
        for (int i = 0; i < 4; i++)
        {
            reader.ReadValueSafe(out byte abilityByte);
            reader.ReadValueSafe(out float cooldown);
            if (!Enum.IsDefined(typeof(OperatorAbilityType), (int)abilityByte)) return Reject("invalid ability type");
            if (!IsFiniteWithin(cooldown, 1000f)) return Reject("invalid cooldown");
            snapLoadouts[i] = (OperatorAbilityType)abilityByte;
            snapCooldowns[i] = Mathf.Max(0f, cooldown);
        }

        // 5. 報酬
        reader.ReadValueSafe(out byte rewardEntryCount);
        if (rewardEntryCount > MaxTableEntries) return Reject("too many reward entries");
        Array.Clear(snapRewardCounts, 0, snapRewardCounts.Length);
        for (int i = 0; i < rewardEntryCount; i++)
        {
            reader.ReadValueSafe(out byte typeByte);
            reader.ReadValueSafe(out ushort count);
            if (typeByte < MaxTableEntries && Enum.IsDefined(typeof(RewardType), (int)typeByte))
            {
                snapRewardCounts[typeByte] = count;
            }
        }

        // 6. タワー
        reader.ReadValueSafe(out ushort towerCount);
        if (towerCount > MaxTowers) return Reject("too many towers");
        snapTowers.Clear();
        for (int i = 0; i < towerCount; i++)
        {
            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out byte typeByte);
            reader.ReadValueSafe(out short x);
            reader.ReadValueSafe(out short y);
            reader.ReadValueSafe(out byte owner);
            reader.ReadValueSafe(out byte outpost);
            reader.ReadValueSafe(out ushort placedWave);
            reader.ReadValueSafe(out float hp);
            reader.ReadValueSafe(out byte flags);

            if (id == 0) return Reject("tower id 0");
            if (!Enum.IsDefined(typeof(TowerType), (int)typeByte)) return Reject("invalid tower type");
            if (Mathf.Abs(x) > MaxCellAbs || Mathf.Abs(y) > MaxCellAbs) return Reject("tower cell out of range");
            if (owner > 1) return Reject("invalid tower owner");
            if (!IsFiniteWithin(hp, 1000000f) || hp < 0f) return Reject("invalid tower hp");

            snapTowers.Add(new TowerEntry
            {
                id = id,
                type = (TowerType)typeByte,
                cell = new Vector3Int(x, y, 0),
                ownerId = owner,
                outpostNumber = outpost,
                placedWave = placedWave,
                hp = hp,
                flags = flags,
            });
        }

        // 7. 敵
        reader.ReadValueSafe(out ushort enemyCount);
        if (enemyCount > MaxEnemies) return Reject("too many enemies");
        snapEnemies.Clear();
        for (int i = 0; i < enemyCount; i++)
        {
            reader.ReadValueSafe(out ushort id);
            reader.ReadValueSafe(out byte kind);
            reader.ReadValueSafe(out byte special);
            reader.ReadValueSafe(out float x);
            reader.ReadValueSafe(out float y);
            reader.ReadValueSafe(out float hp);
            reader.ReadValueSafe(out float maxHp);
            reader.ReadValueSafe(out byte status);

            if (id == 0) return Reject("enemy id 0");
            if (!IsFiniteWithin(x, MaxWorldAbs) || !IsFiniteWithin(y, MaxWorldAbs)) return Reject("enemy position out of range");
            if (!IsFiniteWithin(hp, 1.0e9f) || !IsFiniteWithin(maxHp, 1.0e9f) || maxHp <= 0f) return Reject("invalid enemy hp");

            snapEnemies.Add(new EnemyEntry
            {
                id = id,
                kind = kind,
                special = special,
                position = new Vector3(x, y, 0f),
                hp = hp,
                maxHp = maxHp,
                status = status,
            });
        }

        // 8. 報酬提示（Step 4-8）
        reader.ReadValueSafe(out byte hasOffer);
        if (hasOffer > 1) return Reject("invalid reward offer flag");
        snapHasRewardOffer = false;
        snapRewardOfferCount = 0;
        if (hasOffer == 1)
        {
            reader.ReadValueSafe(out ushort rewardWave);
            reader.ReadValueSafe(out byte chooser);
            reader.ReadValueSafe(out sbyte hover);
            reader.ReadValueSafe(out byte offerCount);
            if (rewardWave < 1 || rewardWave > 9999) return Reject("reward wave out of range");
            if (chooser > 1) return Reject("invalid reward chooser");
            if (offerCount > MaxRewardOffers) return Reject("too many reward offers");
            if (hover < -1 || hover > MaxRewardOffers - 1) return Reject("reward hover out of range");

            for (int i = 0; i < offerCount; i++)
            {
                reader.ReadValueSafe(out byte typeByte);
                reader.ReadValueSafe(out int value);
                if (!Enum.IsDefined(typeof(RewardType), (int)typeByte)) return Reject("invalid reward offer type");
                if (value < -1 || value > 10) return Reject("reward offer value out of range");
                snapRewardOfferTypes[i] = (RewardType)typeByte;
                snapRewardOfferValues[i] = value;
            }

            snapHasRewardOffer = offerCount > 0;
            snapRewardWave = rewardWave;
            snapRewardChooser = chooser;
            snapRewardHover = hover < offerCount ? hover : -1;
            snapRewardOfferCount = offerCount;
        }

        return true;
    }

    // ===== クライアント側: スナップショット適用 =====

    private void ApplySnapshot()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        // 接続直後の最初のスナップショットでミラーモードへ移行する（Update()で既に移行済みなら何もしない）
        if (!gm.IsClientMirror)
        {
            gm.EnterClientMirrorMode();
            if (!gm.IsClientMirror) return;
        }

        // 適用順序には意味がある: スカラー(Wave/Phase/マップ拡張) → 設置数/Union → 報酬(タワーのStart()が参照する) →
        // タワー → 敵。Wave/Phaseを先に反映するのは、タワーのプロキシがStart()で参照するCurrentWave/CurrentPhaseを最新にするため
        gm.ApplyRemoteState(snapPhase, snapWave, snapLife, snapInitialLife, snapCost,
            snapPersonal0, snapPersonal1, snapUnionPower, snapTransferRemaining,
            snapKills0, snapKills1, snapShield, snapMapExpandedWave);

        TowerManager tm = TowerManager.Instance;
        if (tm != null)
        {
            tm.ApplyRemotePlacedCounts(snapPlacedCounts);
            tm.ApplyRemoteUnionPending(snapUnionHas, snapUnionType, snapUnionRequester, snapUnionCell, snapUnionSeconds);
        }

        if (OperatorAbilityManager.Instance != null)
        {
            OperatorAbilityManager.Instance.ApplyRemoteAbilityState(snapLoadouts, snapCooldowns);
        }

        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.ApplyRemoteRewardCounts(snapRewardCounts);
            // 報酬カードの表示/閉鎖（Rewardフェーズ中に提示があれば開き、無ければ閉じる）
            RewardManager.Instance.ApplyRemoteRewardOffer(snapHasRewardOffer, snapRewardWave, snapRewardChooser,
                snapRewardHover, snapRewardOfferTypes, snapRewardOfferValues, snapRewardOfferCount);
        }

        if (tm != null) ApplyTowers(tm);
        ApplyEnemies();
    }

    private static void ApplyTowerState(Tower tower, TowerEntry e)
    {
        tower.ApplyRemoteState(e.hp,
            (e.flags & TowerFlagOffline) != 0,
            (e.flags & TowerFlagRequiresSupply) != 0,
            (e.flags & TowerFlagSiegeMarked) != 0,
            (e.flags & TowerFlagOvercharged) != 0,
            (e.flags & TowerFlagReinforced) != 0);
    }

    private void ApplyTowers(TowerManager tm)
    {
        seenIds.Clear();

        for (int i = 0; i < snapTowers.Count; i++)
        {
            TowerEntry e = snapTowers[i];
            seenIds.Add(e.id);

            if (towerProxies.TryGetValue(e.id, out TowerProxyRecord record))
            {
                bool unchanged = record.tower != null
                    && record.type == e.type && record.cell == e.cell && record.ownerId == e.ownerId;
                if (unchanged)
                {
                    ApplyTowerState(record.tower, e);
                    continue;
                }

                // 同じIDなのに種別/セル/所有者が違う（IDの再利用など）: 古いプロキシを片付けて作り直す
                if (record.tower != null) tm.RemoveRemoteTowerProxy(record.tower);
                towerProxies.Remove(e.id);
            }

            Tower tower = tm.SpawnRemoteTowerProxy(e.id, e.type, e.cell, e.ownerId, e.outpostNumber, e.placedWave, e.hp,
                (e.flags & TowerFlagRequiresSupply) != 0);
            if (tower == null) continue;

            // Offline/Siege/バフのフラグは生成直後（Start()前）でも反映できる（Tower側が保留値として扱う）
            ApplyTowerState(tower, e);
            towerProxies[e.id] = new TowerProxyRecord { tower = tower, type = e.type, cell = e.cell, ownerId = e.ownerId };
        }

        // スナップショットに載っていないIDは売却/破壊されたタワー。セル占有を解除してプロキシを破棄する
        staleIds.Clear();
        foreach (KeyValuePair<ushort, TowerProxyRecord> kv in towerProxies)
        {
            if (!seenIds.Contains(kv.Key)) staleIds.Add(kv.Key);
        }
        for (int i = 0; i < staleIds.Count; i++)
        {
            tm.RemoveRemoteTowerProxy(towerProxies[staleIds[i]].tower);
            towerProxies.Remove(staleIds[i]);
        }
    }

    private void ApplyEnemies()
    {
        EnemySpawner spawner = EnemySpawner.Instance;
        if (spawner == null) return;

        seenIds.Clear();

        for (int i = 0; i < snapEnemies.Count; i++)
        {
            EnemyEntry e = snapEnemies[i];
            seenIds.Add(e.id);
            bool boss = (e.special & EnemySpecialBoss) != 0;
            bool buster = (e.special & EnemySpecialBuster) != 0;
            bool siege = (e.special & EnemySpecialSiege) != 0;
            bool slowed = (e.status & EnemyStatusSlowed) != 0;
            bool taunted = (e.status & EnemyStatusTaunted) != 0;

            if (enemyProxies.TryGetValue(e.id, out Enemy existing))
            {
                bool unchanged = existing != null && existing.NetKind == e.kind
                    && existing.IsBoss == boss && existing.IsBarricadeBuster == buster && existing.IsSiegeMarker == siege;
                if (unchanged)
                {
                    existing.ApplyRemoteState(e.position, e.hp, e.maxHp, slowed, taunted);
                    continue;
                }

                // 同じIDなのに種別が違う（IDの再利用など）: 古いプロキシを片付けて作り直す
                if (existing != null) spawner.RemoveRemoteProxy(existing);
                enemyProxies.Remove(e.id);
            }

            Enemy enemy = spawner.SpawnRemoteProxy(e.id, e.kind, boss, buster, siege, e.position, e.hp, e.maxHp);
            if (enemy == null) continue;
            enemy.ApplyRemoteState(e.position, e.hp, e.maxHp, slowed, taunted);
            enemyProxies[e.id] = enemy;
        }

        // スナップショットに載っていないIDは撃破/コア到達で消えた敵。撃破数・コア被弾などの副作用なしで破棄する
        staleIds.Clear();
        foreach (KeyValuePair<ushort, Enemy> kv in enemyProxies)
        {
            if (!seenIds.Contains(kv.Key)) staleIds.Add(kv.Key);
        }
        for (int i = 0; i < staleIds.Count; i++)
        {
            spawner.RemoveRemoteProxy(enemyProxies[staleIds[i]]);
            enemyProxies.Remove(staleIds[i]);
        }
    }
}
