using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 1枚の大きなテクスチャを divisions x divisions のセルに分割し、セル座標(mod divisions)に応じた
/// 部分画像を返すタイル。1種類のTileアセットを全セルに敷いても、同じ絵が毎マス繰り返されず、
/// divisionsマスごとの周期でシームレスにつながる地形になる。
/// スプライトは初回利用時にテクスチャから生成してキャッシュする（テクスチャ側でスライス設定は不要）。
/// </summary>
[CreateAssetMenu(menuName = "Tiles/Region Tile")]
public class RegionTile : TileBase
{
    [SerializeField] private Texture2D texture;
    [SerializeField] private int divisions = 8;
    [SerializeField] private Tile.ColliderType colliderType = Tile.ColliderType.Sprite;
    [SerializeField] private TileFlags flags = TileFlags.LockColor;

    [System.NonSerialized] private Sprite[] sprites;

    public Texture2D Texture => texture;

    public void Configure(Texture2D tex, int div, Tile.ColliderType collider, TileFlags tileFlags)
    {
        texture = tex;
        divisions = Mathf.Max(1, div);
        colliderType = collider;
        flags = tileFlags;
        sprites = null;
    }

    private Sprite GetSprite(int cx, int cy)
    {
        if (texture == null) return null;

        if (sprites == null || sprites.Length != divisions * divisions)
        {
            sprites = new Sprite[divisions * divisions];
        }

        int index = cy * divisions + cx;
        if (sprites[index] == null)
        {
            // 1セル=1ワールド単位になるよう、PPUはセルのピクセル幅に合わせる
            float cell = texture.width / (float)divisions;
            Rect rect = new Rect(cx * cell, cy * cell, cell, cell);
            Sprite s = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), cell, 0, SpriteMeshType.FullRect);
            s.name = $"{name}_{cx}_{cy}";
            s.hideFlags = HideFlags.HideAndDontSave;
            sprites[index] = s;
        }
        return sprites[index];
    }

    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
    {
        int cx = Mod(position.x, divisions);
        int cy = Mod(position.y, divisions);
        tileData.sprite = GetSprite(cx, cy);
        tileData.color = Color.white;
        tileData.transform = Matrix4x4.identity;
        tileData.flags = flags;
        tileData.colliderType = colliderType;
    }

    private static int Mod(int v, int m)
    {
        int r = v % m;
        return r < 0 ? r + m : r;
    }
}
