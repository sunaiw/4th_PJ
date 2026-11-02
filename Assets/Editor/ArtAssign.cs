using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// 生成したスプライトをプレハブ/シーンへ割り当てる。
public static class ArtAssign
{
    static Sprite Load(string path)
    {
        Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (s == null) Debug.LogError("[ArtAssign] sprite not found: " + path);
        return s;
    }

    [MenuItem("Tools/Art/Assign Prefabs")]
    public static void AssignPrefabs()
    {
        string[] towers = { "Tower", "TankTower", "Healer", "SplashTower", "FrostTower", "Barricade" };
        Sprite silhouette = Load("Assets/Art/Towers/TowerOwnerSilhouette.png");
        foreach (string n in towers)
        {
            Edit("Assets/" + n + ".prefab", root =>
            {
                SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
                sr.sprite = Load("Assets/Art/Towers/" + n + ".png");
                sr.color = Color.white;
                Tower tower = root.GetComponent<Tower>();
                SerializedObject so = new SerializedObject(tower);
                // BarricadeはOffline対象外なのでオフライン用スプライトは持たない
                so.FindProperty("offlineSprite").objectReferenceValue = n == "Barricade" ? null : Load("Assets/Art/Towers/" + n + "_Offline.png");
                so.FindProperty("ownerOutlineSprite").objectReferenceValue = silhouette;
                so.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        foreach (string n in new[] { "Enemy", "Enemy2", "Enemy3", "Enemy4", "Enemy5" })
        {
            Edit("Assets/" + n + ".prefab", root =>
            {
                SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
                sr.sprite = Load("Assets/Art/Enemies/" + n + ".png");
                sr.color = Color.white;
            });
        }

        foreach (string n in new[] { "Bullet", "EnemyBullet" })
        {
            Edit("Assets/" + n + ".prefab", root =>
            {
                SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
                sr.sprite = Load("Assets/Art/Bullets/" + n + ".png");
                sr.color = Color.white;
            });
        }

        Edit("Assets/Core.prefab", root => root.GetComponent<SpriteRenderer>().sprite = Load("Assets/Art/Structures/Core_Round.png"));
        Edit("Assets/EnemySpawner.prefab", root =>
        {
            SpriteRenderer sr = root.GetComponent<SpriteRenderer>();
            sr.sprite = Load("Assets/Art/Structures/EnemySpawner_Round.png");
            sr.sortingOrder = 3; // 森クアッド(2)より手前。コア(3)とは重ならない
        });
        AssetDatabase.SaveAssets();
        Debug.Log("[ArtAssign] Prefabs updated.");
    }

    static void Edit(string path, System.Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/Art/Swap Scene Tiles")]
    public static string SwapSceneTiles()
    {
        Tilemap ground = GameObject.Find("GroundTilemap").GetComponent<Tilemap>();
        Tilemap wall = GameObject.Find("WallTilemap").GetComponent<Tilemap>();
        RegionTile gt = AssetDatabase.LoadAssetAtPath<RegionTile>("Assets/Art/Terrain/GroundRegionTile.asset");
        RegionTile ft = AssetDatabase.LoadAssetAtPath<RegionTile>("Assets/Art/Terrain/ForestRegionTile.asset");
        string a = ArtTerrain.SwapTilemapTiles(ground, gt);
        string b = ArtTerrain.SwapTilemapTiles(wall, ft);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        return a + " | " + b;
    }
}
