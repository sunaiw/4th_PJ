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

    // セーブ: 獲得済み報酬数をDictionaryからList<RewardCountEntry>へ平坦化する（JsonUtility対応）
    public void CaptureInto(GameSaveData data)
    {
        data.rewardCounts.Clear();
        foreach (var pair in acquiredRewardCounts)
        {
            data.rewardCounts.Add(new RewardCountEntry { type = (int)pair.Key, count = pair.Value });
        }
    }

    // ロード: 保存済みの獲得数を復元する。各Tower.Start()のUpdateStatsFromRewards()が参照するため、
    // タワーの復元より必ず先に呼ぶこと
    public void ApplyLoadedState(List<RewardCountEntry> entries)
    {
        acquiredRewardCounts = CreateEmptyRewardCounts();
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                RewardType type = (RewardType)entry.type;
                if (acquiredRewardCounts.ContainsKey(type))
                {
                    acquiredRewardCounts[type] = entry.count;
                }
            }
        }
        OnRewardsUpdated?.Invoke();
    }

    // Step 4-0b-3: クライアント専用。ホストの獲得数（RewardTypeのint値をインデックスとする配列）を反映する。
    // ローカルでの報酬選択・効果適用(SelectReward/ApplyRewardEffect)は行わず、値が変わった時だけ
    // OnRewardsUpdatedを発火して、各タワーのステータス再計算とHUDインジケータを更新させる
    public void ApplyRemoteRewardCounts(int[] countsByType)
    {
        if (countsByType == null) return;

        bool changed = false;
        for (int i = 0; i < countsByType.Length; i++)
        {
            if (!Enum.IsDefined(typeof(RewardType), i)) continue;
            RewardType type = (RewardType)i;
            int next = Mathf.Max(0, countsByType[i]);
            acquiredRewardCounts.TryGetValue(type, out int current);
            if (current != next)
            {
                acquiredRewardCounts[type] = next;
                changed = true;
            }
        }

        if (changed)
        {
            OnRewardsUpdated?.Invoke();
        }
    }

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

    // ===== Step 4-8: 報酬の交互選択とホバー共有 =====
    // 選択者 = クリアしたWave番号のオーナー（奇数Wave→Player 1(Blue)=0 / 偶数Wave→Player 2(Orange)=1）。
    // シングルプレイでは-1（見出しなし・常に自分が選ぶ）
    public int CurrentChooserOwnerId { get; private set; } = -1;
    // 今提示している報酬のWave（=クリアしたWave）。コマンドの古さ判定に使う
    public int CurrentRewardWave { get; private set; } = 0;
    // 選択者が今ホバーしているカード(-1..2)。ホスト権威の値で、スナップショットでクライアントへ共有される
    public int ChooserHoverIndex { get; private set; } = -1;
    // 提示中（ホスト: 抽選後〜選択確定まで / クライアント: スナップショットで受信している間）
    public bool HasActiveOffers { get; private set; } = false;
    public IReadOnlyList<RewardOffer> CurrentOffers => currentOffers;

    private bool lastNetworkedState = false;
    // クライアント専用: 選択コマンド送信直後の連打防止（Time.unscaledTimeまで押せない）
    private float clientSelectLockUntil = -1f;
    private int clientSentHoverIndex = -1;

    private const int MaxOffers = 3;

    private static bool IsNetworkedCoop
    {
        get { return CoopNetworkManager.Instance != null && CoopNetworkManager.Instance.IsNetworked; }
    }

    private static bool IsClientMirror
    {
        get { return GameManager.Instance != null && GameManager.Instance.IsClientMirror; }
    }

    // Waveから選択者を決める。ネットワーク接続中でも相手が未接続/切断中ならホスト(0)にフォールバックする
    private static int ComputeChooser(int wave)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return -1;
        int chooser = (wave % 2 == 1) ? 0 : 1;
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net != null && net.IsNetworked && !net.IsPeerConnected) chooser = 0;
        return chooser;
    }

    // このデバイスのプレイヤーが今カードを選べるか
    private bool ComputeLocalCanPick()
    {
        if (CurrentChooserOwnerId < 0) return true;
        CoopNetworkManager net = CoopNetworkManager.Instance;
        if (net == null || !net.IsNetworked) return true; // PLAY ON THIS DEVICE: 誰でもクリック可
        return net.LocalOwnerId == CurrentChooserOwnerId;
    }

    // 現在の状態をRewardUIの見出し・操作可否へ反映する（再抽選はしない）
    private void RefreshUIState()
    {
        if (rewardUI == null) return;
        bool locked = Time.unscaledTime < clientSelectLockUntil;
        rewardUI.SetChooserState(CurrentChooserOwnerId >= 0, CurrentChooserOwnerId, IsNetworkedCoop, ComputeLocalCanPick(), locked);
        rewardUI.SetHoverHighlight(ChooserHoverIndex);
    }

    private void Update()
    {
        if (!HasActiveOffers) return;
        if (IsClientMirror)
        {
            // 連打防止ロックの解除（ホストに拒否された場合に操作不能のままにならないようにする）
            if (clientSelectLockUntil > 0f && Time.unscaledTime >= clientSelectLockUntil)
            {
                clientSelectLockUntil = -1f;
                RefreshUIState();
            }
            return;
        }

        // 提示中に相手が切断/再接続した場合、選択者をホスト(0)へ切り替えて即座に操作可能にする（待ちぼうけ防止）
        int chooser = ComputeChooser(CurrentRewardWave);
        bool networked = IsNetworkedCoop;
        if (chooser != CurrentChooserOwnerId || networked != lastNetworkedState)
        {
            CurrentChooserOwnerId = chooser;
            lastNetworkedState = networked;
            ChooserHoverIndex = -1;
            RefreshUIState();
        }
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

        CurrentRewardWave = GameManager.Instance != null ? GameManager.Instance.CurrentWave : 0;
        CurrentChooserOwnerId = ComputeChooser(CurrentRewardWave);
        lastNetworkedState = IsNetworkedCoop;
        ChooserHoverIndex = -1;
        HasActiveOffers = true;

        if (rewardUI != null)
        {
            rewardUI.DisplayRewards(currentOffers, CurrentChooserOwnerId >= 0, CurrentChooserOwnerId, IsNetworkedCoop, ComputeLocalCanPick());
        }
        else
        {
            Debug.LogError("[RewardManager] RewardUI reference is missing!");
            // UIがない場合のフォールバック（最初のものを自動選択）
            ExecuteSelect(0);
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
        int value = 0;
        if (type == RewardType.HealCore)
        {
            // B-2: コアHP≤5で回復量増加、コアHPが最大なら最大HP+1
            value = 2;
            if (GameManager.Instance != null)
            {
                int currentLife = GameManager.Instance.Life;
                int maxLife = GameManager.Instance.InitialLife;
                if (currentLife >= maxLife) value = -1; // 特殊値: 最大HP上昇を示す
                else if (currentLife <= 5) value = 3;
            }
        }
        return BuildOffer(type, value);
    }

    // (type, value)からTitle/Descriptionを組み立てる。HealCoreの文言はValueだけで決まる
    // （クライアントはスナップショットの(type, value)から同じ文言を再構築する）
    private static RewardOffer BuildOffer(RewardType type, int value)
    {
        RewardOffer offer = new RewardOffer { Type = type, Value = value };
        BuildOfferText(type, value, out offer.Title, out offer.Description);
        return offer;
    }

    private static void BuildOfferText(RewardType type, int value, out string title, out string description)
    {
        switch (type)
        {
            case RewardType.HealCore:
                if (value == -1)
                {
                    title = "Fortify Core";
                    description = "Core Max HP +1";
                }
                else if (value == 3)
                {
                    title = "Emergency Repair";
                    description = "Repair +3 Core HP";
                }
                else
                {
                    title = "Repair Core";
                    description = "Repair +2 Core HP";
                }
                return;
            case RewardType.IncreaseTowerDamage:
                description = "All Tower ATK +10% per stack";
                break;
            case RewardType.IncreaseTowerFireRate:
                description = "All Tower ATK Speed +10% per stack";
                break;
            case RewardType.IncreaseTowerRange:
                description = "All Tower Range +10% per stack";
                break;
            case RewardType.IncreaseTowerMaxHP:
                description = "All Tower Max HP +15% per stack (Compound)";
                break;
            case RewardType.IncreaseTowerArmor:
                description = $"All Tower Armor +5% per stack (max {CombatUtils.MaxArmor:0}%)";
                break;
            case RewardType.FrostAction:
                description = "Attacks slow enemy move & attack speed by 15% for 1s (stacks)";
                break;
            case RewardType.PiercingShot:
                description = "Attacks pierce to hit 1 nearby enemy (50% DMG, +10%/stack)";
                break;
            case RewardType.CoreShield:
                description = "Next wave: block 1 hit to the Core completely";
                break;
            default:
                description = "";
                break;
        }
        title = RewardTypeNames.Get(type);
    }

    // UIボタンから選択された際に実行される（このデバイスのローカル入力の入口）。
    // ホスト/ローカルで選択が確定した時だけtrueを返す。パネルの開閉はRewardManagerが管理する
    // （ホストは確定時にHidePanel、クライアントはフェーズ離脱をスナップショットで受けて閉じる）
    public bool SelectReward(int index)
    {
        if (index < 0 || index >= currentOffers.Count) return false;
        if (!HasActiveOffers) return false;

        // クライアント: ローカルでは適用せず、ホストへ要求する。結果はスナップショットで反映される
        if (IsClientMirror)
        {
            if (!ComputeLocalCanPick() || Time.unscaledTime < clientSelectLockUntil) return false;
            if (CoopCommandManager.Instance == null) return false;
            CoopCommandManager.Instance.SendSelectReward(CurrentRewardWave, index);
            clientSelectLockUntil = Time.unscaledTime + 1.0f; // 連打防止。拒否された場合はUpdate()で解除される
            RefreshUIState();
            return false;
        }

        // ホスト/ローカル: ネットワーク接続中は自分が選択者の時だけ受け付ける
        if (!ComputeLocalCanPick()) return false;
        return ExecuteSelect(index);
    }

    // ホスト側: クライアントからの選択コマンドの入口。ownerIdは送信元から導出された値
    public bool TrySelectRewardAsOwner(int ownerId, int wave, int index)
    {
        if (IsClientMirror || !HasActiveOffers) return false;
        if (GameManager.Instance == null || GameManager.Instance.CurrentPhase != GamePhase.Reward) return false;
        if (wave != CurrentRewardWave) return false;
        if (index < 0 || index >= currentOffers.Count) return false;
        if (ownerId != CurrentChooserOwnerId)
        {
            if (HUDManager.Instance != null) HUDManager.Instance.ShowToast("It's not your turn to pick");
            return false;
        }
        return ExecuteSelect(index);
    }

    // ホスト側: クライアントからのホバー報告。選択者本人からの有効な値だけ反映する
    public bool TrySetHoverAsOwner(int ownerId, int wave, int index)
    {
        if (IsClientMirror || !HasActiveOffers) return false;
        if (GameManager.Instance == null || GameManager.Instance.CurrentPhase != GamePhase.Reward) return false;
        if (wave != CurrentRewardWave || ownerId != CurrentChooserOwnerId) return false;
        if (index < -1 || index >= currentOffers.Count) return false;
        SetHoverIndex(index);
        return true;
    }

    // このデバイスで選択者がカードにホバーした/外れた時に呼ばれる（RewardUI経由。選択者本人の端末のみ）
    public void ReportLocalHover(int index)
    {
        if (!HasActiveOffers || !ComputeLocalCanPick()) return;
        if (index < -1 || index >= currentOffers.Count) index = -1;

        if (IsClientMirror)
        {
            // 値が変わった時だけ送る
            if (index == clientSentHoverIndex) return;
            clientSentHoverIndex = index;
            if (CoopCommandManager.Instance != null) CoopCommandManager.Instance.SendRewardHover(CurrentRewardWave, index);
            return;
        }

        SetHoverIndex(index);
    }

    private void SetHoverIndex(int index)
    {
        if (ChooserHoverIndex == index) return;
        ChooserHoverIndex = index;
        if (rewardUI != null) rewardUI.SetHoverHighlight(ChooserHoverIndex);
    }

    // ホスト/ローカルでの選択確定。二重選択はHasActiveOffersで防ぐ
    private bool ExecuteSelect(int index)
    {
        if (!HasActiveOffers || index < 0 || index >= currentOffers.Count) return false;
        HasActiveOffers = false;
        ChooserHoverIndex = -1;

        RewardOffer offer = currentOffers[index];
        ApplyRewardEffect(offer);

        if (acquiredRewardCounts.ContainsKey(offer.Type))
        {
            acquiredRewardCounts[offer.Type]++;
        }
        OnRewardsUpdated?.Invoke();

        Debug.Log($"[RewardManager] Selected Reward: {offer.Title}");

        if (rewardUI != null) rewardUI.HidePanel();

        // GameManagerに完了を通知して次のフェーズへ移行
        if (GameManager.Instance != null)
        {
            GameManager.Instance.CompleteRewardPhase();
        }
        return true;
    }

    // クライアント専用: スナップショットの報酬提示状態を反映する。
    // 提示あり&Rewardフェーズ→パネルを開く（Waveが変わったら差し替え）、無し/フェーズ離脱→閉じる。ローカルでは報酬を適用しない
    public void ApplyRemoteRewardOffer(bool hasOffer, int wave, int chooser, int hover,
        RewardType[] types, int[] values, int count)
    {
        bool inRewardPhase = GameManager.Instance != null && GameManager.Instance.CurrentPhase == GamePhase.Reward;
        if (!hasOffer || !inRewardPhase || types == null || values == null)
        {
            if (HasActiveOffers)
            {
                HasActiveOffers = false;
                currentOffers.Clear();
                ChooserHoverIndex = -1;
                clientSelectLockUntil = -1f;
                if (rewardUI != null) rewardUI.HidePanel();
            }
            return;
        }

        count = Mathf.Clamp(count, 0, Mathf.Min(MaxOffers, Mathf.Min(types.Length, values.Length)));

        if (!HasActiveOffers || wave != CurrentRewardWave)
        {
            currentOffers.Clear();
            for (int i = 0; i < count; i++)
            {
                currentOffers.Add(BuildOffer(types[i], values[i]));
            }
            HasActiveOffers = true;
            CurrentRewardWave = wave;
            CurrentChooserOwnerId = chooser;
            ChooserHoverIndex = hover;
            clientSentHoverIndex = -1;
            clientSelectLockUntil = -1f;
            if (rewardUI != null)
            {
                rewardUI.DisplayRewards(currentOffers, true, chooser, true, ComputeLocalCanPick());
                rewardUI.SetHoverHighlight(ChooserHoverIndex);
            }
            return;
        }

        bool changed = false;
        if (chooser != CurrentChooserOwnerId)
        {
            CurrentChooserOwnerId = chooser;
            clientSentHoverIndex = -1;
            changed = true;
        }
        if (hover != ChooserHoverIndex)
        {
            ChooserHoverIndex = hover;
            changed = true;
        }
        if (changed) RefreshUIState();
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
