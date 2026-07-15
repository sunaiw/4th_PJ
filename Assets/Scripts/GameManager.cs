using System;
using System.Collections;
using UnityEngine;

public enum GamePhase
{
    Setup,
    Defense,
    Reward,
    GameOver
}

public class GameManager : SingletonBehaviour<GameManager>
{
    protected override bool PersistAcrossScenes => true;

    [Header("Game Settings")]
    [SerializeField] private int initialLife = 10;
    private const int BASE_COST = 6;
    private const int MAX_COST_CAP = 10;

    [Header("State Readonly")]
    [SerializeField] private GamePhase currentPhase = GamePhase.Setup;
    [SerializeField] private int currentWave = 1;
    [SerializeField] private int life;
    [SerializeField] private int cost;

    // Events for UI bindings
    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int> OnLifeChanged;
    public event Action<int> OnCostChanged;
    public event Action<int> OnWaveNumberChanged;

    public GamePhase CurrentPhase => currentPhase;
    public int CurrentWave => currentWave;
    public int Life => life;
    public int Cost => cost;
    public int InitialLife => initialLife;

    private bool setupPhaseFinished = false;
    private bool rewardPhaseFinished = false;

    // B-1: 敵撃破ボーナス
    private int killCount = 0;
    public int KillCount => killCount;

    // Core Shield: 次ウェーブ1回限りのダメージ無効化
    private bool coreShieldActive = false;
    public bool CoreShieldActive => coreShieldActive;

    private void Start()
    {
        InitializeGame();
        gameObject.AddComponent<HUDManager>();
        StartCoroutine(GameLoopCoroutine());
    }

    private int GetMaxCostForWave(int wave)
    {
        int bonusCost = Mathf.Min(wave / 3, 4); // Wave 3ごとに+1、最大+4
        return Mathf.Min(BASE_COST + bonusCost, MAX_COST_CAP);
    }

    private void InitializeGame()
    {
        life = initialLife;
        cost = GetMaxCostForWave(1);
        currentWave = 1;
        currentPhase = GamePhase.Setup;

        OnLifeChanged?.Invoke(life);
        OnCostChanged?.Invoke(cost);
        OnWaveNumberChanged?.Invoke(currentWave);
    }

    private IEnumerator GameLoopCoroutine()
    {
        while (currentPhase != GamePhase.GameOver)
        {
            // 1. Setup Phase (準備フェーズ)
            currentPhase = GamePhase.Setup;
            int maxCost = GetMaxCostForWave(currentWave);
            // B-1: 前ウェーブの敵撃破数に応じたボーナスコスト (10体ごとに+1、最大+3)。ボーナスは上限をさらに超過できる
            int killBonus = Mathf.Min(killCount / 10, 3);
            cost = maxCost + killBonus;
            killCount = 0; // 撃破カウントリセット
            setupPhaseFinished = false;
            OnPhaseChanged?.Invoke(currentPhase);
            OnCostChanged?.Invoke(cost);
            Debug.Log($"[GameManager] Setup Phase started for Wave {currentWave}. Cost: {cost} (Base: {maxCost}, Kill Bonus: {killBonus}). Place your towers!");
            
            while (!setupPhaseFinished)
            {
                yield return null;
            }

            // 2. Defense Phase (防衛フェーズ)
            currentPhase = GamePhase.Defense;
            OnPhaseChanged?.Invoke(currentPhase);
            Debug.Log($"[GameManager] Defense Phase started! Monsters are coming!" + (coreShieldActive ? " [Core Shield ACTIVE]" : ""));

            // Wave開始処理をSpawnerへ通知
            if (EnemySpawner.Instance != null)
            {
                EnemySpawner.Instance.StartWave(currentWave);
                // Spawnerがウェーブ終了(もしくは敵が全滅)するまで待機
                while (EnemySpawner.Instance.IsWaveRunning())
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning("[GameManager] EnemySpawner is missing. Skipping defense phase simulation. Waiting 3 seconds...");
                yield return new WaitForSeconds(3.0f);
            }

            if (life <= 0)
            {
                TriggerGameOver();
                yield break;
            }

            // ウェーブクリアに伴うマップ動的拡張 (Step 3)
            if (MapManager.Instance != null)
            {
                MapManager.Instance.ExpandMap(currentWave);
            }

            // 3. Reward Phase (ローグライク報酬フェーズ)
            currentPhase = GamePhase.Reward;
            rewardPhaseFinished = false;
            OnPhaseChanged?.Invoke(currentPhase);
            Debug.Log($"[GameManager] Wave {currentWave} Cleared! Reward Phase started.");

            if (RewardManager.Instance != null)
            {
                RewardManager.Instance.ShowRewardSelection();
                while (!rewardPhaseFinished)
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning("[GameManager] RewardManager is missing. Skipping reward phase. Waiting 1 second...");
                yield return new WaitForSeconds(1.0f);
                rewardPhaseFinished = true;
            }

            // Next Wave
            currentWave++;
            OnWaveNumberChanged?.Invoke(currentWave);
        }
    }

    // 準備フェーズを終了して防衛フェーズへ移行するためのトリガー（UIボタン等から呼び出し）
    public void StartDefensePhase()
    {
        if (currentPhase == GamePhase.Setup)
        {
            setupPhaseFinished = true;
        }
    }

    // 報酬フェーズを終了するためのトリガー
    public void CompleteRewardPhase()
    {
        if (currentPhase == GamePhase.Reward)
        {
            rewardPhaseFinished = true;
        }
    }

    // リソース変動処理
    public bool SpendCost(int amount)
    {
        if (cost >= amount)
        {
            cost -= amount;
            OnCostChanged?.Invoke(cost);
            return true;
        }
        return false;
    }

    public void AddCost(int amount)
    {
        int maxCost = GetMaxCostForWave(currentWave);
        cost = Mathf.Clamp(cost + amount, 0, maxCost);
        OnCostChanged?.Invoke(cost);
    }

    public void TakeDamage(int damage)
    {
        // Core Shield: 1回限りのダメージ無効化
        if (coreShieldActive)
        {
            coreShieldActive = false;
            Debug.Log($"[GameManager] Core Shield absorbed {damage} damage! Shield deactivated.");
            return;
        }

        life = Mathf.Max(0, life - damage);
        OnLifeChanged?.Invoke(life);
        
        Debug.Log($"[GameManager] Core took damage! Current Life: {life}");

        if (life <= 0 && currentPhase != GamePhase.GameOver)
        {
            TriggerGameOver();
        }
    }

    public void HealLife(int amount)
    {
        life = Mathf.Min(initialLife, life + amount);
        OnLifeChanged?.Invoke(life);
    }

    public void IncreaseMaxLife(int amount)
    {
        initialLife += amount;
        life = Mathf.Min(initialLife, life + amount);
        OnLifeChanged?.Invoke(life);
        Debug.Log($"[GameManager] Core Max HP increased to {initialLife}. Current HP: {life}");
    }

    public void AddKill()
    {
        killCount++;
    }

    private void TriggerGameOver()
    {
        currentPhase = GamePhase.GameOver;
        OnPhaseChanged?.Invoke(currentPhase);
        Debug.Log("[GameManager] Game Over! The Core was destroyed.");
    }

    /// <summary>
    /// Core Shield リワード: 次ウェーブで1回だけコアへのダメージを完全無効化する。
    /// </summary>
    public void ActivateCoreShield()
    {
        coreShieldActive = true;
        Debug.Log("[GameManager] Core Shield has been activated! Next hit will be absorbed.");
    }
}
