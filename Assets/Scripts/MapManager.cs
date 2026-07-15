using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum CellType
{
    Walkable,
    Wall,
    Tower,
    Core,
    Spawner
}

public class MapManager : MonoBehaviour
{
    public static MapManager Instance { get; private set; }

    [Header("Tilemaps")]
    [SerializeField] private Tilemap groundTilemap;
    [SerializeField] private Tilemap wallTilemap;

    [Header("Key Positions")]
    [SerializeField, HideInInspector] private Vector3Int[] spawnerPositions = new Vector3Int[]
    {
        new Vector3Int(-5, 0, 0), // 左
        new Vector3Int(5, 0, 0),  // 右
        new Vector3Int(0, 5, 0),  // 上
        new Vector3Int(0, -5, 0)  // 下
    };
    [SerializeField, HideInInspector] private Vector3Int[] neighborWallPositions = new Vector3Int[]
    {
        new Vector3Int(-4, 0, 0), // 左隣接
        new Vector3Int(4, 0, 0),  // 右隣接
        new Vector3Int(0, 4, 0),  // 上隣接
        new Vector3Int(0, -4, 0)  // 下隣接
    };
    [SerializeField, HideInInspector] private Vector3Int coreGridPos = new Vector3Int(0, 0, 0);

    [Header("Visual Prefabs")]
    [SerializeField] private GameObject corePrefab;
    [SerializeField] private GameObject spawnerPrefab;

    private GameObject spawnedCoreInstance;
    private List<GameObject> spawnedSpawnerInstances = new List<GameObject>();

    // グリッド座標におけるオブジェクトや障害物の占有状態をキャッシュする辞書
    private Dictionary<Vector3Int, CellType> gridOccupancy = new Dictionary<Vector3Int, CellType>();

    public Tilemap GroundTilemap => groundTilemap;
    public Tilemap WallTilemap => wallTilemap;
    public Vector3Int[] SpawnerPositions => spawnerPositions;
    public Vector3Int SpawnerGridPos => spawnerPositions[0]; // 互換性維持のため
    public Vector3Int CoreGridPos => coreGridPos;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            // 実行時に動的に座標を割り当てることで、Unityエディタ上のセーブ設定をバイパスして確実に適用する
            spawnerPositions = new Vector3Int[]
            {
                new Vector3Int(-18, 7, 0),   // 上
                new Vector3Int(-18, 3, 0),   // 中上
                new Vector3Int(-18, -1, 0),  // 中央
                new Vector3Int(-18, -5, 0),  // 中下
                new Vector3Int(-18, -9, 0)   // 下
            };

            neighborWallPositions = new Vector3Int[]
            {
                new Vector3Int(-16, 8, 0),   // 上隣接
                new Vector3Int(-16, 4, 0),   // 中上隣接
                new Vector3Int(-16, 0, 0),   // 中央隣接
                new Vector3Int(-16, -5, 0),  // 中下隣接
                new Vector3Int(-16, -9, 0)   // 下隣接
            };

            coreGridPos = new Vector3Int(16, -1, 0); // 画面右端中央
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        InitializeGrid();
    }

    // 2x2セルの占有状況を登録するヘルパー
    private void Register2x2Occupancy(Vector3Int basePos, CellType cellType)
    {
        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                Vector3Int occupiedPos = new Vector3Int(basePos.x + x, basePos.y + y, basePos.z);
                gridOccupancy[occupiedPos] = cellType;
            }
        }
    }

    // 初期タイルの配置に基づき、セルの占有状況を初期化
    public void InitializeGrid()
    {
        gridOccupancy.Clear();

        if (groundTilemap == null || wallTilemap == null)
        {
            Debug.LogError("[MapManager] Tilemaps are not assigned!");
            return;
        }

        // 壁タイルマップから全ての壁を登録
        BoundsInt bounds = wallTilemap.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (wallTilemap.HasTile(pos))
            {
                gridOccupancy[pos] = CellType.Wall;
            }
        }

        // コアとスポナーの登録 (2x2マス)
        Register2x2Occupancy(coreGridPos, CellType.Core);
        foreach (var pos in spawnerPositions)
        {
            Register2x2Occupancy(pos, CellType.Spawner);
        }

        // 既存の生成済みインスタンスがあれば破棄する
        if (spawnedCoreInstance != null)
        {
            Destroy(spawnedCoreInstance);
        }
        foreach (var spawnerInstance in spawnedSpawnerInstances)
        {
            if (spawnerInstance != null)
            {
                Destroy(spawnerInstance);
            }
        }
        spawnedSpawnerInstances.Clear();

        // 2x2の中心に合わせるためのオフセット算出
        float cellSizeX = groundTilemap != null ? groundTilemap.cellSize.x : 1f;
        float cellSizeY = groundTilemap != null ? groundTilemap.cellSize.y : 1f;
        Vector3 offset = new Vector3(cellSizeX * 0.5f, cellSizeY * 0.5f, 0f);

        // プレハブから動的にインスタンスを生成して配置する
        if (corePrefab != null)
        {
            Vector3 coreWorldPos = GridToWorld(coreGridPos) + offset;
            spawnedCoreInstance = Instantiate(corePrefab, coreWorldPos, Quaternion.identity);
            spawnedCoreInstance.AddComponent<CoreHPDisplay>();
        }
        else
        {
            // コアが割り当てられていない場合の仮ビジュアルフォールバック（2x2緑キューブ）
            GameObject devCore = GameObject.CreatePrimitive(PrimitiveType.Cube);
            devCore.name = "Dev_Core_Fallback";
            devCore.transform.localScale = new Vector3(cellSizeX * 2f, cellSizeY * 2f, 1f);
            devCore.transform.position = GridToWorld(coreGridPos) + offset;
            Renderer rend = devCore.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material.color = Color.green;
            }
            spawnedCoreInstance = devCore;
            spawnedCoreInstance.AddComponent<CoreHPDisplay>();
        }

        if (spawnerPrefab != null)
        {
            foreach (var pos in spawnerPositions)
            {
                Vector3 spawnerWorldPos = GridToWorld(pos) + offset;
                GameObject spawnerInstance = Instantiate(spawnerPrefab, spawnerWorldPos, Quaternion.identity);
                spawnedSpawnerInstances.Add(spawnerInstance);
            }
        }
 
        UpdateSpawnerVisuals();
    }

    // 隣接する壁が解放されているスポナーを取得する
    public List<Vector3Int> GetActiveSpawners()
    {
        List<Vector3Int> activeSpawners = new List<Vector3Int>();
        
        // 左端の5つのスポナーについて隣接壁の解放状況を確認する
        for (int i = 0; i < spawnerPositions.Length; i++)
        {
            Vector3Int wallPos = neighborWallPositions[i];
            
            // 壁タイルマップに該当座標のタイルが存在しない（＝解放された）場合、有効とする
            if (wallTilemap == null || !wallTilemap.HasTile(wallPos))
            {
                activeSpawners.Add(spawnerPositions[i]);
            }
        }

        // 万が一すべて閉じている場合は、フォールバックとして最初のスポナー（中央）を有効にする
        if (activeSpawners.Count == 0)
        {
            activeSpawners.Add(spawnerPositions[2]); // 中央スポナーをフォールバックに
        }

        return activeSpawners;
    }
 
    private void UpdateSpawnerVisuals()
    {
        if (spawnedSpawnerInstances == null || spawnedSpawnerInstances.Count != spawnerPositions.Length) return;
 
        bool anyUnlocked = false;
        for (int i = 0; i < spawnerPositions.Length; i++)
        {
            Vector3Int wallPos = neighborWallPositions[i];
            if (wallTilemap == null || !wallTilemap.HasTile(wallPos))
            {
                anyUnlocked = true;
                break;
            }
        }
 
        for (int i = 0; i < spawnerPositions.Length; i++)
        {
            GameObject spawnerInstance = spawnedSpawnerInstances[i];
            if (spawnerInstance == null) continue;
 
            Vector3Int wallPos = neighborWallPositions[i];
            bool isUnlocked = (wallTilemap == null || !wallTilemap.HasTile(wallPos));
 
            if (!anyUnlocked && i == 2)
            {
                isUnlocked = true;
            }
 
            spawnerInstance.SetActive(isUnlocked);
        }
    }

    // 特定のセルがタワー配置可能か判定
    public bool CanPlaceTower(Vector3Int cellPos)
    {
        // GroundTilemapに床タイルがない箇所には配置できない
        if (!groundTilemap.HasTile(cellPos))
            return false;

        // 壁、既存タワー、コア、スポナーが存在する場合は配置不可
        if (gridOccupancy.TryGetValue(cellPos, out CellType type))
        {
            if (type == CellType.Wall || type == CellType.Tower || type == CellType.Core || type == CellType.Spawner)
            {
                return false;
            }
        }

        return true;
    }

    // タワーを配置して登録する
    public void SetTowerOccupant(Vector3Int cellPos, bool occupied)
    {
        if (occupied)
        {
            gridOccupancy[cellPos] = CellType.Tower;
        }
        else
        {
            if (gridOccupancy.TryGetValue(cellPos, out CellType type) && type == CellType.Tower)
            {
                gridOccupancy.Remove(cellPos);
            }
        }
    }

    // 指定セルが現在通行可能（A*経路などが通れる）か判定
    public bool IsCellWalkable(Vector3Int cellPos, bool ignoreTowers = false)
    {
        // GroundTilemapの床があること
        if (!groundTilemap.HasTile(cellPos))
            return false;

        // 壁やタワーがあれば通行不可
        if (gridOccupancy.TryGetValue(cellPos, out CellType type))
        {
            if (type == CellType.Wall)
            {
                return false;
            }
            if (type == CellType.Tower)
            {
                return ignoreTowers;
            }
        }

        return true;
    }

    public bool IsCellWalkable(Vector3Int cellPos)
    {
        return IsCellWalkable(cellPos, false);
    }

    // 壁タイルを動的に削除する（Step 3で使用）
    public void RemoveWall(Vector3Int cellPos)
    {
        if (wallTilemap != null && wallTilemap.HasTile(cellPos))
        {
            wallTilemap.SetTile(cellPos, null);
            if (gridOccupancy.TryGetValue(cellPos, out CellType type) && type == CellType.Wall)
            {
                gridOccupancy.Remove(cellPos);
            }
            Debug.Log($"[MapManager] Wall removed at {cellPos}");
        }
    }

    // グリッド座標からワールド座標へ変換
    public Vector3 GridToWorld(Vector3Int gridPos)
    {
        if (groundTilemap != null)
        {
            return groundTilemap.GetCellCenterWorld(gridPos);
        }
        return gridPos; // フォールバック
    }

    // ワールド座標からグリッド座標へ変換
    public Vector3Int WorldToGrid(Vector3 worldPos)
    {
        if (groundTilemap != null)
        {
            return groundTilemap.WorldToCell(worldPos);
        }
        return Vector3Int.FloorToInt(worldPos); // フォールバック
    }

    // 指定されたスタート位置からの固定直線ルート
    public List<Vector3> GetInitialPath(Vector3Int startPos)
    {
        List<Vector3> path = new List<Vector3>();
        Vector3Int current = startPos;
        path.Add(GridToWorld(current));

        // startPos から coreGridPos (0,0,-) への直線的なダミーパスを作成する
        while (current.x != coreGridPos.x)
        {
            current.x += (coreGridPos.x > current.x) ? 1 : -1;
            path.Add(GridToWorld(current));
        }
        while (current.y != coreGridPos.y)
        {
            current.y += (coreGridPos.y > current.y) ? 1 : -1;
            path.Add(GridToWorld(current));
        }

        return path;
    }

    // ウェーブ進行に基づいて壁を取り除き、マップを拡張する (Step 3)
    // 交互に上→下の順で1行ずつ拡張する
    public void ExpandMap(int waveNumber)
    {
        // Wave 17クリア以降（waveNumber >= 17）は壁の動的拡張を行わない
        if (waveNumber > 16) return;

        if (wallTilemap == null) return;

        int minUnlockedY = -(waveNumber / 2 + 1);
        int maxUnlockedY = (waveNumber + 1) / 2;

        BoundsInt bounds = wallTilemap.cellBounds;
        List<Vector3Int> wallsToRemove = new List<Vector3Int>();

        foreach (var pos in bounds.allPositionsWithin)
        {
            if (pos.y >= minUnlockedY && pos.y <= maxUnlockedY)
            {
                if (wallTilemap.HasTile(pos))
                {
                    wallsToRemove.Add(pos);
                }
            }
        }

        foreach (var pos in wallsToRemove)
        {
            RemoveWall(pos);
        }

        UpdateSpawnerVisuals();

        Debug.Log($"[MapManager] Map expanded for Wave {waveNumber}. Unlocked Y range: {minUnlockedY} to {maxUnlockedY}");
    }
}
