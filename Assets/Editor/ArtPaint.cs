using System;
using System.Collections.Generic;
using UnityEngine;

// ArtGenerator用の簡易ペイントツールキット。
// 符号付き距離関数(SDF)で図形を定義し、アンチエイリアス付きで合成する。
// 座標系は「キャンバス中心が原点、Y軸が上向き、単位はピクセル」。
namespace ArtGen
{
    public delegate float Sdf(float x, float y);
    public delegate Color Shader(float x, float y, float depth);

    // ---------- 画像バッファ ----------
    public class Img
    {
        public readonly int W, H;
        public readonly Color[] P; // ストレートアルファ

        public Img(int w, int h)
        {
            W = w; H = h;
            P = new Color[w * h];
        }

        public float CX => W * 0.5f;
        public float CY => H * 0.5f;

        // ピクセル(px,py:左下原点)の中心座標(中心原点・Y上向き)
        public float X(int px) { return px + 0.5f - W * 0.5f; }
        public float Y(int py) { return py + 0.5f - H * 0.5f; }

        public static Color Over(Color dst, Color src, float cov)
        {
            float sa = src.a * cov;
            if (sa <= 0f) return dst;
            float oa = sa + dst.a * (1f - sa);
            if (oa <= 0f) return new Color(0, 0, 0, 0);
            float k = dst.a * (1f - sa);
            return new Color(
                (src.r * sa + dst.r * k) / oa,
                (src.g * sa + dst.g * k) / oa,
                (src.b * sa + dst.b * k) / oa,
                oa);
        }

        // 図形をシェーダで塗る。bboxで最適化するためsdfが十分遠いピクセルは評価しない
        public void Paint(Sdf sdf, Shader shader)
        {
            for (int py = 0; py < H; py++)
            {
                float y = Y(py);
                for (int px = 0; px < W; px++)
                {
                    float x = X(px);
                    float d = sdf(x, y);
                    float cov = Mathf.Clamp01(0.5f - d);
                    if (cov <= 0f) continue;
                    Color c = shader(x, y, -d);
                    int i = py * W + px;
                    P[i] = Over(P[i], c, cov);
                }
            }
        }

        public void Paint(Sdf sdf, Mat m) { Paint(sdf, m.Shader(sdf)); }

        public void Fill(Sdf sdf, Color c) { Paint(sdf, (x, y, d) => c); }

        // 図形の影(ぼかしたシルエット)
        public void Shadow(Sdf sdf, float dx, float dy, float blur, float alpha)
        {
            for (int py = 0; py < H; py++)
            {
                float y = Y(py);
                for (int px = 0; px < W; px++)
                {
                    float x = X(px);
                    float d = sdf(x - dx, y - dy);
                    float a = Mathf.Clamp01(0.5f - d / (2f * blur)) * alpha;
                    if (a <= 0.003f) continue;
                    int i = py * W + px;
                    P[i] = Over(P[i], new Color(0.02f, 0.02f, 0.04f, 1f), a);
                }
            }
        }

        // 放射状グロー(通常合成)
        public void Glow(float cx, float cy, float r, Color c, float intensity, float power = 2f, float sx = 1f, float sy = 1f)
        {
            for (int py = 0; py < H; py++)
            {
                float y = Y(py);
                for (int px = 0; px < W; px++)
                {
                    float x = X(px);
                    float dx = (x - cx) / sx, dy = (y - cy) / sy;
                    float dd = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    if (dd >= 1f) continue;
                    float a = Mathf.Pow(1f - dd, power) * intensity;
                    int i = py * W + px;
                    P[i] = Over(P[i], new Color(c.r, c.g, c.b, 1f), Mathf.Clamp01(a));
                }
            }
        }

        // 既存の不透明部分にだけ色を加算する(結晶の光が石に映り込む表現など)
        public void AddLight(float cx, float cy, float r, Color c, float intensity, float power = 2f)
        {
            for (int py = 0; py < H; py++)
            {
                float y = Y(py);
                for (int px = 0; px < W; px++)
                {
                    int i = py * W + px;
                    if (P[i].a <= 0f) continue;
                    float x = X(px);
                    float dx = x - cx, dy = y - cy;
                    float dd = Mathf.Sqrt(dx * dx + dy * dy) / r;
                    if (dd >= 1f) continue;
                    float a = Mathf.Pow(1f - dd, power) * intensity * P[i].a;
                    Color p = P[i];
                    p.r = Mathf.Min(1f, p.r + c.r * a);
                    p.g = Mathf.Min(1f, p.g + c.g * a);
                    p.b = Mathf.Min(1f, p.b + c.b * a);
                    P[i] = p;
                }
            }
        }

        // 図形の内部だけに色を乗せる(縞模様や紋様用)
        public void Overlay(Sdf mask, Shader shader)
        {
            Paint(mask, shader);
        }

        // 画像全体のアルファに関数を掛ける
        public void MulAlpha(Func<float, float, float> f)
        {
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    int i = py * W + px;
                    Color c = P[i];
                    c.a *= Mathf.Clamp01(f(X(px), Y(py)));
                    P[i] = c;
                }
        }

        // 既存ピクセルに対して色を変換する(明るさ調整・グレースケールなど)
        public void Map(Func<Color, Color> f)
        {
            for (int i = 0; i < P.Length; i++)
                if (P[i].a > 0f) { float a = P[i].a; Color c = f(P[i]); c.a = a; P[i] = c; }
        }

        // すべての不透明部分を指定色で塗りつぶした白シルエットを作る
        public Img Silhouette(Color c)
        {
            Img o = new Img(W, H);
            for (int i = 0; i < P.Length; i++) o.P[i] = new Color(c.r, c.g, c.b, P[i].a);
            return o;
        }

        public Img Clone()
        {
            Img o = new Img(W, H);
            Array.Copy(P, o.P, P.Length);
            return o;
        }
    }

    // ---------- 材質 ----------
    public class Mat
    {
        public Color Top = Color.gray, Bot = Color.gray;
        public float Y0 = -100f, Y1 = 100f;     // 縦グラデーションの範囲
        public float Bevel = 8f;                 // 縁の光沢幅
        public float BevelStr = 0.28f;
        public float OutlineW = 2.5f;
        public Color Outline = new Color(0.04f, 0.04f, 0.06f, 1f);
        public float Noise = 0.10f;
        public float NoiseScale = 0.06f;
        public int Seed = 1;
        public float Inner = 0f;                 // 縁からの内側暗化(0..1)
        public static readonly Vector2 Light = new Vector2(-0.55f, 0.83f); // 左上から

        public Mat(Color top, Color bot) { Top = top; Bot = bot; }

        public Mat Range(float y0, float y1) { Y0 = y0; Y1 = y1; return this; }
        public Mat Bev(float w, float s) { Bevel = w; BevelStr = s; return this; }
        public Mat Out(float w, Color c) { OutlineW = w; Outline = c; return this; }
        public Mat Nz(float amt, float scale = 0.06f, int seed = 1) { Noise = amt; NoiseScale = scale; Seed = seed; return this; }

        public Shader Shader(Sdf sdf)
        {
            return (x, y, depth) =>
            {
                float t = Mathf.Clamp01((y - Y0) / (Y1 - Y0));
                Color c = Color.Lerp(Bot, Top, t);

                if (Bevel > 0f && depth < Bevel)
                {
                    const float h = 1.2f;
                    float nx = sdf(x + h, y) - sdf(x - h, y);
                    float ny = sdf(x, y + h) - sdf(x, y - h);
                    float l = Mathf.Sqrt(nx * nx + ny * ny);
                    if (l > 1e-4f)
                    {
                        nx /= l; ny /= l;
                        float dot = nx * Light.x + ny * Light.y;
                        float f = 1f - depth / Bevel;
                        f *= f;
                        float s = dot * BevelStr * f;
                        if (s > 0f) c = Color.Lerp(c, Color.white, s);
                        else c = Color.Lerp(c, Color.black, -s * 0.9f);
                    }
                }

                if (Inner > 0f)
                {
                    float f = Mathf.Clamp01(1f - depth / 28f);
                    c = Color.Lerp(c, Color.black, f * Inner);
                }

                if (Noise > 0f)
                {
                    float n = Painter.Fbm(x * NoiseScale, y * NoiseScale, Seed) - 0.5f;
                    float m = 1f + n * Noise * 2f;
                    c.r *= m; c.g *= m; c.b *= m;
                }

                if (OutlineW > 0f && depth < OutlineW + 1f)
                {
                    float k = Mathf.Clamp01((OutlineW + 1f - depth) / 1.5f);
                    c = Color.Lerp(c, Outline, k);
                }
                c.a = 1f;
                return c;
            };
        }
    }

    // ---------- 図形・ノイズ ----------
    public static class Painter
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144665;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        static float VNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        public static float Fbm(float x, float y, int seed)
        {
            float v = 0f, a = 0.5f, f = 1f;
            for (int i = 0; i < 4; i++)
            {
                v += a * VNoise(x * f, y * f, seed + i * 31);
                f *= 2.03f; a *= 0.5f;
            }
            return v / 0.9375f;
        }

        public static float Rand(int a, int b = 0, int seed = 0) { return Hash(a, b, seed); }

        // ----- 基本図形 -----
        public static Sdf Circle(float cx, float cy, float r)
        {
            return (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        }

        public static Sdf Ellipse(float cx, float cy, float rx, float ry)
        {
            float m = Mathf.Min(rx, ry);
            return (x, y) =>
            {
                float dx = (x - cx) / rx, dy = (y - cy) / ry;
                return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * m;
            };
        }

        public static Sdf Box(float cx, float cy, float hw, float hh, float round = 0f)
        {
            return (x, y) =>
            {
                float qx = Mathf.Abs(x - cx) - (hw - round);
                float qy = Mathf.Abs(y - cy) - (hh - round);
                float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - round;
            };
        }

        public static Sdf Capsule(float ax, float ay, float bx, float by, float r)
        {
            return (x, y) =>
            {
                float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
                float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
                float dx = pax - bax * h, dy = pay - bay * h;
                return Mathf.Sqrt(dx * dx + dy * dy) - r;
            };
        }

        // 多角形(頂点は時計回り/反時計回りどちらでも可)
        public static Sdf Poly(params float[] pts)
        {
            int n = pts.Length / 2;
            return (x, y) =>
            {
                float d = (x - pts[0]) * (x - pts[0]) + (y - pts[1]) * (y - pts[1]);
                float s = 1f;
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    float ex = pts[j * 2] - pts[i * 2], ey = pts[j * 2 + 1] - pts[i * 2 + 1];
                    float wx = x - pts[i * 2], wy = y - pts[i * 2 + 1];
                    float h = Mathf.Clamp01((wx * ex + wy * ey) / (ex * ex + ey * ey));
                    float bx = wx - ex * h, by = wy - ey * h;
                    d = Mathf.Min(d, bx * bx + by * by);
                    bool c1 = y >= pts[i * 2 + 1];
                    bool c2 = y < pts[j * 2 + 1];
                    bool c3 = ex * wy > ey * wx;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                }
                return s * Mathf.Sqrt(d);
            };
        }

        public static float[] Regular(int n, float r, float rotDeg = 0f, float cx = 0f, float cy = 0f)
        {
            float[] p = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                float a = (rotDeg + 360f * i / n) * Mathf.Deg2Rad;
                p[i * 2] = cx + Mathf.Cos(a) * r; p[i * 2 + 1] = cy + Mathf.Sin(a) * r;
            }
            return p;
        }

        public static float[] Star(int n, float rOut, float rIn, float rotDeg = 0f, float cx = 0f, float cy = 0f)
        {
            float[] p = new float[n * 4];
            for (int i = 0; i < n * 2; i++)
            {
                float a = (rotDeg + 180f * i / n) * Mathf.Deg2Rad;
                float r = (i % 2 == 0) ? rOut : rIn;
                p[i * 2] = cx + Mathf.Cos(a) * r; p[i * 2 + 1] = cy + Mathf.Sin(a) * r;
            }
            return p;
        }

        // ----- 演算 -----
        public static Sdf Union(params Sdf[] s)
        {
            return (x, y) => { float d = float.MaxValue; for (int i = 0; i < s.Length; i++) d = Mathf.Min(d, s[i](x, y)); return d; };
        }
        public static Sdf Sub(Sdf a, Sdf b) { return (x, y) => Mathf.Max(a(x, y), -b(x, y)); }
        public static Sdf Inter(Sdf a, Sdf b) { return (x, y) => Mathf.Max(a(x, y), b(x, y)); }
        public static Sdf Grow(Sdf a, float d) { return (x, y) => a(x, y) - d; }
        public static Sdf Ring(Sdf a, float thick) { return (x, y) => Mathf.Abs(a(x, y)) - thick * 0.5f; }

        // 点(cx,cy)まわりに回転した図形
        public static Sdf Rot(Sdf a, float deg, float cx = 0f, float cy = 0f)
        {
            float r = -deg * Mathf.Deg2Rad;
            float cs = Mathf.Cos(r), sn = Mathf.Sin(r);
            return (x, y) =>
            {
                float dx = x - cx, dy = y - cy;
                return a(cx + dx * cs - dy * sn, cy + dx * sn + dy * cs);
            };
        }

        public static Sdf Move(Sdf a, float dx, float dy) { return (x, y) => a(x - dx, y - dy); }

        // 中心まわりにn回複製(回転対称)
        public static Sdf Radial(Sdf a, int n, float startDeg = 0f)
        {
            Sdf[] arr = new Sdf[n];
            for (int i = 0; i < n; i++) arr[i] = Rot(a, startDeg + 360f * i / n);
            return Union(arr);
        }

        public static Color C(float r, float g, float b, float a = 1f) { return new Color(r, g, b, a); }
        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
