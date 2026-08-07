using UnityEngine;

// プレイフィールド全体が、HUDの上下バーに隠れることなく画面内へ完全に収まるよう、
// カメラのorthographicSizeと位置を実行時に自動調整する。
// 解像度・アスペクト比が変わった場合も追随する。
[RequireComponent(typeof(Camera))]
public class CameraFitter : MonoBehaviour
{
    // フィールド外周が画面端に密着しないよう、わずかに余白を持たせる倍率
    [SerializeField] private float margin = 1.02f;

    private Camera targetCamera;
    private int lastScreenWidth;
    private int lastScreenHeight;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        Fit();
    }

    private void Update()
    {
        // 解像度・ウィンドウサイズの変更に追随する
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            Fit();
        }
    }

    public void Fit()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        if (targetCamera == null || !targetCamera.orthographic) return;
        if (MapManager.Instance == null) return;

        Bounds fieldBounds = MapManager.Instance.GetFieldWorldBounds();
        if (fieldBounds.size.x <= 0f || fieldBounds.size.y <= 0f) return;

        // 横方向の要件: 画面幅にフィールド幅が収まる最小のorthographicSize
        float aspect = targetCamera.aspect;
        float sizeForWidth = aspect > 0f ? (fieldBounds.size.x * 0.5f) / aspect : 0f;

        // 縦方向の要件: HUDバーに覆われない中央帯にフィールド高が収まる最小のorthographicSize
        float usableHeightRatio = Mathf.Clamp(1f - 2f * GetHudBarScreenRatio(), 0.1f, 1f);
        float sizeForHeight = (fieldBounds.size.y * 0.5f) / usableHeightRatio;

        targetCamera.orthographicSize = Mathf.Max(sizeForWidth, sizeForHeight) * margin;
        transform.position = new Vector3(fieldBounds.center.x, fieldBounds.center.y, transform.position.z);
    }

    // HUDバー1本が画面高に占める割合を返す。
    // HUDCanvasは ScaleWithScreenSize / 基準解像度1920x1080 / matchWidthOrHeight = 0.5 のため、
    // スケール係数は幅と高さの比率の幾何平均になる。
    private float GetHudBarScreenRatio()
    {
        if (Screen.height <= 0) return 0f;
        float scaleFactor = Mathf.Sqrt((Screen.width / 1920f) * (Screen.height / 1080f));
        return (HUDManager.BarHeight * scaleFactor) / Screen.height;
    }
}
