using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ArtGen;

// ビジュアル刷新用のアート生成ツール。Tools/Art/ 以下のメニューから実行する。
// すべて固定シードの手続き生成なので、何度実行しても同じ画像になる。
public static class ArtGenerator
{
    const string ArtRoot = "Assets/Art";
    const string ResRoot = "Assets/Resources/Art";
    public const string SheetDir = @"C:\Users\Sun\AppData\Local\Temp\claude\c--Users-Sun-PJ-Unity-project-4th-PJ\b29f395a-8de2-4512-974e-573be258a7d7\scratchpad";

    // ---------- 出力ヘルパー ----------
    public static void EnsureDir(string assetDir)
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), assetDir);
        if (!Directory.Exists(full)) Directory.CreateDirectory(full);
    }

    public static void WritePng(Img im, string assetPath)
    {
        EnsureDir(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
        Texture2D t = new Texture2D(im.W, im.H, TextureFormat.RGBA32, false);
        t.SetPixels(im.P);
        t.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), assetPath), t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    // スプライトとして出力し、インポート設定を整える。ppuはワールド上の大きさを決める
    public static Sprite SaveSprite(Img im, string assetPath, float ppu, bool mips = true)
    {
        WritePng(im, assetPath);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = ppu;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = mips;
        ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 2048;
        TextureImporterSettings s = new TextureImporterSettings();
        ti.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        s.spriteAlignment = (int)SpriteAlignment.Center;
        ti.SetTextureSettings(s);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    // ---------- スプライト一式 ----------
    [MenuItem("Tools/Art/Generate Sprites")]
    public static void GenerateSprites()
    {
        var sheet = new List<KeyValuePair<string, Img>>();
        void Add(string name, Img im, string path, float ppu = 256f)
        {
            SaveSprite(im, path, ppu);
            sheet.Add(new KeyValuePair<string, Img>(name, im));
        }

        Img t = Sprites.Tower(), tk = Sprites.TankTower(), h = Sprites.Healer(), sp = Sprites.SplashTower(), f = Sprites.FrostTower(), b = Sprites.Barricade();
        Add("Tower", t, ArtRoot + "/Towers/Tower.png");
        Add("TankTower", tk, ArtRoot + "/Towers/TankTower.png");
        Add("Healer", h, ArtRoot + "/Towers/Healer.png");
        Add("SplashTower", sp, ArtRoot + "/Towers/SplashTower.png");
        Add("FrostTower", f, ArtRoot + "/Towers/FrostTower.png");
        Add("Barricade", b, ArtRoot + "/Towers/Barricade.png");
        Add("Tower_Off", Sprites.Offline(t), ArtRoot + "/Towers/Tower_Offline.png");
        Add("TankTower_Off", Sprites.Offline(tk), ArtRoot + "/Towers/TankTower_Offline.png");
        Add("Healer_Off", Sprites.Offline(h), ArtRoot + "/Towers/Healer_Offline.png");
        Add("SplashTower_Off", Sprites.Offline(sp), ArtRoot + "/Towers/SplashTower_Offline.png");
        Add("FrostTower_Off", Sprites.Offline(f), ArtRoot + "/Towers/FrostTower_Offline.png");
        Add("OwnerSilhouette", Sprites.OwnerSilhouette(), ArtRoot + "/Towers/TowerOwnerSilhouette.png");

        Add("Enemy", Fit(Sprites.Enemy(), 118f, 1.35f), ArtRoot + "/Enemies/Enemy.png");
        Add("Enemy2", Fit(Sprites.Enemy2(), 118f, 1.35f), ArtRoot + "/Enemies/Enemy2.png");
        Add("Enemy3", Fit(Sprites.Enemy3(), 118f, 1.2f), ArtRoot + "/Enemies/Enemy3.png");
        Add("Enemy4", Fit(Sprites.Enemy4(), 118f, 1.3f), ArtRoot + "/Enemies/Enemy4.png");
        Add("Enemy5", Fit(Sprites.Enemy5(), 118f, 1.2f), ArtRoot + "/Enemies/Enemy5.png");
        Add("Boss", Sprites.Boss(), ResRoot + "/Boss.png");
        Add("Buster", Sprites.BarricadeBuster(), ResRoot + "/BarricadeBuster.png");
        Add("Siege", Sprites.SiegeMarker(), ResRoot + "/SiegeMarker.png");

        Add("Bullet", Sprites.Bullet(), ArtRoot + "/Bullets/Bullet.png");
        Add("EnemyBullet", Sprites.EnemyBullet(), ArtRoot + "/Bullets/EnemyBullet.png");

        WriteContactSheet(sheet, SheetDir + @"\sheet_native.png", 256, 6, true);
        WriteContactSheet(sheet, SheetDir + @"\sheet_small.png", 54, 11, false);
        AssetDatabase.SaveAssets();
        Debug.Log("[ArtGenerator] Sprites generated.");
    }

    // 描画内容(アルファ>0.5)の最大半径が target になるよう中心基準で拡大する(上限 maxScale、縮小はしない)
    static Img Fit(Img src, float target, float maxScale)
    {
        float ext = 1f;
        for (int py = 0; py < src.H; py++)
            for (int px = 0; px < src.W; px++)
                if (src.P[py * src.W + px].a > 0.5f)
                    ext = Mathf.Max(ext, Mathf.Max(Mathf.Abs(src.X(px)), Mathf.Abs(src.Y(py))));
        float k = Mathf.Clamp(target / ext, 1f, maxScale);
        if (k <= 1.001f) return src;
        Img o = new Img(src.W, src.H);
        for (int py = 0; py < o.H; py++)
            for (int px = 0; px < o.W; px++)
            {
                float u = o.X(px) / k + src.W * 0.5f - 0.5f, v = o.Y(py) / k + src.H * 0.5f - 0.5f;
                int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
                float fx = u - x0, fy = v - y0;
                Color r = default; float a = 0f;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int xx = x0 + i, yy = y0 + j;
                        if (xx < 0 || yy < 0 || xx >= src.W || yy >= src.H) continue;
                        float w = (i == 0 ? 1f - fx : fx) * (j == 0 ? 1f - fy : fy);
                        Color c = src.P[yy * src.W + xx];
                        r += c * (c.a * w); a += c.a * w;
                    }
                o.P[py * o.W + px] = a > 0f ? new Color(r.r / a, r.g / a, r.b / a, a) : new Color(0, 0, 0, 0);
            }
        return o;
    }

    // ---------- コンタクトシート ----------
    static Img BoxDownscale(Img src, int size)
    {
        Img o = new Img(size, size);
        float k = src.W / (float)size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, a = 0, n = 0;
                int x0 = Mathf.FloorToInt(x * k), x1 = Mathf.Max(x0 + 1, Mathf.FloorToInt((x + 1) * k));
                int y0 = Mathf.FloorToInt(y * k), y1 = Mathf.Max(y0 + 1, Mathf.FloorToInt((y + 1) * k));
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        Color c = src.P[yy * src.W + xx];
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a; n++;
                    }
                if (a > 0) o.P[y * size + x] = new Color(r / a, g / a, b / a, a / n);
            }
        return o;
    }

    public static void WriteContactSheet(List<KeyValuePair<string, Img>> items, string path, int cell, int cols, bool checker)
    {
        int pad = 8;
        int rows = (items.Count + cols - 1) / cols;
        int W = cols * (cell + pad) + pad, H = rows * (cell + pad) + pad;
        Img sheet = new Img(W, H);
        // 背景: 石畳に近い色
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float n = Painter.Fbm(x * 0.03f, y * 0.03f, 5);
                float v = 0.30f + n * 0.12f;
                sheet.P[y * W + x] = new Color(v * 1.05f, v, v * 0.92f, 1f);
            }
        for (int i = 0; i < items.Count; i++)
        {
            int col = i % cols, row = i / cols;
            int ox = pad + col * (cell + pad);
            int oy = H - pad - (row + 1) * (cell + pad) + pad;
            Img im = cell == items[i].Value.W ? items[i].Value : BoxDownscale(items[i].Value, cell);
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    int di = (oy + y) * W + ox + x;
                    sheet.P[di] = Img.Over(sheet.P[di], im.P[y * cell + x], 1f);
                }
        }
        WritePngRaw(sheet, path);
    }

    public static void WritePngRaw(Img im, string absPath)
    {
        Texture2D t = new Texture2D(im.W, im.H, TextureFormat.RGBA32, false);
        t.SetPixels(im.P);
        t.Apply();
        File.WriteAllBytes(absPath, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }
}
