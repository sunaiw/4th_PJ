using System;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;

// Step 4-7: 相手カーソル（ピアのゴーストカーソル）。
// 自分のマウスのワールド座標を20Hzで相手へ送り、受信した相手のカーソルを所有者カラーの矢印として
// ワールド空間に表示する。「ここに置いて」を指差しで伝えるための純粋な視覚補助であり、ゲーム状態には一切影響しない。
//
// 重要な設計判断:
// - ホスト・クライアントの双方向。両側が同じチャンネル(CursorChannelName)のハンドラを登録する
//   （送信先: ホスト→PeerClientId／クライアント→ServerClientId）
// - 配信はUnreliableSequenced。カーソル座標は古い値に意味がなく、欠損しても次の値で上書きされるため
// - 送信・受信・補間・フェードはすべてTime.unscaledTime/unscaledDeltaTimeで行う
//   （ポーズ・チュートリアル等でTime.timeScaleが0でも動作させるため）
// - ハンドラ登録はCoopCommandManagerと同じパターン（Messaging差し替わり時の再登録、停止直前のBeforeNetworkShutdownで解除）
// - ネットワーク未起動（シングルプレイ・PLAY ON THIS DEVICE）では送受信も描画も一切行わない
//
// CoopCommandManager等と同じ理由（GameManager.Start()からAddComponentで動的生成されるため）で
// SingletonBehaviour<T>は使わず、最小限の静的参照だけを持たせる
public class CoopCursorSync : MonoBehaviour
{
    public static CoopCursorSync Instance { get; private set; }

    // ===== ペイロード: byte flags, float x, float y =====
    private const byte FlagVisible = 1;       // bit0: カーソルを表示するか
    private const byte KnownFlagsMask = FlagVisible;

    // 受信側の不正値対策（CoopCommandManager / CoopStateSyncManagerと同じ上限）
    private const float MaxWorldAbs = 500f;

    // ===== 送信 =====
    private const float SendInterval = 0.05f;       // 20Hz
    private const float KeepAliveInterval = 0.25f;  // 変化が無くても最低この間隔で送る（受信側のタイムアウトより十分短くする）
    private const float MoveThreshold = 0.005f;     // これ未満の移動は「変化なし」とみなす（ワールド単位）

    // ===== 表示 =====
    private const float TimeoutSeconds = 0.5f;      // この時間パケットが来なければフェードアウト
    private const float FadeSpeed = 6f;             // alphaの変化速度（1/秒）。約0.17秒でフェード完了
    private const float FollowSharpness = 18f;      // 位置の指数平滑化の強さ（大きいほど追従が速い）
    private const int CursorSortingOrder = 5000;    // 既存のワールド側最大(HealthDisplayの100)より十分手前
    private const int TexW = 16;
    private const int TexH = 22;
    private const int TexPad = 2;                   // 矢印の先端を基準にしたテクスチャ余白(px)。輪郭を収めるため
    private const float PixelsPerUnit = 40f;        // 矢印の大きさ: 約0.4x0.55ワールド単位

    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);

    // 全体で1枚だけ共有する静的キャッシュ（Tower.GetOrCreateBadgeSpriteと同じ方針）
    private static Sprite cursorSprite;

    private CustomMessagingManager registeredOn;

    // 送信状態
    private float nextSendTime;
    private float lastSentTime = -999f;
    private bool hasLastSent;
    private byte lastSentFlags;
    private Vector2 lastSentPos;

    // 受信状態
    private bool hasReceived;
    private bool targetVisible;
    private Vector2 targetPos;
    private float lastReceiveTime = -999f;

    // 描画状態
    private GameObject cursorObj;
    private SpriteRenderer cursorRenderer;
    private float currentAlpha;
    private bool shown; // 画面に出ている（alpha>0）か。falseから出る際は位置をスナップする

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
        UnregisterHandler();
        DestroyCursor();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // 状態変化イベントを取りこぼした場合の保険（CustomMessagingManagerの差し替わりを毎フレーム軽量に確認する）
        EnsureRegistered();

        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworked || !net.IsPeerConnected || net.Messaging == null)
        {
            // シングルプレイ／PLAY ON THIS DEVICE／相手未接続: 送受信も描画も行わない
            ResetRemoteState();
            return;
        }

        TrySend(net);
        UpdateCursorVisual(net);
    }

    private void HandleConnectionStateChanged()
    {
        EnsureRegistered();

        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworked || !net.IsPeerConnected)
        {
            // 切断: 即座に消す（フェードさせない）
            ResetRemoteState();
        }
        else
        {
            // 接続成立: 次のUpdateで必ず現在値を送る
            hasLastSent = false;
        }
    }

    private void HandleBeforeNetworkShutdown()
    {
        UnregisterHandler();
        ResetRemoteState();
    }

    // 受信・描画・送信の状態を初期化し、カーソルオブジェクトを破棄する
    private void ResetRemoteState()
    {
        hasReceived = false;
        targetVisible = false;
        lastReceiveTime = -999f;
        hasLastSent = false;
        currentAlpha = 0f;
        shown = false;
        DestroyCursor();
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

        current.RegisterNamedMessageHandler(CoopNetworkManager.CursorChannelName, HandleCursorMessage);
        registeredOn = current;
        Debug.Log("[CoopCursorSync] Registered cursor handler.");
    }

    private void UnregisterHandler()
    {
        if (registeredOn == null) return;
        try
        {
            registeredOn.UnregisterNamedMessageHandler(CoopNetworkManager.CursorChannelName);
        }
        catch (Exception e)
        {
            // 既に停止処理が進んでいる場合は解除に失敗しうるが、マネージャごと破棄されるため無害
            Debug.LogWarning($"[CoopCursorSync] Failed to unregister handler: {e.Message}");
        }
        registeredOn = null;
    }

    // ===== 送信 =====

    private void TrySend(CoopNetworkManager net)
    {
        float now = Time.unscaledTime;
        if (now < nextSendTime) return;
        nextSendTime = now + SendInterval;

        byte flags = 0;
        Vector2 pos = hasLastSent ? lastSentPos : Vector2.zero;
        if (TryGetLocalCursorWorld(out Vector2 world))
        {
            flags |= FlagVisible;
            pos = world;
        }

        // 変化が無ければ送らない（ただしKeepAliveIntervalごとには必ず送る）
        if (hasLastSent && now - lastSentTime < KeepAliveInterval)
        {
            if (flags == lastSentFlags)
            {
                if (flags == 0) return; // 非表示のまま
                if ((pos - lastSentPos).sqrMagnitude < MoveThreshold * MoveThreshold) return;
            }
        }

        using (FastBufferWriter w = new FastBufferWriter(16, Allocator.Temp))
        {
            w.WriteValueSafe(flags);
            w.WriteValueSafe(pos.x);
            w.WriteValueSafe(pos.y);

            ulong target = net.IsHostAuthority ? net.PeerClientId : NetworkManager.ServerClientId;
            net.Messaging.SendNamedMessage(CoopNetworkManager.CursorChannelName, target, w, NetworkDelivery.UnreliableSequenced);
        }

        hasLastSent = true;
        lastSentTime = now;
        lastSentFlags = flags;
        lastSentPos = pos;
    }

    // ローカルのマウスのワールド座標。フォーカス外・画面外・カメラ無しの場合はfalse（=非表示）
    private static bool TryGetLocalCursorWorld(out Vector2 world)
    {
        world = Vector2.zero;
        if (!Application.isFocused) return false;

        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 mouse = Input.mousePosition;
        if (mouse.x < 0f || mouse.y < 0f || mouse.x > Screen.width || mouse.y > Screen.height) return false;

        Vector3 w = cam.ScreenToWorldPoint(mouse);
        if (float.IsNaN(w.x) || float.IsNaN(w.y) || float.IsInfinity(w.x) || float.IsInfinity(w.y)) return false;
        world = new Vector2(w.x, w.y);
        return true;
    }

    // ===== 受信 =====

    private void HandleCursorMessage(ulong senderClientId, FastBufferReader reader)
    {
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworked) return;

        // 不正・途切れたペイロードは例外を出さず無視する
        try
        {
            reader.ReadValueSafe(out byte flags);
            reader.ReadValueSafe(out float x);
            reader.ReadValueSafe(out float y);

            if ((flags & ~KnownFlagsMask) != 0) return;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return;
            if (Mathf.Abs(x) > MaxWorldAbs || Mathf.Abs(y) > MaxWorldAbs) return;

            hasReceived = true;
            targetVisible = (flags & FlagVisible) != 0;
            targetPos = new Vector2(x, y);
            lastReceiveTime = Time.unscaledTime;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CoopCursorSync] Malformed cursor message ignored: {e.Message}");
        }
    }

    // ===== 描画 =====

    private void UpdateCursorVisual(CoopNetworkManager net)
    {
        int peerOwnerId = 1 - net.LocalOwnerId;
        bool wantVisible = hasReceived
            && targetVisible
            && net.LocalOwnerId >= 0
            && (Time.unscaledTime - lastReceiveTime) < TimeoutSeconds;

        float dt = Time.unscaledDeltaTime;
        float targetAlpha = wantVisible ? 1f : 0f;
        currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, FadeSpeed * dt);

        if (currentAlpha <= 0f)
        {
            // 完全に透明になったらオブジェクトごと隠す。次に出るときは位置をスナップする
            if (cursorObj != null && cursorObj.activeSelf) cursorObj.SetActive(false);
            shown = false;
            return;
        }

        EnsureCursorObject();
        if (!cursorObj.activeSelf) cursorObj.SetActive(true);

        Vector3 target = new Vector3(targetPos.x, targetPos.y, 0f);
        if (!shown)
        {
            cursorObj.transform.position = target; // 初回表示は補間せずスナップ
            shown = true;
        }
        else if (wantVisible)
        {
            // 指数平滑化（フレームレート非依存）。フェードアウト中は最後の位置に留める
            float k = 1f - Mathf.Exp(-FollowSharpness * dt);
            cursorObj.transform.position = Vector3.Lerp(cursorObj.transform.position, target, k);
        }

        Color baseColor = peerOwnerId == 1 ? OwnerColorOrange : OwnerColorBlue;
        baseColor.a = currentAlpha;
        cursorRenderer.color = baseColor;
    }

    private void EnsureCursorObject()
    {
        if (cursorObj != null) return;

        cursorObj = new GameObject("PeerCursor");
        cursorRenderer = cursorObj.AddComponent<SpriteRenderer>();
        cursorRenderer.sprite = GetOrCreateCursorSprite();
        cursorRenderer.sortingOrder = CursorSortingOrder;
    }

    private void DestroyCursor()
    {
        if (cursorObj != null) Destroy(cursorObj);
        cursorObj = null;
        cursorRenderer = null;
    }

    // 矢印型ポインタのSpriteを実行時に生成する（外部アセット非依存）。
    // 塗りは白（SpriteRenderer.colorで所有者カラーに着色）、輪郭は黒で焼き込む。黒はcolorを乗じても黒のままなので
    // どの背景でも視認できる。ピボットは矢印の先端
    private static Sprite GetOrCreateCursorSprite()
    {
        if (cursorSprite != null) return cursorSprite;

        // 先端を原点、x右・y下の座標系で定義した矢印ポリゴン
        Vector2[] poly =
        {
            new Vector2(0f, 0f), new Vector2(0f, 16f), new Vector2(4f, 12.5f), new Vector2(7f, 18.5f),
            new Vector2(9.5f, 17.3f), new Vector2(6.5f, 11.5f), new Vector2(11.5f, 11.5f),
        };

        bool[] inside = new bool[TexW * TexH]; // 添字: py * TexW + px（pyは上からの行）
        for (int py = 0; py < TexH; py++)
        {
            for (int px = 0; px < TexW; px++)
            {
                Vector2 p = new Vector2(px - TexPad + 0.5f, py - TexPad + 0.5f);
                inside[py * TexW + px] = PointInPolygon(p, poly);
            }
        }

        Texture2D texture = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
        texture.name = "PeerCursorTexture";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        Color32[] pixels = new Color32[TexW * TexH];
        Color32 clear = new Color32(0, 0, 0, 0);
        Color32 white = new Color32(255, 255, 255, 255);
        Color32 black = new Color32(0, 0, 0, 255);
        for (int py = 0; py < TexH; py++)
        {
            for (int px = 0; px < TexW; px++)
            {
                Color32 c;
                if (inside[py * TexW + px])
                {
                    c = white;
                }
                else
                {
                    // 塗りに8近傍で接していれば輪郭（1px）
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx, ny = py + dy;
                            if (nx < 0 || ny < 0 || nx >= TexW || ny >= TexH) continue;
                            if (inside[ny * TexW + nx]) { edge = true; break; }
                        }
                    }
                    c = edge ? black : clear;
                }
                // Texture2Dは下から上の行順
                pixels[(TexH - 1 - py) * TexW + px] = c;
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        // ピボット = 矢印の先端（テクスチャ上でx=TexPad, 上からTexPad）
        Vector2 pivot = new Vector2((float)TexPad / TexW, (float)(TexH - TexPad) / TexH);
        cursorSprite = Sprite.Create(texture, new Rect(0, 0, TexW, TexH), pivot, PixelsPerUnit);
        return cursorSprite;
    }

    // 偶奇規則による点のポリゴン内外判定
    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool result = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                result = !result;
            }
        }
        return result;
    }
}
