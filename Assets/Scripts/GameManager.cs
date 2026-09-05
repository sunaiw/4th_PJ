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

    [Header("CO-OP (Step 4-1)")]
    // ネットワーク層はStep 4-0bで実装されたが、ホスト/参加が自動的にCO-OPを意味するわけではまだない。
    // 現段階でもforceCoopModeはローカルのデバッグフラグ（1人で両プレイヤーownerId 0/1を操作して検証する用途）
    // としての意味を保ち続ける
    // TODO: 後続ステップでHOST GAME/JOIN GAMEを選んだ場合にforceCoopModeを自動的にtrueとみなすようにする
    [SerializeField] private bool forceCoopMode = false;
    public bool IsCoop => forceCoopMode;

    // 現在操作中のプレイヤー。CO-OP時のみTabキーで0⇔1を切り替える（シングルプレイでは常に0固定）。
    // Step 4-0a: 入力層と実行層を分離済み（TowerManager.TryPlaceTower等はownerIdを明示的な引数として
    // 受け取るようになった）。Step 4-0b-1でネットワーク層（CoopNetworkManager）を実装し、このプロパティは
    // 「ホストによって割り当てられた、このクライアントが操作するプレイヤー」を表せるようになった。
    // 下のUpdate()にあるTabキーでのトグルは、両プレイヤーが1台のデバイスを共有する「PLAY ON THIS DEVICE」の
    // ローカル検証用の仕組みであり、CoopNetworkManager.IsNetworked==trueの実ネットワーク接続時は無効化され、
    // 値はHandleCoopConnectionStateChanged()経由でCoopNetworkManager.LocalOwnerIdに固定される
    public int ActiveOwnerId { get; private set; } = 0;
    public event Action<int> OnActiveOwnerChanged;

    // Events for UI bindings
    public event Action<GamePhase> OnPhaseChanged;
    public event Action<int> OnLifeChanged;
    public event Action<int> OnCostChanged;
    public event Action<int> OnWaveNumberChanged;

    // Step 4-3: 二層コスト（Personal Cost / Union Power）とTransferのイベント。CO-OP時のみ意味を持つ
    // 引数: (ownerId, 変更後の値)
    public event Action<int, int> OnPersonalCostChanged;
    public event Action<int> OnUnionPowerChanged;
    public event Action<int> OnTransferRemainingChanged;

    public GamePhase CurrentPhase => currentPhase;
    public int CurrentWave => currentWave;
    public int Life => life;
    public int Cost => cost;
    public int InitialLife => initialLife;
    public int MaxCostForCurrentWave => GetMaxCostForWave(currentWave);

    private bool setupPhaseFinished = false;
    private bool rewardPhaseFinished = false;

    // セーブ/ロード: タイトルのCONTINUEで読み込まれたデータ。GameLoopCoroutine先頭で他マネージャの
    // Start()完了を待ってから消費し、消費後はnullへ戻す
    private GameSaveData pendingLoadData = null;
    // ロード直後の最初のSetupフェーズのみ、Setup冒頭のリソース再配布（cost再計算・killCountsリセット）を
    // スキップし、保存済みの値をそのまま使う
    private bool skipNextSetupResourceReset = false;

    // B-1: 敵撃破ボーナス
    // Step 4-1: プレイヤー別（ownerId 0/1）に撃破数を保持する。KillCountは既存参照互換のため両者の合計を返す
    private int[] killCounts = new int[2];
    public int KillCount => killCounts[0] + killCounts[1];

    // Core Shield: 次ウェーブ1回限りのダメージ無効化
    private bool coreShieldActive = false;
    public bool CoreShieldActive => coreShieldActive;

    [Header("CO-OP Resources (Step 4-3)")]
    // 二層コスト。既存の単一プール(cost)はシングルプレイ専用として意味を変えず残し、
    // CO-OP時のみ以下のプレイヤー別Personal CostとUnionPowerを別途管理する
    private int[] personalCost = new int[2];
    // 各プールのそのウェーブの上限（撃破ボーナスを含まない値）。売却返還時のクランプに使う。
    // Transferで受け取った分だけはこのクランプを経由しないため上限を超過できる
    private int[] personalCostCapForWave = new int[2];
    private int unionPower = 0;
    private int unionPowerCapForWave = 0;

    // Transfer: Setupフェーズ中のみ、1ウェーブ最大3回まで、自分のPersonal Costを相手へ1ずつ譲渡できる
    public const int TransferMaxPerWave = 3;
    private int transferRemaining = 0;
    public int TransferRemaining => transferRemaining;

    public int GetPersonalCost(int ownerId)
    {
        int idx = (ownerId >= 0 && ownerId < personalCost.Length) ? ownerId : 0;
        return personalCost[idx];
    }

    public int UnionPower => unionPower;

    // 第1項: 既存GetMaxCostForWave()の半分（切り上げ）。第2項は無く、撃破ボーナスは呼び出し側で別途加算する
    public int GetMaxPersonalCostForWave(int wave) => Mathf.CeilToInt(GetMaxCostForWave(wave) / 2f);

    // Wave 2以下は0（Healer解禁がWave3のため使い道が無い）、Wave3以降は3 + min(wave/3, 4)
    public int GetMaxUnionPowerForWave(int wave) => wave <= 2 ? 0 : 3 + Mathf.Min(wave / 3, 4);

    public int MaxPersonalCostForCurrentWave => GetMaxPersonalCostForWave(currentWave);
    public int MaxUnionPowerForCurrentWave => GetMaxUnionPowerForWave(currentWave);

    // 配置時の消費。プールが足りなければfalseを返し何も変化させない
    public bool SpendPersonalCost(int ownerId, int amount)
    {
        int idx = (ownerId >= 0 && ownerId < personalCost.Length) ? ownerId : 0;
        if (personalCost[idx] >= amount)
        {
            personalCost[idx] -= amount;
            OnPersonalCostChanged?.Invoke(idx, personalCost[idx]);
            return true;
        }
        return false;
    }

    // 売却返還用。そのウェーブのPersonal Cost上限（撃破ボーナスを含まない値）でクランプする。
    // Transferによる譲渡はこのメソッドを経由しない別経路（TryTransferCost）のため上限を超過できる
    public void AddPersonalCost(int ownerId, int amount)
    {
        int idx = (ownerId >= 0 && ownerId < personalCost.Length) ? ownerId : 0;
        personalCost[idx] = Mathf.Clamp(personalCost[idx] + amount, 0, personalCostCapForWave[idx]);
        OnPersonalCostChanged?.Invoke(idx, personalCost[idx]);
    }

    // Union Power承認時の消費。TowerManagerの承認フロー(ApproveUnionRequest)から呼ばれる
    public bool SpendUnionPower(int amount)
    {
        if (unionPower >= amount)
        {
            unionPower -= amount;
            OnUnionPowerChanged?.Invoke(unionPower);
            return true;
        }
        return false;
    }

    // Union系タワーの売却返還用。そのウェーブのUnion Power上限でクランプする
    public void AddUnionPower(int amount)
    {
        unionPower = Mathf.Clamp(unionPower + amount, 0, unionPowerCapForWave);
        OnUnionPowerChanged?.Invoke(unionPower);
    }

    // Step 4-3 Transfer: Setupフェーズ中のみ、fromOwnerIdのPersonal Costを1相手へ譲渡する。
    // 譲渡先では上限クランプを適用しない（AddPersonalCost()の通常クランプ経路とは別にする）
    public bool TryTransferCost(int fromOwnerId)
    {
        if (!IsCoop || currentPhase != GamePhase.Setup) return false;
        if (transferRemaining <= 0) return false;

        int fromIdx = (fromOwnerId >= 0 && fromOwnerId < personalCost.Length) ? fromOwnerId : 0;
        int toIdx = fromIdx == 0 ? 1 : 0;
        if (personalCost[fromIdx] < 1) return false;

        personalCost[fromIdx] -= 1;
        personalCost[toIdx] += 1; // 意図的に上限クランプを適用しない（受け取った側は超過を許容する）
        transferRemaining--;

        OnPersonalCostChanged?.Invoke(fromIdx, personalCost[fromIdx]);
        OnPersonalCostChanged?.Invoke(toIdx, personalCost[toIdx]);
        OnTransferRemainingChanged?.Invoke(transferRemaining);
        return true;
    }

    private void Start()
    {
        // セーブ/ロード: タイトルのCONTINUEから読み込まれたデータがあれば、通常のInitializeGame()の
        // 代わりにスカラー状態（wave/life/cost等）だけをここで即時反映する。他マネージャ（MapManager/
        // TowerManager/RewardManager）に依存する復元（タワー再生成・壁解放・報酬適用）はまだ実行できない
        // （それらのStart()がこの時点で完了している保証がないため）。GameLoopCoroutine側で行う
        if (SaveSystem.PendingLoad != null)
        {
            pendingLoadData = SaveSystem.PendingLoad;
            SaveSystem.PendingLoad = null;
            ApplyLoadedScalarState(pendingLoadData);
            skipNextSetupResourceReset = true;
        }
        else
        {
            InitializeGame();
        }

        gameObject.AddComponent<HUDManager>();
        gameObject.AddComponent<TutorialUI>();
        gameObject.AddComponent<GameSpeedController>();
        // Step 4-4: CO-OP専用のOperator Ability管理。シングルプレイではIsCoop==falseのため
        // 自身のUpdate()の先頭で即returnし、一切機能しない
        gameObject.AddComponent<OperatorAbilityManager>();
        // Step 4-0b-1: CO-OP用LAN接続層。シングルプレイでもAddComponent自体は行われるが、
        // StartHost/StartClientが実際に呼ばれない限りNetworkManager等のGameObjectは一切生成されない
        gameObject.AddComponent<CoopNetworkManager>();
        CoopNetworkManager.Instance.OnConnectionStateChanged += HandleCoopConnectionStateChanged;
        // Step 4-0b-1: CO-OP専用の接続選択モーダル（HOST/JOIN/PLAY ON THIS DEVICE）。
        // AbilityLoadoutUIより手前に表示する必要があるため、そのAddComponentより前に置く。
        // シングルプレイでは自身のStart()の先頭で即returnし、GameObjectを一切生成しない
        gameObject.AddComponent<CoopConnectUI>();
        // Step 4-4: CO-OP専用のAbility選択モーダル（起動時に1回だけ表示）。シングルプレイでは
        // 自身のStart()の先頭で即returnし、GameObjectを一切生成しない
        gameObject.AddComponent<AbilityLoadoutUI>();
        // セーブ・ロード・終了機能: ESCキーで開くポーズメニュー
        gameObject.AddComponent<PauseMenuUI>();
        new GameObject("GameOverUI").AddComponent<GameOverUI>();
        StartCoroutine(GameLoopCoroutine());
    }

    // Step 4-1: CO-OP時のみ、Tabキーで操作中プレイヤー(ActiveOwnerId)を0⇔1に切り替える
    // （「PLAY ON THIS DEVICE」で両プレイヤーを1台のデバイスで検証するためのローカル用途）。
    // Step 4-0b-1: ネットワーク接続中（CoopNetworkManager.IsNetworked==true）はこのトグルを無効化する。
    // 実ネットワーク上では操作対象プレイヤーはホスト/クライアントの役割で固定されるべきであり、
    // Tabで相手側を操作できてしまうと権威（どちらの入力がどちらのプレイヤーに属するか）が崩れるため
    private void Update()
    {
        if (!IsCoop) return;

        bool tabToggleAllowed = CoopNetworkManager.Instance == null || !CoopNetworkManager.Instance.IsNetworked;
        if (tabToggleAllowed && Input.GetKeyDown(KeyCode.Tab))
        {
            ActiveOwnerId = ActiveOwnerId == 0 ? 1 : 0;
            OnActiveOwnerChanged?.Invoke(ActiveOwnerId);
            Debug.Log($"[GameManager] Active owner switched to Player {ActiveOwnerId}.");
        }

        // Step 4-3 Transfer: Setupフェーズ中のみ、Tキーで操作中プレイヤーから相手へPersonal Costを1譲渡する
        if (currentPhase == GamePhase.Setup && Input.GetKeyDown(KeyCode.T))
        {
            if (TryTransferCost(ActiveOwnerId))
            {
                Debug.Log($"[GameManager] Player {ActiveOwnerId} transferred 1 personal cost to the other player. Remaining transfers: {transferRemaining}.");
            }
        }
    }

    // Step 4-0b-1: CoopNetworkManagerの接続状態変化(接続確立・切断・シャットダウン)を購読するハンドラ。
    // ネットワーク接続が確立された（IsNetworked==trueになった）タイミングで一度だけActiveOwnerIdを
    // ホスト=0(Blue)/クライアント=1(Orange)に固定する。
    // 逆にネットワーク接続が確立されなかった場合（タイムアウト・接続拒否）や、確立後に切断された場合
    // （IsNetworked==falseに戻った場合）は、PLAY ON THIS DEVICE相当のローカル状態（Player 1起点）に戻す。
    // これを怠ると、接続に失敗した直後にPLAY ON THIS DEVICEを選んだ際にPlayer 2(Orange)起点のまま
    // 開始してしまう不具合が起きるため必須。値が実際に変化した場合のみイベントを発火し、無駄な通知を避ける
    private void HandleCoopConnectionStateChanged()
    {
        if (CoopNetworkManager.Instance == null) return;

        if (CoopNetworkManager.Instance.IsNetworked)
        {
            ActiveOwnerId = CoopNetworkManager.Instance.LocalOwnerId;
            OnActiveOwnerChanged?.Invoke(ActiveOwnerId);
            Debug.Log($"[GameManager] Active owner fixed to Player {ActiveOwnerId} by network connection.");
        }
        else if (ActiveOwnerId != 0)
        {
            ActiveOwnerId = 0;
            OnActiveOwnerChanged?.Invoke(ActiveOwnerId);
            Debug.Log("[GameManager] Active owner reset to Player 0 (network connection not established or lost).");
        }
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

    // セーブ/ロード: 他マネージャに依存しないスカラー状態のみをここで即時反映する
    // （タワー再生成・壁解放・報酬適用はGameLoopCoroutine側で行う）
    private void ApplyLoadedScalarState(GameSaveData data)
    {
        currentWave = data.currentWave;
        life = data.life;
        initialLife = data.initialLife;
        cost = data.cost;
        coreShieldActive = data.coreShieldActive;
        killCounts[0] = (data.killCounts != null && data.killCounts.Length > 0) ? data.killCounts[0] : 0;
        killCounts[1] = (data.killCounts != null && data.killCounts.Length > 1) ? data.killCounts[1] : 0;
        currentPhase = GamePhase.Setup;

        OnLifeChanged?.Invoke(life);
        OnCostChanged?.Invoke(cost);
        OnWaveNumberChanged?.Invoke(currentWave);
    }

    // セーブ: 現在のスカラー状態を吸い上げる
    public void CaptureInto(GameSaveData data)
    {
        data.currentWave = currentWave;
        data.life = life;
        data.initialLife = initialLife;
        data.cost = cost;
        data.coreShieldActive = coreShieldActive;
        data.killCounts = new int[] { killCounts[0], killCounts[1] };
    }

    // タイトル/リトライ遷移前にGameManager（シーン間永続）を明示的に破棄する。
    // 怠ると遷移先で新旧2つのGameManagerが競合する
    public static void DestroyPersistentInstance()
    {
        if (Instance != null)
        {
            DestroyImmediate(Instance.gameObject);
        }
    }

    private IEnumerator GameLoopCoroutine()
    {
        // セーブ/ロード: 他マネージャ（MapManager/TowerManager/RewardManager）のStart()完了を
        // 保証するため1フレーム待ってから復元する
        if (pendingLoadData != null)
        {
            yield return null;

            if (RewardManager.Instance != null)
            {
                // Tower.Start()のUpdateStatsFromRewards()が参照するため、タワー復元より必ず先に適用する
                RewardManager.Instance.ApplyLoadedState(pendingLoadData.rewardCounts);
            }
            if (MapManager.Instance != null && pendingLoadData.currentWave > 1)
            {
                // 壁の解放範囲はWaveに対して単調増加するため、1回の呼び出しで累積分を再現できる
                MapManager.Instance.ExpandMap(pendingLoadData.currentWave - 1);
            }
            if (TowerManager.Instance != null)
            {
                TowerManager.Instance.RestoreTowers(pendingLoadData);
            }

            pendingLoadData = null;

            // RestoreTowers()で生成したタワーはInstantiate直後（Awake()実行済み・Start()未実行）の
            // 状態のまま次のSetupフェーズへ進んでしまう。Start()内のUpdateStatsFromRewards()適用と
            // PrepareRestoredState()の反映（currentHp/placedWave/requiresSupply）を待たないと、
            // 直後のオートセーブが不完全な状態を上書き保存してしまうため1フレーム待つ
            yield return null;
        }

        while (currentPhase != GamePhase.GameOver)
        {
            // 1. Setup Phase (準備フェーズ)
            currentPhase = GamePhase.Setup;

            int maxCost;
            int killBonus;
            if (skipNextSetupResourceReset)
            {
                // ロード直後の最初のSetupのみ、保存済みのcost/killCountsをそのまま使う
                // （ApplyLoadedScalarState()で既に設定済み）
                skipNextSetupResourceReset = false;
                maxCost = cost;
                killBonus = 0;
            }
            else
            {
                maxCost = GetMaxCostForWave(currentWave);
                // B-1: 前ウェーブの敵撃破数に応じたボーナスコスト (10体ごとに+1、最大+3)。ボーナスは上限をさらに超過できる
                // Step 4-3のコスト分離までは、撃破ボーナスは従来どおり両者合計のKillCountを使う
                killBonus = Mathf.Min(KillCount / 10, 3);
                cost = maxCost + killBonus;

                // Step 4-3: CO-OP時のみ、二層コスト（Personal Cost / Union Power）とTransfer回数をリセットする。
                // シングルプレイの挙動（上のcost算出）には一切影響させない。
                // 撃破ボーナスの算出には、直後でリセットされる killCounts をリセット前に参照する必要がある
                if (IsCoop)
                {
                    int personalCap = GetMaxPersonalCostForWave(currentWave);
                    int unionCap = GetMaxUnionPowerForWave(currentWave);
                    personalCostCapForWave[0] = personalCap;
                    personalCostCapForWave[1] = personalCap;
                    unionPowerCapForWave = unionCap;

                    for (int p = 0; p < 2; p++)
                    {
                        int personalKillBonus = Mathf.Min(GetKillCount(p) / 10, 3);
                        personalCost[p] = personalCap + personalKillBonus; // 撃破ボーナスは上限を超えて加算可（シングルと同じ仕様）
                        OnPersonalCostChanged?.Invoke(p, personalCost[p]);
                    }

                    unionPower = unionCap;
                    OnUnionPowerChanged?.Invoke(unionPower);

                    transferRemaining = TransferMaxPerWave;
                    OnTransferRemainingChanged?.Invoke(transferRemaining);

                    Debug.Log($"[GameManager] CO-OP resources reset for Wave {currentWave}. Personal: {personalCost[0]}/{personalCost[1]} (cap {personalCap}), Union: {unionPower} (cap {unionCap}).");
                }

                killCounts[0] = 0;
                killCounts[1] = 0; // 撃破カウントリセット（プレイヤー別）
            }

            setupPhaseFinished = false;
            OnPhaseChanged?.Invoke(currentPhase);
            OnCostChanged?.Invoke(cost);
            Debug.Log($"[GameManager] Setup Phase started for Wave {currentWave}. Cost: {cost} (Base: {maxCost}, Kill Bonus: {killBonus}). Place your towers!");

            // セーブ・ロード・終了機能: Setupフェーズ確定直後にオートセーブする（CO-OPは対象外）
            if (!IsCoop)
            {
                SaveSystem.Save(SaveSystem.CaptureCurrentState());
            }

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

    // Step 4-1: 撃破ボーナスの帰属先（ownerId）を受け取る。範囲外の値は0にクランプする
    public void AddKill(int ownerId)
    {
        int idx = (ownerId >= 0 && ownerId < killCounts.Length) ? ownerId : 0;
        killCounts[idx]++;
    }

    public int GetKillCount(int ownerId)
    {
        int idx = (ownerId >= 0 && ownerId < killCounts.Length) ? ownerId : 0;
        return killCounts[idx];
    }

    private void TriggerGameOver()
    {
        currentPhase = GamePhase.GameOver;
        // この周回は終了したため、オートセーブからCONTINUEできないようにする
        SaveSystem.DeleteSave();
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
