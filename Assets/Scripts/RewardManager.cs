using System;
using System.Collections.Generic;
using UnityEngine;

public enum RewardType
{
    HealCore,
    IncreaseTowerDamage,
    IncreaseTowerFireRate,
    IncreaseTowerRange,
    IncreaseTowerMaxHP,
    IncreaseTowerArmor,
    FrostAction,
    PiercingShot,
    CoreShield
}

[System.Serializable]
public class RewardOffer
{
    public RewardType Type;
    public string Title;
    public string Description;
    public int Value; // 強化量などの数値
}

public static class RewardTypeNames
{
    // Reward選択画面のカードとHUD右下インジケータで共通利用する短い名前
    public static readonly Dictionary<RewardType, string> ShortNames = new Dictionary<RewardType, string>
    {
        { RewardType.HealCore, "Repair Core" },
        { RewardType.IncreaseTowerDamage, "Damage UP" },
        { RewardType.IncreaseTowerFireRate, "Speed UP" },
        { RewardType.IncreaseTowerRange, "Range UP" },
        { RewardType.IncreaseTowerMaxHP, "HP UP" },
        { RewardType.IncreaseTowerArmor, "Armor UP" },
        { RewardType.FrostAction, "Frost Action" },
        { RewardType.PiercingShot, "Piercing Shot" },
        { RewardType.CoreShield, "Core Shield" },
    };

    public static string Get(RewardType type) =>
        ShortNames.TryGetValue(type, out string name) ? name : type.ToString();
}

public class RewardManager : SingletonBehaviour<RewardManager>
{
    [Header("UI Reference")]
    [SerializeField] private RewardUI rewardUI;

    private List<RewardOffer> currentOffers = new List<RewardOffer>();

    public event Action OnRewardsUpdated;

    private Dictionary<RewardType, int> acquiredRewardCounts = CreateEmptyRewardCounts();

    public Dictionary<RewardType, int> GetAcquiredRewardCounts() => acquiredRewardCounts;

    // 全RewardTypeを0で初期化した獲得数辞書を作成する
    private static Dictionary<RewardType, int> CreateEmptyRewardCounts()
    {
        var counts = new Dictionary<RewardType, int>();
        foreach (RewardType type in Enum.GetValues(typeof(RewardType)))
        {
            counts[type] = 0;
        }
        return counts;
    }

    // 報酬一覧の抽選とUI表示
    public void ShowRewardSelection()
    {
        currentOffers.Clear();
        
        // 1. Core回復 (必ず出現)
        currentOffers.Add(CreateOffer(RewardType.HealCore));

        // 2. 残り8種のバフから2つを確率に基づいてユニークに抽選
        List<RewardType> targetTypes = new List<RewardType>
        {
            RewardType.IncreaseTowerMaxHP,
            RewardType.IncreaseTowerRange,
            RewardType.IncreaseTowerFireRate,
            RewardType.IncreaseTowerArmor,
            RewardType.IncreaseTowerDamage,
            RewardType.FrostAction,
            RewardType.PiercingShot,
            RewardType.CoreShield
        };
        List<float> weights = new List<float> { 0.15f, 0.15f, 0.15f, 0.10f, 0.15f, 0.10f, 0.10f, 0.10f };

        for (int step = 0; step < 2; step++)
        {
            if (targetTypes.Count == 0) break;

            float totalWeight = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                totalWeight += weights[i];
            }

            float randValue = UnityEngine.Random.Range(0f, totalWeight);
            float currentSum = 0f;
            int selectedIdx = 0;

            for (int i = 0; i < weights.Count; i++)
            {
                currentSum += weights[i];
                if (randValue <= currentSum)
                {
                    selectedIdx = i;
                    break;
                }
            }

            RewardType selected = targetTypes[selectedIdx];
            currentOffers.Add(CreateOffer(selected));

            // プールから削除
            targetTypes.RemoveAt(selectedIdx);
            weights.RemoveAt(selectedIdx);
        }

        // 表示される3つのカードの並び順をランダム化 (Core回復が常に左端になるのを防ぐ)
        ShuffleOffers(currentOffers);

        if (rewardUI != null)
        {
            rewardUI.DisplayRewards(currentOffers);
        }
        else
        {
            Debug.LogError("[RewardManager] RewardUI reference is missing!");
            // UIがない場合のフォールバック（最初のものを自動選択）
            SelectReward(0);
        }
    }

    private void ShuffleOffers(List<RewardOffer> list)
    {
        for (int n = list.Count - 1; n > 0; n--)
        {
            int k = UnityEngine.Random.Range(0, n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    private RewardOffer CreateOffer(RewardType type)
    {
        RewardOffer offer = new RewardOffer { Type = type };
        switch (type)
        {
            case RewardType.HealCore:
                // B-2: コアHP≤5で回復量増加、コアHPが最大なら最大HP+1
                if (GameManager.Instance != null)
                {
                    int currentLife = GameManager.Instance.Life;
                    int maxLife = GameManager.Instance.InitialLife;
                    if (currentLife >= maxLife)
                    {
                        offer.Title = "Fortify Core";
                        offer.Description = "Core Max HP +1";
                        offer.Value = -1; // 特殊値: 最大HP上昇を示す
                    }
                    else if (currentLife <= 5)
                    {
                        offer.Title = "Emergency Repair";
                        offer.Description = "Repair +3 Core HP";
                        offer.Value = 3;
                    }
                    else
                    {
                        offer.Title = "Repair Core";
                        offer.Description = "Repair +2 Core HP";
                        offer.Value = 2;
                    }
                }
                else
                {
                    offer.Title = "Repair Core";
                    offer.Description = "Repair +2 Core HP";
                    offer.Value = 2;
                }
                break;
            case RewardType.IncreaseTowerDamage:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "All Tower ATK +10% per stack";
                offer.Value = 0;
                break;
            case RewardType.IncreaseTowerFireRate:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "All Tower ATK Speed +10% per stack";
                offer.Value = 0;
                break;
            case RewardType.IncreaseTowerRange:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "All Tower Range +10% per stack";
                offer.Value = 0;
                break;
            case RewardType.IncreaseTowerMaxHP:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "All Tower Max HP +15% per stack (Compound)";
                offer.Value = 0;
                break;
            case RewardType.IncreaseTowerArmor:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "All Tower Armor +5% per stack";
                offer.Value = 0;
                break;
            case RewardType.FrostAction:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "Attacks slow enemies by 15% for 1s (stacks)";
                offer.Value = 0;
                break;
            case RewardType.PiercingShot:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "Attacks pierce to hit 1 nearby enemy (50% DMG, +10%/stack)";
                offer.Value = 0;
                break;
            case RewardType.CoreShield:
                offer.Title = RewardTypeNames.Get(type);
                offer.Description = "Next wave: block 1 hit to the Core completely";
                offer.Value = 0;
                break;
        }
        return offer;
    }

    // UIボタンから選択された際に実行される
    public void SelectReward(int index)
    {
        if (index < 0 || index >= currentOffers.Count) return;
        
        RewardOffer offer = currentOffers[index];
        ApplyRewardEffect(offer);

        if (acquiredRewardCounts.ContainsKey(offer.Type))
        {
            acquiredRewardCounts[offer.Type]++;
        }
        OnRewardsUpdated?.Invoke();

        Debug.Log($"[RewardManager] Selected Reward: {offer.Title}");

        // GameManagerに完了を通知して次のフェーズへ移行
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CompleteRewardPhase();
        }
    }

    private void ApplyRewardEffect(RewardOffer offer)
    {
        switch (offer.Type)
        {
            case RewardType.HealCore:
                if (GameManager.Instance != null)
                {
                    if (offer.Value == -1)
                    {
                        // コア最大HP上昇
                        GameManager.Instance.IncreaseMaxLife(1);
                    }
                    else
                    {
                        GameManager.Instance.HealLife(offer.Value);
                    }
                }
                break;
            case RewardType.CoreShield:
                // Core Shield: 次ウェーブのみ有効な1回限りのバリア
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.ActivateCoreShield();
                    Debug.Log("[RewardManager] Core Shield activated for next wave!");
                }
                break;
            default:
                // 他のタワー用バフ（攻撃力、射程、速度、HP、Frost、Piercing）は
                // 累積獲得カウントが増加した後に、各タワーの Start() や 
                // RewardManager.Instance.OnRewardsUpdated を介して安全に自動反映されるため、
                // ここでの個別処理は不要です。
                break;
        }
    }
}
