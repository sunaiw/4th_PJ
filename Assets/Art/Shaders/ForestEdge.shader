// 壁(森)を1枚のクアッドで描くための非ライトの半透明シェーダ(Built-in RP)。
// 低解像度マスク(_Mask: 森=1/通路=0、ブラー済み)とノイズで有機的な縁を作り、
// 縁の外側に「暗い土/苔の帯(verge)」と「林冠の接触影」を重ねて、石畳→土/苔→葉の順に見せる。
// 森テクスチャのUVはワールド座標/16(=RegionTileの周期と一致)。
Shader "Custom/ForestEdge"
{
    Properties
    {
        _ForestTex ("Forest (repeat)", 2D) = "white" {}
        _Noise ("Noise (repeat)", 2D) = "gray" {}
        _Mask ("Mask", 2D) = "white" {}
        // xy = マスク左下のワールド座標, zw = 1/マスクのワールドサイズ
        _MaskParams ("Mask Params", Vector) = (0, 0, 1, 1)
        _RegionCells ("Region Cells", Float) = 16
        _NoiseAmp ("Edge Noise Amount", Float) = 0.8
        _NoiseScale ("Edge Noise Scale", Float) = 0.35
        _VergeWidth ("Verge Width (mask units)", Float) = 0.4
        _VergeColorA ("Verge Earth", Color) = (0.13, 0.10, 0.07, 1)
        _VergeColorB ("Verge Moss", Color) = (0.12, 0.20, 0.08, 1)
        _ShadowStrength ("Contact Shadow", Range(0, 1)) = 0.3
        _RimDark ("Canopy Rim Darkening", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ForestTex;
            sampler2D _Noise;
            sampler2D _Mask;
            float4 _MaskParams;
            float _RegionCells;
            float _NoiseAmp;
            float _NoiseScale;
            float _VergeWidth;
            fixed4 _VergeColorA;
            fixed4 _VergeColorB;
            float _ShadowStrength;
            float _RimDark;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 world : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 w = i.world;

                // マスク(範囲外はクランプ=端のテクセル=森)
                float2 muv = (w - _MaskParams.xy) * _MaskParams.zw;
                float m = tex2D(_Mask, muv).r;

                // 2スケールのノイズで縁を揺らす(通路側/森側とも最大でおよそ0.2セル)
                float n1 = tex2D(_Noise, w * _NoiseScale).r;
                float n2 = tex2D(_Noise, w * (_NoiseScale * 3.1) + 0.37).r;
                float n3 = tex2D(_Noise, w * (_NoiseScale * 8.3) + 0.11).r;
                float n = n1 * 0.5 + n2 * 0.3 + n3 * 0.2;
                float d = m + (n - 0.5) * _NoiseAmp;

                // 葉の領域
                float foliageA = smoothstep(0.47, 0.53, d);

                // 森テクスチャ(16セル周期)
                fixed3 forest = tex2D(_ForestTex, w / _RegionCells).rgb;
                // 縁の内側を少し暗くして林冠の厚みを出す
                float rim = smoothstep(0.5, 0.5 + 0.28, d);
                forest *= lerp(1.0 - _RimDark, 1.0, rim);

                // 縁の外側: 土/苔の帯 + 林冠の接触影
                float vergeT = smoothstep(0.5 - _VergeWidth, 0.5, d);
                float mossN = tex2D(_Noise, w * 0.9 + 0.71).r;
                fixed3 vergeCol = lerp(_VergeColorA.rgb, _VergeColorB.rgb, smoothstep(0.35, 0.65, mossN));
                float vergeA = vergeT * (0.5 + 0.4 * n2);
                float shadowA = _ShadowStrength * smoothstep(0.5 - _VergeWidth * 1.5, 0.5, d);

                // 下地(影→土)を合成
                float baseA = shadowA + vergeA * (1.0 - shadowA);
                fixed3 baseCol = (vergeCol * vergeA + fixed3(0.02, 0.02, 0.03) * shadowA * (1.0 - vergeA)) / max(baseA, 1e-4);

                // 葉を最前面に合成
                float outA = foliageA + baseA * (1.0 - foliageA);
                fixed3 outRGB = (forest * foliageA + baseCol * baseA * (1.0 - foliageA)) / max(outA, 1e-4);
                return fixed4(outRGB, outA);
            }
            ENDCG
        }
    }
    Fallback Off
}
