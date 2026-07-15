using System;
using System.Collections.Generic;
using UnityEngine;

public class AStarPathfinding : SingletonBehaviour<AStarPathfinding>
{
    private class Node : IComparable<Node>
    {
        public Vector3Int Position;
        public int GCost; // スタートからのコスト
        public int HCost; // ゴールまでの推定コスト
        public Node Parent;

        public int FCost => GCost + HCost;

        public Node(Vector3Int pos, int g, int h, Node parent = null)
        {
            Position = pos;
            GCost = g;
            HCost = h;
            Parent = parent;
        }

        public int CompareTo(Node other)
        {
            int compare = FCost.CompareTo(other.FCost);
            if (compare == 0) compare = HCost.CompareTo(other.HCost);
            return compare;
        }
    }

    // C-1: 優先度付きキュー（Min-Heap）の実装
    private class MinHeap
    {
        private List<Node> heap = new List<Node>();
        public int Count => heap.Count;

        public void Add(Node node)
        {
            heap.Add(node);
            HeapifyUp(heap.Count - 1);
        }

        public Node ExtractMin()
        {
            if (heap.Count == 0) return null;
            Node min = heap[0];
            int lastIdx = heap.Count - 1;
            heap[0] = heap[lastIdx];
            heap.RemoveAt(lastIdx);
            if (heap.Count > 0) HeapifyDown(0);
            return min;
        }

        public void UpdateNode(Node node)
        {
            for (int i = 0; i < heap.Count; i++)
            {
                if (heap[i] == node)
                {
                    HeapifyUp(i);
                    HeapifyDown(i);
                    break;
                }
            }
        }

        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[index].CompareTo(heap[parent]) < 0)
                {
                    (heap[index], heap[parent]) = (heap[parent], heap[index]);
                    index = parent;
                }
                else break;
            }
        }

        private void HeapifyDown(int index)
        {
            int count = heap.Count;
            while (true)
            {
                int smallest = index;
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                if (left < count && heap[left].CompareTo(heap[smallest]) < 0) smallest = left;
                if (right < count && heap[right].CompareTo(heap[smallest]) < 0) smallest = right;
                if (smallest == index) break;
                (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
                index = smallest;
            }
        }
    }

    // A*アルゴリズムによる経路計算
    // 通過セルの中心座標のリストを返す
    public List<Vector3> FindPath(Vector3Int start, Vector3Int end, bool ignoreTowers = false, bool avoidThreats = true)
    {
        if (MapManager.Instance == null) return new List<Vector3>();

        Node endNode = FindPathNode(start, end, ignoreTowers, avoidThreats);
        if (endNode == null)
        {
            return new List<Vector3>(); // 経路が見つからなかった
        }

        // 経路の復元
        List<Vector3> path = new List<Vector3>();
        Node current = endNode;
        while (current != null)
        {
            path.Add(MapManager.Instance.GridToWorld(current.Position));
            current = current.Parent;
        }

        path.Reverse(); // スタートからになるように反転
        return path;
    }

    // パスが存在するかどうか（バリデーション用）
    public bool HasValidPath(Vector3Int start, Vector3Int end)
    {
        Node endNode = FindPathNode(start, end, false);
        return endNode != null;
    }

    private Node FindPathNode(Vector3Int start, Vector3Int end, bool ignoreTowers = false, bool avoidThreats = true)
    {
        // C-1: List<Node>からMinHeapに置き換え
        MinHeap openHeap = new MinHeap();
        HashSet<Vector3Int> closedSet = new HashSet<Vector3Int>();
        Dictionary<Vector3Int, Node> openDict = new Dictionary<Vector3Int, Node>();

        Node startNode = new Node(start, 0, GetManhattanDistance(start, end));
        openHeap.Add(startNode);
        openDict[start] = startNode;

        // 探索限界数（安全対策、マップサイズが巨大になった時のパフォーマンス調整）
        int maxIterations = 2000;
        int iterations = 0;

        while (openHeap.Count > 0 && iterations++ < maxIterations)
        {
            // C-1: O(1)で最小FCostのノードを取得
            Node currentNode = openHeap.ExtractMin();
            openDict.Remove(currentNode.Position);
            closedSet.Add(currentNode.Position);

            // ゴールに到達したか判定
            if (currentNode.Position == end)
            {
                return currentNode;
            }

            // 隣接ノード（上下左右）の探索
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3Int neighborPos = currentNode.Position + directions[i];
                // すでに探索済みか、障害物（壁やタワー）で歩けないゾーンの場合はスキップ
                if (closedSet.Contains(neighborPos)) continue;

                bool isWalkable = MapManager.Instance.IsCellWalkable(neighborPos, ignoreTowers);
                
                // 例外処理：ゴールやスタートが一時的にブロックされる判定になっていたとしても、そこまでは繋ぐ必要がある
                if (neighborPos == end || neighborPos == start)
                {
                    isWalkable = true;
                }

                if (!isWalkable) continue;

                int threatCost = avoidThreats ? GetCellDamageCost(neighborPos) : 0;
                int newMovementCostToNeighbor = currentNode.GCost + 10 + threatCost;

                if (openDict.TryGetValue(neighborPos, out Node existingNode))
                {
                    if (newMovementCostToNeighbor < existingNode.GCost)
                    {
                        existingNode.GCost = newMovementCostToNeighbor;
                        existingNode.Parent = currentNode;
                        openHeap.UpdateNode(existingNode);
                    }
                }
                else
                {
                    Node neighborNode = new Node(neighborPos, newMovementCostToNeighbor, GetManhattanDistance(neighborPos, end), currentNode);
                    openHeap.Add(neighborNode);
                    openDict[neighborPos] = neighborNode;
                }
            }
        }

        return null; // パスが見つからない
    }

    // 再利用可能なリストで隣接ノード取得（GCアロケーション軽減）
    private static readonly Vector3Int[] directions = {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    private int GetManhattanDistance(Vector3Int a, Vector3Int b)
    {
        return Mathf.Abs(a.x - b.x) * 10 + Mathf.Abs(a.y - b.y) * 10;
    }

    private int GetCellDamageCost(Vector3Int cellPos)
    {
        if (TowerManager.Instance == null || MapManager.Instance == null) return 0;

        float totalDps = 0f;
        List<Tower> towers = TowerManager.Instance.GetActiveTowers();
        Vector3 cellWorldPos = MapManager.Instance.GridToWorld(cellPos);

        foreach (Tower tower in towers)
        {
            if (tower == null || tower.IsBarricade || tower.IsHealer) continue;
            float dist = Vector3.Distance(cellWorldPos, tower.transform.position);
            if (dist <= tower.Range)
            {
                totalDps += tower.Damage * tower.FireRate;
            }
        }

        // 1 DPS = 100,000 コスト。
        return Mathf.RoundToInt(totalDps * 100000f);
    }
}
