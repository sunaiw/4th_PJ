using UnityEngine;
using static ArtGen.Painter;

// ArtGeneratorが使う各スプライトの描画定義。すべて決定的(固定シード)。
// 256x256キャンバス、原点は中心、Y上向き。敵は+X方向(右向き)を正面とする。
namespace ArtGen
{
    public static class Sprites
    {
        public const int S = 256;
        static readonly Color Ink = new Color(0.05f, 0.04f, 0.07f, 1f);

        static Img New() { return new Img(S, S); }
        static Mat M(int top, int bot, float y0 = -100f, float y1 = 100f)
        {
            return new Mat(Hex(top), Hex(bot)).Range(y0, y1);
        }

        // ============ タワー共通の石の台座 ============
        public static Sdf BaseDisc { get { return Circle(0, 0, 112); } }

        static void StoneBase(Img im, Color accent)
        {
            Sdf disc = BaseDisc;
            im.Shadow(disc, 7, -9, 9, 0.6f);
            im.Paint(disc, M(0x8a8c98, 0x42444f, -112, 112).Bev(13, 0.32f).Out(3.2f, Ink).Nz(0.16f, 0.05f, 3));
            // 外周の石目(放射状の割れ目)
            Sdf cracks = Radial(Box(0, 105, 1.2f, 7f), 12, 7f);
            im.Paint(Inter(cracks, disc), (x, y, d) => new Color(0.1f, 0.1f, 0.14f, 0.7f));
            // 金の輪
            im.Paint(Ring(Circle(0, 0, 97), 8), M(0xffe27a, 0x9a6416, -100, 100).Bev(4, 0.4f).Out(1.6f, new Color(0.25f, 0.13f, 0.02f)).Nz(0.05f, 0.1f, 5));
            // 金の鋲
            im.Paint(Radial(Circle(0, 105, 3.2f), 8, 22.5f), M(0xfff0b0, 0xb8801a).Bev(2, 0.4f).Out(1f, new Color(0.25f, 0.13f, 0.02f)));
            // 内側の窪んだ床
            im.Paint(Circle(0, 0, 88), M(0x30323c, 0x16171e, -88, 88).Bev(12, -0.30f).Out(2, Ink).Nz(0.18f, 0.07f, 9));
            im.Glow(0, 0, 90, accent, 0.28f, 1.6f);
            im.Paint(Ring(Circle(0, 0, 84), 2.5f), (x, y, d) => new Color(accent.r, accent.g, accent.b, 0.8f));
        }

        // 面ごとに色を塗り分ける宝石
        static void Facet(Img im, float[] tri, Color c)
        {
            Color top = Color.Lerp(c, Color.white, 0.12f), bot = Color.Lerp(c, Color.black, 0.12f);
            im.Paint(Poly(tri), new Mat(top, bot).Range(-70, 70).Bev(0, 0).Out(0, Ink).Nz(0.06f, 0.1f, 11));
        }

        // ============ タワー ============
        public static Img Tower()
        {
            Img im = New();
            Color accent = Hex(0x4aa8ff);
            StoneBase(im, accent);

            // 4つの銃眼(外側に突き出る)
            Sdf merlons = Union(Box(0, 80, 15, 12, 3), Box(0, -80, 15, 12, 3), Box(80, 0, 12, 15, 3), Box(-80, 0, 12, 15, 3));
            im.Paint(merlons, M(0x6f86b0, 0x2a3556).Bev(5, 0.35f).Out(2.4f, Ink).Nz(0.1f, 0.1f, 4));
            Sdf oct = Poly(Regular(8, 76, 22.5f));
            im.Paint(oct, M(0x7a98cc, 0x2c3f78, -76, 76).Bev(9, 0.4f).Out(3, Ink).Nz(0.12f, 0.08f, 6));
            im.Paint(Ring(Poly(Regular(8, 60, 22.5f)), 3.2f), (x, y, d) => new Color(0.55f, 0.85f, 1f, 0.75f));
            // 隅の鋲
            im.Paint(Radial(Circle(0, 69, 4f), 8, 22.5f), M(0xd8e8ff, 0x6a7ca8).Bev(2, 0.4f).Out(1, Ink));

            // 結晶
            im.Glow(0, 4, 74, accent, 0.65f, 1.6f);
            float[] top1 = { 0, 66, -30, 6, 0, 6 };
            float[] top2 = { 0, 66, 30, 6, 0, 6 };
            float[] bot1 = { -30, 6, 0, -54, 0, 6 };
            float[] bot2 = { 30, 6, 0, -54, 0, 6 };
            Facet(im, top1, C(0.68f, 0.94f, 1f));
            Facet(im, top2, C(0.28f, 0.66f, 1f));
            Facet(im, bot1, C(0.16f, 0.46f, 0.95f));
            Facet(im, bot2, C(0.07f, 0.22f, 0.68f));
            im.Paint(Ring(Poly(0, 66, 30, 6, 0, -54, -30, 6), 3.4f), (x, y, d) => new Color(0.03f, 0.08f, 0.32f, 1f));
            im.Paint(Capsule(-18, 26, -7, 48, 2.2f), (x, y, d) => new Color(1, 1, 1, 0.85f));
            im.Glow(0, 8, 26, Color.white, 0.8f, 2f);
            return im;
        }

        public static Img TankTower()
        {
            Img im = New();
            Color accent = Hex(0xb8c4d8);
            StoneBase(im, accent);

            // 角の稜堡(ベースの内側に収める)
            Sdf keep = Box(0, 0, 80, 80, 14);
            im.Paint(keep, M(0xa8b0bc, 0x40464f, -80, 80).Bev(11, 0.4f).Out(3.2f, Ink).Nz(0.14f, 0.09f, 7));
            // 鉄板の継ぎ目
            im.Paint(Inter(Union(Box(0, 40, 80, 1.3f), Box(0, -40, 80, 1.3f), Box(40, 0, 1.3f, 80), Box(-40, 0, 1.3f, 80)), keep), (x, y, d) => new Color(0.08f, 0.09f, 0.12f, 0.6f));
            // 四隅の円塔
            Sdf towers = Union(Circle(66, 66, 27), Circle(-66, 66, 27), Circle(66, -66, 27), Circle(-66, -66, 27));
            im.Paint(towers, M(0xc4ccd8, 0x4c535e, -92, 92).Bev(9, 0.45f).Out(3f, Ink).Nz(0.1f, 0.12f, 8));
            im.Paint(Union(Circle(66, 66, 12), Circle(-66, 66, 12), Circle(66, -66, 12), Circle(-66, -66, 12)), M(0x7a8290, 0x262a32).Bev(5, -0.3f).Out(1.6f, Ink));
            im.Paint(Union(Circle(66, 66, 4), Circle(-66, 66, 4), Circle(66, -66, 4), Circle(-66, -66, 4)), (x, y, d) => new Color(1f, 0.85f, 0.4f, 1f));
            // 中央の紋章盾
            float[] shield = { -38, 42, 38, 42, 38, 2, 0, -50, -38, 2 };
            im.Shadow(Poly(shield), 3, -4, 4, 0.5f);
            im.Paint(Poly(shield), M(0xffe27a, 0x9a6416, -50, 42).Bev(4, 0.4f).Out(2.6f, new Color(0.2f, 0.1f, 0.02f)));
            float[] shieldIn = { -30, 35, 30, 35, 30, 5, 0, -40, -30, 5 };
            im.Paint(Poly(shieldIn), M(0x6a7a96, 0x26304a, -40, 35).Bev(5, 0.3f).Out(1.5f, Ink));
            im.Paint(Union(Box(0, 4, 5, 26), Box(0, 14, 17, 5)), M(0xfff0b0, 0xc89a30).Bev(2, 0.4f).Out(1.2f, new Color(0.2f, 0.1f, 0.02f)));
            return im;
        }

        public static Img Healer()
        {
            Img im = New();
            Color accent = Hex(0xff6ec8);
            StoneBase(im, accent);

            im.Glow(0, 0, 112, accent, 0.5f, 1.4f);
            // 四つ葉(クアトレフォイル)
            Sdf petals = Union(Circle(40, 0, 38), Circle(-40, 0, 38), Circle(0, 40, 38), Circle(0, -40, 38), Circle(0, 0, 46));
            im.Paint(petals, M(0xffb0dc, 0xa8206e, -78, 78).Bev(11, 0.45f).Out(3.2f, new Color(0.2f, 0.02f, 0.14f)).Nz(0.08f, 0.1f, 12));
            // 花弁の中心にある金の宝珠
            im.Paint(Union(Circle(52, 0, 9), Circle(-52, 0, 9), Circle(0, 52, 9), Circle(0, -52, 9)), M(0xfff0b0, 0xb8801a).Bev(3, 0.45f).Out(1.4f, new Color(0.25f, 0.1f, 0.02f)));
            // 中央の白い光の円盤
            im.Paint(Circle(0, 0, 33), M(0xfff4fb, 0xf08cc8, -33, 33).Bev(6, 0.3f).Out(2.4f, new Color(0.55f, 0.08f, 0.38f)));
            im.Glow(0, 0, 40, Color.white, 0.8f, 1.5f);
            // 十字
            Sdf cross = Union(Box(0, 0, 8, 25, 3), Box(0, 0, 25, 8, 3));
            im.Paint(cross, M(0xff4fb0, 0xc01878, -25, 25).Bev(3, 0.4f).Out(1.6f, new Color(0.4f, 0.03f, 0.25f)));
            return im;
        }

        public static Img SplashTower()
        {
            Img im = New();
            Color accent = Hex(0xff8a28);
            StoneBase(im, accent);

            // 歯車状の砲座
            Sdf cog = Union(Circle(0, 0, 70), Radial(Box(0, 78, 16, 14, 2), 8, 0f));
            im.Paint(cog, M(0xe8a060, 0x5a2a10, -90, 90).Bev(10, 0.45f).Out(3.2f, Ink).Nz(0.14f, 0.09f, 13));
            im.Paint(Radial(Circle(0, 58, 5), 8, 22.5f), M(0xffe0a0, 0x805020).Bev(2, 0.4f).Out(1.2f, Ink));
            // 砲口(鉄のリング+闇)
            im.Paint(Circle(0, 0, 50), M(0x8a8a96, 0x30303a, -50, 50).Bev(8, 0.5f).Out(2.6f, Ink));
            im.Paint(Circle(0, 0, 40), M(0x1a1010, 0x050303, -40, 40).Bev(10, -0.35f).Out(1.5f, Ink));
            im.Glow(0, -2, 38, accent, 0.9f, 1.3f);
            // 砲弾
            im.Paint(Circle(0, -2, 25), M(0x6a6a78, 0x0a0a0e, -25, 25).Bev(8, 0.5f).Out(2.2f, Ink));
            im.Paint(Circle(-8, 8, 6), (x, y, d) => new Color(1f, 1f, 1f, 0.35f));
            // 導火線の火花
            im.Paint(Capsule(10, 20, 20, 32, 2.4f), M(0xc0a070, 0x604020));
            im.Glow(22, 34, 20, C(1f, 0.8f, 0.2f), 1f, 1.6f);
            im.Paint(Circle(22, 34, 4f), (x, y, d) => new Color(1f, 0.98f, 0.8f, 1f));
            return im;
        }

        public static Img FrostTower()
        {
            Img im = New();
            Color accent = Hex(0x68e0ff);
            StoneBase(im, accent);
            im.Glow(0, 0, 110, accent, 0.55f, 1.5f);

            // 小さい副晶(30度ずれ)と大きい主晶(6本)
            DrawShards(im, 6, 30f, 64f, 11f, C(0.55f, 0.85f, 1f), C(0.18f, 0.45f, 0.78f));
            DrawShards(im, 6, 0f, 96f, 15f, C(0.9f, 0.99f, 1f), C(0.22f, 0.62f, 0.9f));

            // 六角形のハブ
            Sdf hub = Poly(Regular(6, 36, 0));
            im.Paint(hub, M(0xf0ffff, 0x58b4e8, -36, 36).Bev(8, 0.45f).Out(3f, new Color(0.04f, 0.2f, 0.4f)));
            im.Paint(Poly(Regular(6, 20, 0)), M(0xc0f4ff, 0x38a0e0, -20, 20).Bev(5, -0.3f).Out(1.8f, new Color(0.04f, 0.2f, 0.4f)));
            im.Glow(0, 0, 30, Color.white, 0.95f, 1.5f);
            return im;
        }

        static void DrawShards(Img im, int n, float rot, float len, float halfW, Color light, Color dark)
        {
            for (int i = 0; i < n; i++)
            {
                float a = rot + 90f + 360f * i / n;
                // 局所座標(+Yが先端)のkite形状を回転して描く
                float[] left = { 0, len, -halfW, len * 0.42f, 0, 10 };
                float[] right = { 0, len, halfW, len * 0.42f, 0, 10 };
                Sdf l = Rot(Poly(left), a - 90f);
                Sdf r = Rot(Poly(right), a - 90f);
                im.Paint(Grow(Union(l, r), 0), new Mat(Color.Lerp(dark, light, 0.5f), Color.Lerp(dark, light, 0.5f)).Bev(0, 0).Out(0, Ink));
                im.Paint(l, new Mat(light, Color.Lerp(light, dark, 0.35f)).Range(-len, len).Bev(0, 0).Out(0, Ink).Nz(0.05f, 0.1f, 14 + i));
                im.Paint(r, new Mat(dark, Color.Lerp(dark, Color.black, 0.3f)).Range(-len, len).Bev(0, 0).Out(0, Ink).Nz(0.05f, 0.1f, 20 + i));
                im.Paint(Ring(Rot(Poly(0, len, halfW, len * 0.42f, 0, 10, -halfW, len * 0.42f), a - 90f), 3.2f), (x, y, d) => new Color(0.03f, 0.18f, 0.38f, 1f));
            }
        }

        public static Img Barricade()
        {
            Img im = New();
            Color woodDark = C(0.22f, 0.12f, 0.05f);
            // 外周の杭(先端が外を向く)
            Sdf stake = Poly(-11, 56, 11, 56, 6, 106, 0, 118, -6, 106);
            Sdf stakes = Radial(stake, 12, 15f);
            im.Shadow(Union(stakes, Circle(0, 0, 80)), 7, -9, 9, 0.6f);
            im.Paint(stakes, M(0xc89458, 0x4a2a12, -110, 110).Bev(6, 0.4f).Out(2.8f, Ink).Nz(0.14f, 0.15f, 15));
            im.Paint(Radial(Poly(-3.5f, 104, 3.5f, 104, 0, 117), 12, 15f), (x, y, d) => new Color(0.95f, 0.85f, 0.65f, 1f));
            // 木の甲板
            Sdf deck = Circle(0, 0, 84);
            im.Paint(deck, M(0xb88048, 0x4e2c14, -84, 84).Bev(10, 0.4f).Out(3.4f, Ink).Nz(0.16f, 0.12f, 16));
            // 板目
            Sdf planks = Union(Box(0, 42, 90, 1.5f), Box(0, 14, 90, 1.5f), Box(0, -14, 90, 1.5f), Box(0, -42, 90, 1.5f));
            im.Paint(Inter(planks, deck), (x, y, d) => new Color(woodDark.r, woodDark.g, woodDark.b, 0.8f));
            // 鉄の帯
            Sdf bands = Union(Box(-44, 0, 11, 90), Box(44, 0, 11, 90));
            im.Paint(Inter(bands, deck), M(0x9aa0ac, 0x3a3e48, -90, 90).Bev(4, 0.5f).Out(1.8f, Ink));
            im.Paint(Union(Circle(-44, 52, 3.6f), Circle(-44, -52, 3.6f), Circle(44, 52, 3.6f), Circle(44, -52, 3.6f), Circle(-44, 0, 3.6f), Circle(44, 0, 3.6f)), M(0xe0e4ec, 0x6a707c).Bev(2, 0.4f).Out(1, Ink));
            // 中央の鉄の鋲
            im.Paint(Circle(0, 0, 22), M(0xb8bec8, 0x3a3e48, -22, 22).Bev(8, 0.5f).Out(2.6f, Ink));
            im.Paint(Circle(0, 0, 9), M(0x5a606c, 0x1a1c22).Bev(4, -0.4f).Out(1.4f, Ink));
            return im;
        }

        // タワー用の白シルエット(オーナーアウトライン用)。台座と同じ円形で統一する
        public static Img OwnerSilhouette()
        {
            Img im = New();
            im.Fill(Circle(0, 0, 106), Color.white);
            return im;
        }

        // Offline状態: 彩度を落として暗くする
        public static Img Offline(Img src)
        {
            Img o = src.Clone();
            o.Map(c =>
            {
                float g = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                g = g * 0.62f;
                return new Color(g * 0.98f, g, g * 1.04f, c.a);
            });
            return o;
        }

        // ============ 敵(右向き) ============
        static void FootShadow(Img im, float cx, float cy, float rx, float ry, float a = 0.5f)
        {
            im.Shadow(Ellipse(cx, cy, rx, ry), 5, -9, 9, a);
        }

        public static Img Enemy() // 通常: 赤い小鬼の槍兵
        {
            Img im = New();
            FootShadow(im, -4, 0, 72, 86);
            // 槍
            im.Paint(Capsule(-20, -62, 100, -62, 4.5f), M(0xb88050, 0x4e2c14).Bev(3, 0.4f).Out(2, Ink));
            im.Paint(Poly(98, -72, 98, -52, 128, -62), M(0xe8eef6, 0x7a8294, -72, -52).Bev(0, 0).Out(2f, Ink));
            // 胴(肩幅)
            im.Paint(Ellipse(-6, 0, 66, 80), M(0xd2362a, 0x5a0c10, -80, 80).Bev(11, 0.4f).Out(3.2f, Ink).Nz(0.12f, 0.09f, 21));
            im.Paint(Ellipse(-6, 0, 44, 58), M(0x8e1e1e, 0x3a0608, -58, 58).Bev(8, -0.3f).Out(0, Ink).Nz(0.1f, 0.1f, 22));
            // 肩当て
            im.Paint(Union(Circle(-8, 68, 20), Circle(-8, -68, 20)), M(0xb0b6c4, 0x3c4250, -90, 90).Bev(7, 0.45f).Out(2.6f, Ink));
            // 角
            Sdf hornA = Poly(26, 26, 54, 40, 96, 72, 62, 22);
            Sdf hornB = Poly(26, -26, 54, -40, 96, -72, 62, -22);
            im.Paint(Union(hornA, hornB), M(0xfff0c8, 0x8a7a50, -72, 72).Bev(4, 0.4f).Out(2.4f, Ink));
            // 頭
            im.Paint(Circle(28, 0, 40), M(0xff6a50, 0x8a1a18, -40, 40).Bev(9, 0.45f).Out(3f, Ink).Nz(0.1f, 0.12f, 23));
            // 目
            im.Glow(52, 15, 16, C(1f, 0.85f, 0.2f), 0.8f, 1.5f);
            im.Glow(52, -15, 16, C(1f, 0.85f, 0.2f), 0.8f, 1.5f);
            im.Paint(Union(Ellipse(52, 15, 7, 4.5f), Ellipse(52, -15, 7, 4.5f)), (x, y, d) => new Color(1f, 0.96f, 0.55f, 1f));
            return im;
        }

        public static Img Enemy2() // 高速: すり抜ける亡霊(紫がかったマゼンタ)
        {
            Img im = New();
            // 尾(左へたなびく)
            int n = 28;
            float[] pts = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                float x = Mathf.Lerp(-120f, 12f, t);
                float w = 50f * Mathf.Pow(t, 0.7f) * (1f + 0.12f * Mathf.Sin(t * 22f));
                float yo = 16f * Mathf.Sin(t * 8.5f) * (1f - t);
                pts[i * 2] = x; pts[i * 2 + 1] = yo + w;
                pts[(2 * n - 1 - i) * 2] = x; pts[(2 * n - 1 - i) * 2 + 1] = yo - w;
            }
            Sdf tail = Poly(pts);
            Sdf body = Union(tail, Circle(26, 0, 54));
            im.Glow(10, 0, 118, C(1f, 0.3f, 0.9f), 0.35f, 2f);
            Img g = New();
            g.Paint(body, M(0xff7ae8, 0x5a0a66, -60, 60).Bev(12, 0.45f).Out(3.2f, new Color(0.18f, 0.0f, 0.2f)).Nz(0.16f, 0.08f, 24));
            // 尾に向けて透明にする
            g.MulAlpha((x, y) => Mathf.Clamp01((x + 118f) / 70f));
            for (int i = 0; i < g.P.Length; i++) im.P[i] = Img.Over(im.P[i], g.P[i], 1f);
            // 目と口
            im.Paint(Union(Poly(40, 30, 74, 14, 44, 12), Poly(40, -30, 74, -14, 44, -12)), (x, y, d) => new Color(1f, 1f, 1f, 1f));
            im.Glow(54, 20, 18, C(1f, 0.6f, 1f), 0.9f, 1.5f);
            im.Glow(54, -20, 18, C(1f, 0.6f, 1f), 0.9f, 1.5f);
            im.Paint(Poly(64, 6, 78, 0, 64, -6, 70, 0), (x, y, d) => new Color(0.2f, 0.0f, 0.25f, 1f));
            return im;
        }

        public static Img Enemy3() // 重装: 青鋼の騎士+大盾
        {
            Img im = New();
            FootShadow(im, 0, 0, 80, 100);
            // 肩のスパイク
            im.Paint(Union(Poly(-30, 82, 14, 82, -8, 118), Poly(-30, -82, 14, -82, -8, -118)), M(0xc8d4ee, 0x4a5a80, -118, 118).Bev(4, 0.4f).Out(2.6f, Ink));
            // 胴
            im.Paint(Box(-8, 0, 60, 90, 26), M(0x7a98d8, 0x1c2c60, -90, 90).Bev(13, 0.45f).Out(3.4f, Ink).Nz(0.12f, 0.08f, 25));
            im.Paint(Ring(Box(-8, 0, 40, 68, 16), 3f), (x, y, d) => new Color(0.05f, 0.1f, 0.3f, 0.85f));
            // 肩当て
            im.Paint(Union(Ellipse(-6, 74, 36, 24), Ellipse(-6, -74, 36, 24)), M(0xb4c8f0, 0x2c3c78, -100, 100).Bev(8, 0.5f).Out(3f, Ink));
            // 兜
            im.Paint(Circle(6, 0, 42), M(0xc8d8f8, 0x3c508c, -42, 42).Bev(11, 0.5f).Out(3.2f, Ink).Nz(0.08f, 0.1f, 26));
            im.Paint(Capsule(-30, 0, 34, 0, 8), M(0x8aa4e0, 0x1c2c60).Bev(4, 0.4f).Out(2, Ink));
            im.Paint(Box(38, 0, 6, 24, 3), (x, y, d) => new Color(0.02f, 0.02f, 0.06f, 1f));
            im.Glow(38, 0, 18, C(1f, 0.6f, 0.2f), 0.5f, 1.8f);
            // 大盾(正面)
            Sdf shield = Box(84, 0, 24, 66, 16);
            im.Shadow(shield, 3, -4, 4, 0.5f);
            im.Paint(shield, M(0xe0b848, 0x7a5410, -66, 66).Bev(8, 0.45f).Out(3.2f, Ink));
            im.Paint(Box(84, 0, 15, 56, 10), M(0x6a88c8, 0x1c2c60, -56, 56).Bev(7, 0.3f).Out(1.8f, Ink));
            im.Paint(Circle(84, 0, 12), M(0xfff0b0, 0xb8801a).Bev(4, 0.5f).Out(2, Ink));
            return im;
        }

        public static Img Enemy4() // 範囲攻撃: 爆弾砲兵(オレンジ)
        {
            Img im = New();
            FootShadow(im, -10, 0, 74, 74);
            // 背中の爆弾
            im.Paint(Circle(-62, 0, 30), M(0x585868, 0x0a0a10, -30, 30).Bev(9, 0.5f).Out(2.6f, Ink));
            im.Paint(Capsule(-80, 18, -92, 32, 2.6f), M(0xc0a070, 0x604020));
            im.Glow(-94, 34, 22, C(1f, 0.8f, 0.2f), 1f, 1.6f);
            im.Paint(Circle(-94, 34, 4.2f), (x, y, d) => new Color(1f, 0.98f, 0.8f, 1f));
            // 胴
            im.Paint(Circle(-14, 0, 66), M(0xffa84a, 0x6a2a08, -66, 66).Bev(12, 0.45f).Out(3.4f, Ink).Nz(0.12f, 0.09f, 27));
            im.Paint(Ring(Circle(-14, 0, 46), 3), (x, y, d) => new Color(0.35f, 0.1f, 0.02f, 0.8f));
            // 大砲(右へ)
            Sdf tube = Box(70, 0, 52, 21, 5);
            im.Shadow(tube, 3, -5, 5, 0.5f);
            im.Paint(tube, M(0x909098, 0x2a2a32, -21, 21).Bev(7, 0.5f).Out(3f, Ink).Nz(0.08f, 0.1f, 28));
            im.Paint(Union(Box(40, 0, 5, 22, 2), Box(78, 0, 5, 22, 2)), M(0xffe27a, 0x9a6416).Bev(3, 0.4f).Out(1.8f, Ink));
            im.Paint(Box(120, 0, 8, 29, 5), M(0x6a6a76, 0x1c1c24, -29, 29).Bev(5, 0.5f).Out(2.6f, Ink));
            im.Glow(124, 0, 24, C(1f, 0.55f, 0.1f), 0.7f, 1.6f);
            // ゴーグル
            im.Paint(Union(Circle(-22, 26, 17), Circle(-22, -26, 17)), M(0x4a3a30, 0x1a1210).Bev(4, 0.4f).Out(2.4f, Ink));
            im.Paint(Union(Circle(-22, 26, 10), Circle(-22, -26, 10)), M(0xfff0a0, 0xffb020).Bev(4, 0.3f).Out(1.2f, Ink));
            return im;
        }

        public static Img Enemy5() // 妨害: 紫の棘球と一つ目
        {
            Img im = New();
            FootShadow(im, 0, 0, 90, 90, 0.4f);
            im.Glow(0, 0, 124, C(0.7f, 0.3f, 1f), 0.45f, 1.8f);
            Sdf spikes = Poly(Star(10, 108, 66, 0));
            im.Paint(spikes, M(0xc890ff, 0x38106a, -108, 108).Bev(10, 0.45f).Out(3.4f, new Color(0.08f, 0.02f, 0.16f)).Nz(0.12f, 0.08f, 29));
            im.Paint(Circle(0, 0, 58), M(0x7a3aca, 0x1c0640, -58, 58).Bev(12, -0.3f).Out(2.6f, new Color(0.08f, 0.02f, 0.16f)).Nz(0.1f, 0.1f, 30));
            // 眼
            Sdf eye = Ellipse(4, 0, 42, 32);
            im.Paint(eye, M(0xfffff0, 0xd8d0e8, -32, 32).Bev(8, 0.3f).Out(3f, new Color(0.08f, 0.02f, 0.16f)));
            im.Paint(Circle(14, 0, 22), M(0x70ffe8, 0x1090c0, -22, 22).Bev(6, 0.4f).Out(2f, new Color(0.02f, 0.1f, 0.2f)));
            im.Paint(Box(15, 0, 5, 17, 2), (x, y, d) => new Color(0.02f, 0.02f, 0.08f, 1f));
            im.Paint(Circle(8, 9, 4.2f), (x, y, d) => new Color(1, 1, 1, 0.95f));
            return im;
        }

        public static Img Boss() // ボス: 深紅の魔王
        {
            Img im = New();
            FootShadow(im, -2, 0, 82, 104);
            im.Glow(0, 0, 124, C(1f, 0.15f, 0.05f), 0.35f, 2f);
            // 肩のスパイク(3本ずつ)
            Sdf sp = Union(
                Poly(-40, 80, -10, 80, -30, 120), Poly(-8, 84, 22, 84, 6, 124), Poly(20, 76, 46, 76, 40, 112),
                Poly(-40, -80, -10, -80, -30, -120), Poly(-8, -84, 22, -84, 6, -124), Poly(20, -76, 46, -76, 40, -112));
            im.Paint(sp, M(0xe8d8c0, 0x4a3a30, -124, 124).Bev(4, 0.4f).Out(2.6f, Ink));
            // 胴
            im.Paint(Box(-10, 0, 62, 92, 28), M(0xc42a2a, 0x3a0408, -92, 92).Bev(13, 0.45f).Out(3.6f, Ink).Nz(0.14f, 0.08f, 31));
            // 金の縁取り
            im.Paint(Ring(Box(-10, 0, 46, 76, 20), 4.5f), M(0xffe27a, 0x9a6416, -76, 76).Bev(2, 0.4f).Out(1.2f, new Color(0.25f, 0.1f, 0.02f)));
            // 背中のルーン
            im.Glow(-34, 0, 40, C(1f, 0.55f, 0.1f), 0.8f, 1.5f);
            im.Paint(Ring(Circle(-34, 0, 18), 5), (x, y, d) => new Color(1f, 0.85f, 0.4f, 1f));
            im.Paint(Poly(Regular(3, 11, 0, -34, 0)), (x, y, d) => new Color(1f, 0.85f, 0.4f, 1f));
            // 肩当て(金縁)
            im.Paint(Union(Ellipse(-6, 74, 40, 26), Ellipse(-6, -74, 40, 26)), M(0xe04040, 0x500a0a, -100, 100).Bev(9, 0.45f).Out(3.2f, Ink));
            // 頭
            im.Paint(Circle(20, 0, 46), M(0xe84a3a, 0x500808, -46, 46).Bev(11, 0.5f).Out(3.4f, Ink).Nz(0.1f, 0.12f, 32));
            // 大きな角
            Sdf horns = Union(
                Poly(14, 36, 40, 52, 82, 78, 112, 100, 90, 60, 52, 34),
                Poly(14, -36, 40, -52, 82, -78, 112, -100, 90, -60, 52, -34));
            im.Paint(horns, M(0xfff0d0, 0x6a5a40, -100, 100).Bev(5, 0.45f).Out(3f, Ink));
            im.Paint(Union(Poly(100, 88, 112, 100, 94, 70), Poly(100, -88, 112, -100, 94, -70)), M(0xffe27a, 0xb8801a).Bev(2, 0.4f).Out(1.6f, Ink));
            // 目
            im.Glow(50, 17, 22, C(1f, 0.75f, 0.1f), 1f, 1.5f);
            im.Glow(50, -17, 22, C(1f, 0.75f, 0.1f), 1f, 1.5f);
            im.Paint(Union(Poly(38, 24, 66, 10, 40, 12), Poly(38, -24, 66, -10, 40, -12)), (x, y, d) => new Color(1f, 0.95f, 0.5f, 1f));
            return im;
        }

        public static Img BarricadeBuster() // 破城槌を押す黄色いゴブリン
        {
            Img im = New();
            FootShadow(im, 10, 0, 100, 60);
            // 丸太
            Sdf log = Capsule(-10, 0, 90, 0, 32);
            im.Paint(log, M(0xd09a58, 0x4a2a12, -32, 32).Bev(9, 0.4f).Out(3.2f, Ink).Nz(0.14f, 0.15f, 33));
            // 鉄の帯
            im.Paint(Inter(Union(Box(14, 0, 6, 36), Box(46, 0, 6, 36), Box(78, 0, 6, 36)), Grow(log, 0)), M(0x9aa0ac, 0x30343e, -34, 34).Bev(3, 0.5f).Out(1.8f, Ink));
            // 鉄の杭付き先端
            Sdf head = Union(Poly(88, 38, 110, 30, 126, 0, 110, -30, 88, -38));
            im.Paint(head, M(0xe8ecf4, 0x4a505c, -38, 38).Bev(8, 0.5f).Out(3.2f, Ink));
            im.Paint(Poly(112, 8, 128, 0, 112, -8), (x, y, d) => new Color(0.15f, 0.16f, 0.2f, 1f));
            // ゴブリン(押し手)
            im.Paint(Circle(-58, 0, 52), M(0xffe84a, 0xa06a08, -52, 52).Bev(11, 0.45f).Out(3.4f, Ink).Nz(0.1f, 0.1f, 34));
            // 警戒縞
            Sdf bodyMask = Circle(-58, 0, 49);
            im.Overlay(bodyMask, (x, y, d) =>
            {
                float s = Mathf.Repeat((x + y) * 0.07f, 1f);
                return s < 0.3f ? new Color(0.08f, 0.06f, 0.04f, 0.55f) : new Color(0, 0, 0, 0);
            });
            // 腕(丸太を握る)
            im.Paint(Union(Capsule(-40, 36, 0, 24, 8), Capsule(-40, -36, 0, -24, 8)), M(0xc09030, 0x604010).Bev(3, 0.4f).Out(2, Ink));
            // 頭と目
            im.Paint(Circle(-44, 0, 26), M(0xffd84a, 0xa06a08, -26, 26).Bev(7, 0.4f).Out(2.8f, Ink));
            im.Glow(-30, 9, 10, C(1f, 0.2f, 0.1f), 0.9f, 1.5f);
            im.Glow(-30, -9, 10, C(1f, 0.2f, 0.1f), 0.9f, 1.5f);
            im.Paint(Union(Circle(-30, 9, 4.4f), Circle(-30, -9, 4.4f)), (x, y, d) => new Color(1f, 0.3f, 0.2f, 1f));
            return im;
        }

        public static Img SiegeMarker() // 標的を刻む黒紫の槍頭
        {
            Img im = New();
            FootShadow(im, 0, 0, 100, 76);
            im.Glow(0, 0, 124, C(0.9f, 0.1f, 0.8f), 0.4f, 1.8f);
            Sdf kite = Poly(124, 0, 40, 74, -84, 58, -62, 0, -84, -58, 40, -74);
            im.Paint(kite, M(0xc02ab8, 0x2a0230, -74, 74).Bev(12, 0.45f).Out(3.4f, new Color(0.08f, 0.0f, 0.1f)).Nz(0.12f, 0.08f, 35));
            // 金の縁
            im.Paint(Ring(Poly(106, 0, 36, 56, -66, 44, -48, 0, -66, -44, 36, -56), 4f), M(0xffe27a, 0x9a6416, -60, 60).Bev(2, 0.4f).Out(1.2f, new Color(0.25f, 0.1f, 0.02f)));
            // 照準
            im.Paint(Circle(18, 0, 30), M(0x4a0a4a, 0x14001a, -30, 30).Bev(8, -0.3f).Out(2.2f, Ink));
            im.Glow(18, 0, 34, C(1f, 0.3f, 0.95f), 0.9f, 1.4f);
            im.Paint(Ring(Circle(18, 0, 24), 4.5f), (x, y, d) => new Color(1f, 0.82f, 0.95f, 1f));
            im.Paint(Union(Box(18, 0, 3, 36), Box(18, 0, 36, 3)), (x, y, d) => new Color(1f, 0.82f, 0.95f, 1f));
            im.Paint(Circle(18, 0, 8), (x, y, d) => new Color(1f, 1f, 1f, 1f));
            // 後ろの棘
            im.Paint(Union(Poly(-70, 30, -112, 44, -86, 14), Poly(-70, -30, -112, -44, -86, -14)), M(0xe8d8c0, 0x4a3a30).Bev(3, 0.4f).Out(2.4f, Ink));
            return im;
        }

        // ============ 弾 ============
        public static Img Bullet()
        {
            Img im = New();
            im.Glow(0, 0, 124, C(0.3f, 0.8f, 1f), 0.6f, 1.6f);
            im.Paint(Circle(0, 0, 92), new Mat(C(0.75f, 0.97f, 1f), C(0.1f, 0.4f, 0.9f)).Range(-92, 92).Bev(0, 0).Out(7, C(0.02f, 0.1f, 0.4f)).Nz(0, 0.1f, 1));
            im.Glow(-8, 10, 52, Color.white, 1f, 1.3f);
            return im;
        }

        public static Img EnemyBullet()
        {
            Img im = New();
            im.Glow(0, 0, 124, C(1f, 0.2f, 0.1f), 0.6f, 1.6f);
            im.Paint(Circle(0, 0, 92), new Mat(C(1f, 0.6f, 0.2f), C(0.55f, 0.04f, 0.08f)).Range(-92, 92).Bev(0, 0).Out(9, C(0.1f, 0.0f, 0.03f)).Nz(0, 0.1f, 1));
            im.Glow(-8, 10, 46, C(1f, 0.95f, 0.5f), 0.95f, 1.3f);
            return im;
        }
    }
}
