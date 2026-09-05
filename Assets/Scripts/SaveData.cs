using System;
using System.Collections.Generic;

[Serializable]
public class TowerSaveData
{
    public int type; // (int)TowerType
    public int cellX;
    public int cellY;
    public int ownerId;
    public float currentHp;
    public int placedWave;
    public bool requiresSupply;
    public int outpostNumber;
}

[Serializable]
public class RewardCountEntry
{
    public int type; // (int)RewardType
    public int count;
}

[Serializable]
public class PlacedCountEntry
{
    public int type; // (int)TowerType
    public int count;
}

[Serializable]
public class GameSaveData
{
    public int saveVersion = SaveSystem.CurrentVersion;
    public string savedAtUtc;

    public int currentWave;
    public int life;
    public int initialLife;
    public int cost;
    public bool coreShieldActive;
    public int[] killCounts = new int[2];

    public List<RewardCountEntry> rewardCounts = new List<RewardCountEntry>();

    public List<TowerSaveData> towers = new List<TowerSaveData>();
    public int[] nextOutpostNumberByOwner = new int[2] { 1, 1 };
    public List<PlacedCountEntry> placedCountsInCurrentSetup = new List<PlacedCountEntry>();
}
