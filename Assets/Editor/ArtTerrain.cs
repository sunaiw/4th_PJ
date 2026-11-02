using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using ArtGen;
using static ArtGen.Painter;

// 地形・コア/スポナー・UI用スプライト・タイトル背景の生成。
public static class ArtTerrain
{
    const string ArtRoot = "Assets/Art";
    const string UIRoot = "Assets/Resources/Art/UI";

    // ---------- 画像読み込み ----------
    public static Img LoadImg(string assetPath)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
        Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(bytes);
        Img im = new Img(t.width, t.height);
        System.Array.Copy(t.GetPixels(), im.P, im.P.Length);
        Object.DestroyImmediate(t);
        return im;
    }

    static Img Half(Img src)
    {
        Img o = new Img(src.W / 2, src.H / 2);
        for (int y = 0; y < o.H; y++)
            for (int x = 0; x < o.W; x++)
            {
                Color a = src.P[(y * 2) * src.W + x * 2], b = src.P[(y * 2) * src.W + x * 2 + 1];
                Color c = src.P[(y * 2 + 1) * src.W + x * 2], d = src.P[(y * 2 + 1) * src.W + x * 2 + 1];
                o.P[y * o.W + x] = (a + b + c + d) * 0.25f;
            }
        return o;
    }

    static Img Resize(Img src, int w, int h)
    {
        Img o = new Img(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * src.W - 0.5f, v = (y + 0.5f) / h * src.H - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, src.W - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, src.H - 1);
                int x1 = Mathf.Min(x0 + 1, src.W - 1), y1 = Mathf.Min(y0 + 1, src.H - 1);
                float fx = Mathf.Clamp01(u - x0), fy = Mathf.Clamp01(v - y0);
                Color c = Color.Lerp(Color.Lerp(src.P[y0 * src.W + x0], src.P[y0 * src.W + x1], fx), Color.Lerp(src.P[y1 * src.W + x0], src.P[y1 * src.W + x1], fx), fy);
                o.P[y * w + x] = c;
            }
        return o;
    }

    // 縮小用: 2の冪倍率ならBoxで、そうでなければ段階的に半分にしてから補間
    static Img Downscale(Img src, int size)
    {
        Img cur = src;
        while (cur.W / 2 >= size) cur = Half(cur);
        return cur.W == size ? cur : Resize(cur, size, size);
    }

    // ---------- Geminiの透かし除去(別の位置のパッチでフェザー合成) ----------
    static void RemoveWatermark(Img im, int cx, int cy, int radius, int srcDx, int srcDy)
    {
        for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= im.W || y >= im.H) continue;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1f) continue;
                float w = Mathf.Clamp01((1f - d) * 3f);
                int sx = Mathf.Clamp(x + srcDx, 0, im.W - 1), sy = Mathf.Clamp(y + srcDy, 0, im.H - 1);
                Color s = im.P[sy * im.W + sx];
                im.P[y * im.W + x] = Color.Lerp(im.P[y * im.W + x], s, w);
            }
    }

    // 継ぎ目の目立ち度(端の列と、内側の隣接列との差の比)。1に近ければシームレス
    static float SeamRatio(Img im)
    {
        double edge = 0, inner = 0;
        for (int y = 0; y < im.H; y++)
        {
            Color a = im.P[y * im.W], b = im.P[y * im.W + im.W - 1], c = im.P[y * im.W + 1], e = im.P[y * im.W + im.W / 2];
            Color f = im.P[y * im.W + im.W / 2 + 1];
            edge += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
            inner += Mathf.Abs(e.r - f.r) + Mathf.Abs(e.g - f.g) + Mathf.Abs(e.b - f.b);
            inner += Mathf.Abs(a.r - c.r) + Mathf.Abs(a.g - c.g) + Mathf.Abs(a.b - c.b);
        }
        double h = edge / (inner / 2 + 1e-6);
        edge = 0; inner = 0;
        for (int x = 0; x < im.W; x++)
        {
            Color a = im.P[x], b = im.P[(im.H - 1) * im.W + x], c = im.P[im.W + x], e = im.P[(im.H / 2) * im.W + x], f = im.P[(im.H / 2 + 1) * im.W + x];
            edge += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
            inner += Mathf.Abs(e.r - f.r) + Mathf.Abs(e.g - f.g) + Mathf.Abs(e.b - f.b);
            inner += Mathf.Abs(a.r - c.r) + Mathf.Abs(a.g - c.g) + Mathf.Abs(a.b - c.b);
        }
        double v = edge / (inner / 2 + 1e-6);
        return (float)System.Math.Max(h, v);
    }

    // 半周期ずらした画像とクロスフェードして継ぎ目を消す
    static Img MakeSeamless(Img im, float band)
    {
        Img o = new Img(im.W, im.H);
        for (int y = 0; y < im.H; y++)
            for (int x = 0; x < im.W; x++)
            {
                float ux = Mathf.Abs((x + 0.5f) / im.W * 2f - 1f), uy = Mathf.Abs((y + 0.5f) / im.H * 2f - 1f);
                float wx = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - ux) / band));
                float wy = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - uy) / band));
                float w = wx * wy;
                Color a = im.P[y * im.W + x];
                Color b = im.P[((y + im.H / 2) % im.H) * im.W + (x + im.W / 2) % im.W];
                o.P[y * im.W + x] = Color.Lerp(b, a, w);
            }
        return o;
    }


    // ---------- 周期ノイズ/石畳 ----------
    // 周期P(格子数)でラップする値ノイズ。x,yは[0,P)の連続値
    static float WNoise(float x, float y, int period, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = ((xi % period) + period) % period, x1 = (x0 + 1) % period;
        int y0 = ((yi % period) + period) % period, y1 = (y0 + 1) % period;
        float a = Painter.Rand(x0, y0, seed), b = Painter.Rand(x1, y0, seed), c = Painter.Rand(x0, y1, seed), d = Painter.Rand(x1, y1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // u,vは[0,1)のテクスチャ座標。baseFreqは1周期あたりの最低周波数(整数)
    static float WFbm(float u, float v, int baseFreq, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        int f = baseFreq;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * WNoise(u * f, v * f, f, seed + i * 17);
            norm += amp; amp *= 0.5f; f *= 2;
        }
        return sum / norm;
    }

    static void GenerateForestNoise()
    {
        const int N = 256;
        Img im = new Img(N, N);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float n = WFbm((x + 0.5f) / N, (y + 0.5f) / N, 4, 5, 41);
                im.P[y * N + x] = new Color(n, n, n, 1f);
            }
        string path = ArtRoot + "/Terrain/ForestNoise.png";
        ArtGenerator.WritePng(im, path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false; // 値データなのでガンマ変換しない
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // ラップするVoronoiによる丸い石畳。n個/周期。低コントラストの冷たい暗めトーン
    // 周期境界つきの分離ボックスブラー(半径r)
    static void WrapBox(float[] src, float[] dst, int size, int r)
    {
        float[] tmp = new float[size * size];
        float inv = 1f / (2 * r + 1);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += src[y * size + ((x + k) % size + size) % size];
                tmp[y * size + x] = sum * inv;
            }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += tmp[(((y + k) % size + size) % size) * size + x];
                dst[y * size + x] = sum * inv;
            }
    }

    static Img MakeCobblestones(int size, int n, int seed)
    {
        float cell = size / (float)n;
        float[] px = new float[n * n], py = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                // 行ごとに少しずらして石畳らしい並びにする
                float off = (j % 2 == 0) ? 0f : 0.25f;
                px[j * n + i] = (i + 0.5f + off + (Painter.Rand(i, j, seed) - 0.5f) * 0.45f) * cell;
                py[j * n + i] = (j + 0.5f + (Painter.Rand(i, j, seed + 5) - 0.5f) * 0.45f) * cell;
            }

        float[] h = new float[size * size];
        Color[] baseCol = new Color[size * size];
        float[] gap = new float[size * size];
        float[] blurTmp = new float[size * size];

        const float gapW = 2.6f, bevel = 8f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float f1 = 1e9f, f2 = 1e9f; int id = 0; float nx = 0, ny = 0;
                int ci = Mathf.FloorToInt(x / cell), cj = Mathf.FloorToInt(y / cell);
                for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int gi = ci + di, gj = cj + dj;
                        int wi = ((gi % n) + n) % n, wj = ((gj % n) + n) % n;
                        float wx = px[wj * n + wi] + (gi - wi) * cell, wy = py[wj * n + wi] + (gj - wj) * cell;
                        float dx = x + 0.5f - wx, dy = y + 0.5f - wy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < f1) { f2 = f1; f1 = d; id = wj * n + wi; nx = wx; ny = wy; }
                        else if (d < f2) f2 = d;
                    }
                float edge = (f2 - f1) * 0.5f; // 境界までのおおよその距離
                int idx = y * size + x;
                h[idx] = edge > gapW ? 1f : 0f; // 後でブラーして角を丸める

                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float rb = Painter.Rand(id, 0, seed + 11), rh = Painter.Rand(id, 1, seed + 12);
                float bright = 0.88f + 0.22f * rb;
                float blotch = (WFbm(u, v, 24, 2, seed + 3) - 0.5f) * 0.14f;
                float fine = (WFbm(u, v, 128, 3, seed + 4) - 0.5f) * 0.12f;
                float m = bright + blotch + fine;
                // 石ごとの色相の揺れ(青灰〜やや暖色)
                Color stone = new Color(0.47f + (rh - 0.5f) * 0.04f, 0.485f, 0.54f - (rh - 0.5f) * 0.035f);
                baseCol[idx] = stone * m;
            }

        // 二値の石マスクを2回ブラーして角を丸める(Voronoiの尖った角を丸い石にする)
        WrapBox(h, blurTmp, size, 4); WrapBox(blurTmp, h, size, 4);
        for (int i = 0; i < h.Length; i++)
        {
            float bl = h[i];
            gap[i] = 1f - Mathf.SmoothStep(0.30f, 0.52f, bl);
            h[i] = Mathf.SmoothStep(0.35f, 1.0f, bl); // 縁ほど低いドーム状の高さ
        }

        Img im = new Img(size, size);
        Color mortar = new Color(0.115f, 0.12f, 0.145f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int idx = y * size + x;
                int xm = (x - 1 + size) % size, xp = (x + 1) % size, ym = (y - 1 + size) % size, yp = (y + 1) % size;
                float gx = h[y * size + xp] - h[y * size + xm];
                float gy = h[yp * size + x] - h[ym * size + x];
                float k = 1f + (-gx - gy) * 1.3f; // 左上から光を当てる
                Color c = baseCol[idx];
                float dome = k * (0.90f + 0.10f * h[idx]);
                c = new Color(c.r * dome, c.g * dome, c.b * dome);

                // 目地(モルタル)と一部の苔
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float g = gap[idx];
                if (g > 0f)
                {
                    Color mc = mortar * (0.85f + 0.5f * WFbm(u, v, 96, 2, 21));
                    float moss = WFbm(u, v, 10, 3, 33);
                    if (moss > 0.58f && g > 0.55f)
                    {
                        float mk = Mathf.Clamp01((moss - 0.58f) * 7f) * Mathf.SmoothStep(0.55f, 1f, g);
                        mc = Color.Lerp(mc, new Color(0.14f, 0.24f, 0.11f) * (0.8f + 0.5f * WFbm(u, v, 128, 2, 22)), mk * 0.85f);
                    }
                    c = Color.Lerp(c, mc, Mathf.SmoothStep(0.15f, 0.85f, g));
                }
                c.a = 1f;
                im.P[idx] = c;
            }
        return im;
    }

    // ForestEdgeシェーダのマテリアル(Resources配下=ビルドに含まれる。シェーダ/テクスチャへのシリアライズ参照を持つ)
    public static void CreateForestMaterial()
    {
        UnityEngine.Shader shader = AssetDatabase.LoadAssetAtPath<UnityEngine.Shader>("Assets/Art/Shaders/ForestEdge.shader");
        string path = "Assets/Resources/Art/ForestEdge.mat";
        ArtGenerator.EnsureDir("Assets/Resources/Art");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetTexture("_ForestTex", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + "/Terrain/Forest.png"));
        mat.SetTexture("_Noise", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + "/Terrain/ForestNoise.png"));
        mat.SetFloat("_NoiseAmp", 0.8f);
        mat.SetFloat("_ShadowStrength", 0.3f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
    }

    // ---------- 地形 ----------
    [MenuItem("Tools/Art/Generate Terrain")]
    public static void GenerateTerrain()
    {
        // 地面(石畳): 元画像の拡大ではなく、ラップするVoronoiで手続き生成する(16セル周期に約22個=1個が約0.7セル)
        float seamG = 1f;
        Img g1 = MakeCobblestones(1024, 22, 7);
        ArtGenerator.WritePng(g1, ArtRoot + "/Terrain/Ground.png");

        // 森(壁)
        Img f = LoadImg(ArtRoot + "/../Gemini_Generated_Image_sgj7mqsgj7mqsgj7.png");
        RemoveWatermark(f, 1818, 246, 120, -620, 20);
        float seamF = SeamRatio(f);
        Img f1 = Downscale(MakeSeamless(f, 0.30f), 1024);
        // 道より暗くして歩ける領域を際立たせる
        f1.Map(c => new Color(c.r * 0.48f, c.g * 0.52f, c.b * 0.55f, 1f));
        ArtGenerator.WritePng(f1, ArtRoot + "/Terrain/Forest.png");

        AssetDatabase.Refresh();
        foreach (string p in new[] { ArtRoot + "/Terrain/Ground.png", ArtRoot + "/Terrain/Forest.png" })
        {
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(p);
            ti.textureType = TextureImporterType.Default;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat; // ForestEdgeシェーダがワールド座標/16でサンプルするため
            ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.maxTextureSize = 1024;
            ti.SaveAndReimport();
        }
        Debug.Log($"[ArtTerrain] seam ratio ground={seamG:F2} forest={seamF:F2}");

        GenerateForestNoise();
        CreateRegionTile(ArtRoot + "/Terrain/Ground.png", ArtRoot + "/Terrain/GroundRegionTile.asset", "Assets/Gemini_Generated_Image_3bjlx63bjlx63bjl_0.asset");
        CreateRegionTile(ArtRoot + "/Terrain/Forest.png", ArtRoot + "/Terrain/ForestRegionTile.asset", "Assets/Gemini_Generated_Image_sgj7mqsgj7mqsgj7_0.asset");
        CreateForestMaterial();
    }

    static void CreateRegionTile(string texPath, string assetPath, string oldTilePath)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        Tile old = AssetDatabase.LoadAssetAtPath<Tile>(oldTilePath);
        RegionTile rt = AssetDatabase.LoadAssetAtPath<RegionTile>(assetPath);
        if (rt == null)
        {
            rt = ScriptableObject.CreateInstance<RegionTile>();
            AssetDatabase.CreateAsset(rt, assetPath);
        }
        rt.Configure(tex, 16, old != null ? old.colliderType : Tile.ColliderType.Sprite, old != null ? old.flags : TileFlags.LockColor);
        EditorUtility.SetDirty(rt);
        AssetDatabase.SaveAssets();
    }

    // タイルマップ上の旧タイルを新タイルへ全セル一括で差し替える
    public static string SwapTilemapTiles(Tilemap map, TileBase newTile)
    {
        TileBase[] used = new TileBase[map.GetUsedTilesCount()];
        map.GetUsedTilesNonAlloc(used);
        int swapped = 0;
        foreach (TileBase t in used)
        {
            if (t == null || t == newTile) continue;
            map.SwapTile(t, newTile);
            swapped++;
        }
        map.RefreshAllTiles();
        EditorUtility.SetDirty(map);
        return $"{map.name}: swapped {swapped} tile type(s), bounds {map.cellBounds}, tiles now {map.GetUsedTilesCount()} type(s)";
    }

    // ---------- コア/スポナー(円形マスクで正方形の暗い縁をなじませる) ----------
    [MenuItem("Tools/Art/Generate Structures")]
    public static void GenerateStructures()
    {
        MakeStructure("Core.png", "Core_Round.png");
        MakeStructure("EnemySpawner.png", "EnemySpawner_Round.png");
    }

    static void MakeStructure(string srcName, string dstName)
    {
        Img src = LoadImg("Assets/" + srcName);
        Img im = Downscale(src, 512);
        for (int y = 0; y < im.H; y++)
            for (int x = 0; x < im.W; x++)
            {
                float dx = (x + 0.5f) / im.W - 0.5f, dy = (y + 0.5f) / im.H - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.405f) / 0.095f));
                Color c = im.P[y * im.W + x];
                c.a = a;
                im.P[y * im.W + x] = c;
            }
        // 元と同じワールドサイズ(2048px/2048ppu=1ユニット)を保つ: 512px/512ppu
        ArtGenerator.SaveSprite(im, ArtRoot + "/Structures/" + dstName, 512f);
    }

    // ---------- UIスプライト ----------
    static void SaveUI(Img im, string name, float ppu, Vector4 border)
    {
        string path = UIRoot + "/" + name + ".png";
        ArtGenerator.WritePng(im, path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = ppu;
        ti.spriteBorder = border;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        TextureImporterSettings s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(s);
        ti.SaveAndReimport();
    }

    static readonly Color GoldDark = new Color(0.30f, 0.17f, 0.03f, 1f);

    [MenuItem("Tools/Art/Generate UI")]
    public static void GenerateUI()
    {
        // パネル本体: 明るい無彩色(Image.colorで着色して使う)
        {
            Img im = new Img(128, 128);
            Sdf b = Box(0, 0, 64, 64, 26);
            im.Paint(b, new Mat(C(1f, 1f, 1f), C(0.80f, 0.80f, 0.83f)).Range(-64, 64).Bev(9, 0.14f).Out(0, GoldDark).Nz(0.025f, 0.12f, 3));
            SaveUI(im, "UI_Panel", 200f, new Vector4(44, 44, 44, 44));
        }
        // パネルの金縁(縁のみ)
        {
            Img im = new Img(128, 128);
            Sdf outer = Box(0, 0, 64, 64, 26);
            Sdf inner = Box(0, 0, 58.5f, 58.5f, 20.5f);
            im.Paint(Sub(outer, inner), new Mat(C(1f, 0.90f, 0.55f), C(0.72f, 0.5f, 0.16f)).Range(-64, 64).Bev(3, 0.35f).Out(1.4f, GoldDark));
            SaveUI(im, "UI_PanelFrame", 200f, new Vector4(44, 44, 44, 44));
        }
        // ボタン本体
        {
            Img im = new Img(128, 64);
            Sdf b = Box(0, 0, 64, 32, 16);
            im.Paint(b, new Mat(C(1f, 1f, 1f), C(0.72f, 0.72f, 0.78f)).Range(-32, 32).Bev(8, 0.22f).Out(0, GoldDark).Nz(0.02f, 0.12f, 5));
            SaveUI(im, "UI_Button", 200f, new Vector4(32, 28, 32, 28));
        }
        // ボタンの縁
        {
            Img im = new Img(128, 64);
            Sdf outer = Box(0, 0, 64, 32, 16);
            Sdf inner = Box(0, 0, 60.5f, 28.5f, 12.5f);
            im.Paint(Sub(outer, inner), new Mat(C(1f, 0.9f, 0.6f), C(0.7f, 0.48f, 0.14f)).Range(-32, 32).Bev(2, 0.3f).Out(1.2f, GoldDark));
            SaveUI(im, "UI_ButtonFrame", 200f, new Vector4(32, 28, 32, 28));
        }
        // ゲージの背景(丸い溝)
        {
            Img im = new Img(64, 32);
            Sdf b = Box(0, 0, 32, 16, 16);
            im.Paint(b, new Mat(C(0.80f, 0.80f, 0.84f), C(1f, 1f, 1f)).Range(-16, 16).Bev(6, -0.25f).Out(0, GoldDark));
            SaveUI(im, "UI_Bar", 200f, new Vector4(15, 15, 15, 15));
        }
        // ゲージの縁
        {
            Img im = new Img(64, 32);
            Sdf outer = Box(0, 0, 32, 16, 16);
            Sdf inner = Box(0, 0, 29.5f, 13.5f, 13.5f);
            im.Paint(Sub(outer, inner), new Mat(C(1f, 0.88f, 0.55f), C(0.62f, 0.42f, 0.12f)).Range(-16, 16).Bev(2, 0.3f).Out(1f, GoldDark));
            SaveUI(im, "UI_BarFrame", 200f, new Vector4(15, 15, 15, 15));
        }
        // 区切り線の装飾(ロゴ下など)
        {
            Img im = new Img(256, 16);
            im.Paint(Box(0, 0, 124, 2.5f, 2.5f), new Mat(C(1f, 0.9f, 0.55f), C(0.7f, 0.48f, 0.14f)).Range(-3, 3).Bev(0, 0).Out(0, GoldDark));
            im.Paint(Poly(0, 8, 9, 0, 0, -8, -9, 0), new Mat(C(1f, 0.95f, 0.7f), C(0.75f, 0.5f, 0.15f)).Range(-8, 8).Bev(3, 0.35f).Out(1.2f, GoldDark));
            im.MulAlpha((x, y) => 1f - Mathf.Pow(Mathf.Abs(x) / 128f, 3f));
            SaveUI(im, "UI_Divider", 100f, Vector4.zero);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ArtTerrain] UI sprites generated.");
    }

    // ---------- タイトル背景 ----------
    [MenuItem("Tools/Art/Generate Title Background")]
    public static void GenerateTitleBackground()
    {
        const int W = 1280, H = 720;
        Img forest = LoadImg(ArtRoot + "/Terrain/Forest.png"); // 1024 (既に暗め)
        Img core = LoadImg(ArtRoot + "/Structures/Core_Round.png");
        Img spawner = LoadImg(ArtRoot + "/Structures/EnemySpawner_Round.png");
        Img bg = new Img(W, H);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // 森を拡大して敷く(1024px=1.5画面幅 → 拡大率1.0)
                int sx = (int)((x * 0.9f) % 1024), sy = (int)((y * 0.9f + 200) % 1024);
                Color c = forest.P[sy * 1024 + sx];
                c.a = 1f;
                bg.P[y * W + x] = c;
            }
        // 中央を通る石畳の道(暗い帯)
        Img road = LoadImg(ArtRoot + "/Terrain/Ground.png");
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cy = H * 0.5f + Mathf.Sin(x * 0.006f) * 26f - 40f;
                float d = Mathf.Abs(y - cy);
                float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 66f) / 30f));
                if (a <= 0f) continue;
                Color r = road.P[((y * 1) % 1024) * 1024 + (x % 1024)];
                r *= 0.62f; r.a = 1f;
                bg.P[y * W + x] = Color.Lerp(bg.P[y * W + x], r, a);
            }
        // 左にスポナー、右にコア
        Blit(bg, Resize(spawner, 360, 360), 230, 300);
        Blit(bg, Resize(core, 360, 360), 1050, 300);
        // 光
        Img light = new Img(W, H);
        light.Glow(1050 - W / 2f, 300 - H / 2f, 380, C(0.25f, 0.55f, 1f), 0.5f, 1.8f);
        light.Glow(230 - W / 2f, 300 - H / 2f, 380, C(0.65f, 0.2f, 1f), 0.5f, 1.8f);
        for (int i = 0; i < bg.P.Length; i++) bg.P[i] = Img.Over(bg.P[i], light.P[i], 1f);
        // ビネットと上下の暗幕
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = (x / (float)W - 0.5f) * 2f, dy = (y / (float)H - 0.5f) * 2f;
                float v = Mathf.Clamp01((Mathf.Sqrt(dx * dx * 0.7f + dy * dy) - 0.45f) / 0.9f);
                float top = Mathf.Clamp01((y / (float)H - 0.55f) / 0.45f);
                float k = Mathf.Clamp01(v * 0.85f + top * 0.45f);
                Color c = bg.P[y * W + x];
                bg.P[y * W + x] = Color.Lerp(c, new Color(0.02f, 0.03f, 0.06f, 1f), k);
            }
        ArtGenerator.SaveSprite(bg, UIRoot + "/TitleBackground.png", 100f, false);
        Debug.Log("[ArtTerrain] Title background generated.");
    }

    static void Blit(Img dst, Img src, int cx, int cy)
    {
        int ox = cx - src.W / 2, oy = cy - src.H / 2;
        for (int y = 0; y < src.H; y++)
            for (int x = 0; x < src.W; x++)
            {
                int dx = ox + x, dy = oy + y;
                if (dx < 0 || dy < 0 || dx >= dst.W || dy >= dst.H) continue;
                dst.P[dy * dst.W + dx] = Img.Over(dst.P[dy * dst.W + dx], src.P[y * src.W + x], 1f);
            }
    }
}
