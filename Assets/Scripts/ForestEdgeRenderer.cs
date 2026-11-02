using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 壁(森)を「WallTilemapのセル単位の描画」ではなく、1枚の大きなクアッド＋マスクシェーダで描く。
/// WallTilemapはロジック(HasTile)の情報源として残し、そのTilemapRendererだけを実行時に無効化する
/// （シーンへ保存されるenabledは変更しないので、エディタのシーンビューは従来のまま）。
/// マスクはWallTilemap/GroundTilemapから生成した低解像度テクスチャで、タイル変更
/// （RemoveWall/ExpandMap/SealOutsideField/セーブ読込/CO-OP同期）を Tilemap.tilemapTileChanged で検知して
/// LateUpdateで1度だけ再構築する。MapManagerから生成される。
/// </summary>
public class ForestEdgeRenderer : MonoBehaviour
{
    private const string MaterialResourcePath = "Art/ForestEdge"; // Resources内のMaterial(シェーダ参照を含むのでビルドに含まれる)
    private const int TexelsPerCell = 4;
    private const int MarginCells = 6;       // 地面タイルマップ範囲の外側に確保する(森として扱う)マージン
    private const float QuadHalfExtent = 80f; // 超ワイド画面でも覆える十分大きなクアッド

    private Tilemap wallTilemap;
    private Tilemap groundTilemap;
    private TilemapRenderer wallRenderer;
    private bool wallRendererWasEnabled;

    private Material material;
    private Texture2D maskTexture;
    private MeshRenderer meshRenderer;
    private bool dirty = true;
    private bool subscribed;

    public static ForestEdgeRenderer Create(GameObject host, Tilemap wall, Tilemap ground)
    {
        if (wall == null || ground == null) return null;
        Material shared = Resources.Load<Material>(MaterialResourcePath);
        if (shared == null)
        {
            Debug.LogWarning("[ForestEdgeRenderer] Material not found. Falling back to the standard tilemap renderer.");
            return null;
        }

        ForestEdgeRenderer r = host.AddComponent<ForestEdgeRenderer>();
        r.Init(wall, ground, shared);
        return r;
    }

    private void Init(Tilemap wall, Tilemap ground, Material shared)
    {
        wallTilemap = wall;
        groundTilemap = ground;
        material = new Material(shared); // 実行時パラメータ(マスク)用のインスタンス

        wallRenderer = wallTilemap.GetComponent<TilemapRenderer>();
        BuildQuad();

        if (wallRenderer != null)
        {
            wallRendererWasEnabled = wallRenderer.enabled;
            wallRenderer.enabled = false; // 描画だけ止める。HasTile等のロジックはそのまま
        }

        Tilemap.tilemapTileChanged += OnTilemapTileChanged;
        subscribed = true;
        dirty = true;
        RebuildMask();
    }

    private void BuildQuad()
    {
        GameObject quadObj = new GameObject("ForestEdgeQuad");
        quadObj.transform.SetParent(transform, false);
        quadObj.transform.position = Vector3.zero;

        Mesh mesh = new Mesh { name = "ForestEdgeQuad" };
        float e = QuadHalfExtent;
        mesh.vertices = new[] { new Vector3(-e, -e, 0), new Vector3(e, -e, 0), new Vector3(e, e, 0), new Vector3(-e, e, 0) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(e * 2f, e * 2f, 1f));

        quadObj.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = quadObj.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // 地面(1)より上、スポナー(2)・コア(3)・タワー/敵/弾(4,5)より下
        if (wallRenderer != null) meshRenderer.sortingLayerID = wallRenderer.sortingLayerID;
        meshRenderer.sortingOrder = 2;
    }

    private void OnTilemapTileChanged(Tilemap tilemap, Tilemap.SyncTile[] tiles)
    {
        if (tilemap == wallTilemap || tilemap == groundTilemap) dirty = true;
    }

    private void LateUpdate()
    {
        if (dirty) RebuildMask();
    }

    private void OnDestroy()
    {
        if (subscribed) Tilemap.tilemapTileChanged -= OnTilemapTileChanged;
        subscribed = false;
        if (wallRenderer != null) wallRenderer.enabled = wallRendererWasEnabled;
        if (maskTexture != null) Destroy(maskTexture);
        if (material != null) Destroy(material);
    }

    // 森(=壁のあるセル、または地面タイルの無いセル)を1、通路を0としたマスクを作る
    private void RebuildMask()
    {
        dirty = false;
        if (wallTilemap == null || groundTilemap == null) return;

        BoundsInt gb = groundTilemap.cellBounds;
        int minX = gb.xMin - MarginCells, minY = gb.yMin - MarginCells;
        int cellsX = gb.size.x + MarginCells * 2, cellsY = gb.size.y + MarginCells * 2;
        int w = cellsX * TexelsPerCell, h = cellsY * TexelsPerCell;

        float[] raw = new float[w * h];
        for (int cy = 0; cy < cellsY; cy++)
            for (int cx = 0; cx < cellsX; cx++)
            {
                Vector3Int cell = new Vector3Int(minX + cx, minY + cy, 0);
                bool forest = wallTilemap.HasTile(cell) || !groundTilemap.HasTile(cell);
                if (!forest) continue;
                for (int ty = 0; ty < TexelsPerCell; ty++)
                    for (int tx = 0; tx < TexelsPerCell; tx++)
                        raw[(cy * TexelsPerCell + ty) * w + cx * TexelsPerCell + tx] = 1f;
            }

        // 3x3ボックスブラーを2回(縁を約1セル幅のなだらかな勾配にし、角を丸める)
        float[] tmp = new float[w * h];
        Blur(raw, tmp, w, h);
        Blur(tmp, raw, w, h);

        if (maskTexture == null || maskTexture.width != w || maskTexture.height != h)
        {
            if (maskTexture != null) Destroy(maskTexture);
            maskTexture = new Texture2D(w, h, TextureFormat.R8, false, true)
            {
                name = "ForestMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }
        byte[] bytes = new byte[w * h];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(raw[i] * 255f), 0, 255);
        maskTexture.SetPixelData(bytes, 0);
        maskTexture.Apply(false);

        Vector3 origin = groundTilemap.CellToWorld(new Vector3Int(minX, minY, 0));
        Vector3 cellSize = groundTilemap.cellSize;
        material.SetTexture("_Mask", maskTexture);
        material.SetVector("_MaskParams", new Vector4(origin.x, origin.y, 1f / (cellsX * cellSize.x), 1f / (cellsY * cellSize.y)));
    }

    // 範囲外は端の値を繰り返す(端は常に森なので森のまま)
    private static void Blur(float[] src, float[] dst, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = Mathf.Clamp(y + dy, 0, h - 1);
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = Mathf.Clamp(x + dx, 0, w - 1);
                        sum += src[yy * w + xx];
                    }
                }
                dst[y * w + x] = sum / 9f;
            }
    }
}
