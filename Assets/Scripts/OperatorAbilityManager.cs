using System.Collections.Generic;
using UnityEngine;

// Step 4-4: Operator Abilityの種別。
// 10章未決事項#3（選択タイミング）は未決のため、選択UIは実装せずInspectorのAbilityLoadoutで固定する。
public enum OperatorAbilityType
{
    Overcharge,
    FieldRepair,
    FreezeZone,
    TauntBeacon,
}

/// <summary>
/// CO-OP専用: Step 4-4 Operator Ability / Sync Combo。
/// GameManager.Start()が他のマネージャ(HUDManager等)をAddComponent()している方式に倣い、
/// 同じくAddComponent<OperatorAbilityManager>()で生成される想定（Prefab/シーンは一切変更しない）。
/// シングルプレイでは何もしない（GameManager.IsCoop==falseの間、Update()の先頭で即return）。
/// </summary>
public class OperatorAbilityManager : MonoBehaviour
{
    // Step 4-1のHUDManagerと同じ理由（AddComponentで動的生成されるため）でSingletonBehaviour<T>は使わず、
    // 最小限の静的参照だけを持たせる
    public static OperatorAbilityManager Instance { get; private set; }

    [System.Serializable]
    public class AbilityLoadout
    {
        public OperatorAbilityType slot1 = OperatorAbilityType.Overcharge;
        public OperatorAbilityType slot2 = OperatorAbilityType.FieldRepair;
    }

    [Header("Ability Loadouts (Step 4-4)")]
    [Tooltip("10章未決事項#3（選択タイミング）は未決のため、現状はInspectorで固定する。選択UIは未実装。")]
    [SerializeField]
    private AbilityLoadout player1Loadout = new AbilityLoadout
    {
        slot1 = OperatorAbilityType.Overcharge,
        slot2 = OperatorAbilityType.FieldRepair,
    };

    [SerializeField]
    private AbilityLoadout player2Loadout = new AbilityLoadout
    {
        slot1 = OperatorAbilityType.FreezeZone,
        slot2 = OperatorAbilityType.TauntBeacon,
    };

    // ===== クールダウン秒数(既定値。5章の表と一致させる) =====
    private const float OverchargeCooldown = 25f;
    private const float FieldRepairCooldown = 30f;
    private const float FreezeZoneCooldown = 35f;
    private const float TauntBeaconCooldown = 45f;

    // ===== 単体効果パラメータ =====
    private const float OverchargeDuration = 5f;
    private const float FieldRepairRadius = 3.0f;
    private const float FieldRepairHealPercent = 0.30f;
    private const float FreezeZoneRadius = 3.0f;
    private const float FreezeZoneSlowPercent = 0.50f;
    private const float FreezeZoneSlowDuration = 3.0f;
    private const float TauntBeaconRadius = 4.0f;
    private const float TauntBeaconDuration = 5.0f;

    // Overchargeの対象タワー選定（マウス位置からの許容誤差。タワーは1x1マス想定のコライダーを持つため
    // その半分強を許容範囲とする）
    private const float OverchargeTargetPickTolerance = 0.6f;

    // ===== Sync Combo パラメータ =====
    private const float ComboWindowSeconds = 2.0f;
    private const float ComboRadius = 3.0f;
    private const float ComboCooldownReductionRatio = 0.20f;
    private const float FocusFireRangeMultiplier = 1.5f;
    private const float DeepFreezeSlowPercent = 0.70f;
    private const float DeepFreezeSlowDuration = 8.0f;
    private const float ReinforceDuration = 8.0f;
    private const float ShatterDamagePercentOfBaselineHp = 0.40f;

    public enum ComboKind
    {
        FullBurst,
        DeepFreeze,
        FullRestore,
        Shatter,
        FocusFire,
        Reinforce,
    }

    // Combo成立時にHUDへ通知する（コンボ表示名。HUDManagerが大きな画面表示に使う）
    public event System.Action<string> OnComboTriggered;

    // ownerId(0/1) x slotIndex(0/1) の残クールダウン秒数
    private readonly float[,] cooldownRemaining = new float[2, 2];

    private struct AbilityActivation
    {
        public int ownerId;
        public int slotIndex;
        public OperatorAbilityType type;
        public Vector3 position;
        public float time;
    }

    // 直近のアビリティ発動記録。Sync Comboの相手候補として、次の発動時にこれと照合する
    private AbilityActivation? lastActivation = null;

    private void Awake()
    {
        // HUDManager.Start()より確実に先に参照可能にするため、Start()ではなくAwake()でInstanceを確定させる
        // （GameManager.Start()はHUDManagerを先にAddComponent()するが、Start()の呼び出し順は
        // 　AddComponent()の順序に厳密には依存しないため、Awake()側で保証する）
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // シングルプレイでは一切機能させない
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;

        // クールダウンはTime.deltaTimeベースで進行させ、ゲーム速度倍率(x1.2〜x3.0)に自動追随させる
        // （既存のOfflineグレースタイマー・Union Power承認タイムアウトと同方式）
        for (int p = 0; p < 2; p++)
        {
            for (int s = 0; s < 2; s++)
            {
                if (cooldownRemaining[p, s] > 0f)
                {
                    cooldownRemaining[p, s] = Mathf.Max(0f, cooldownRemaining[p, s] - Time.deltaTime);
                }
            }
        }

        // Sync Combo可能ウィンドウの期限切れを毎フレーム監視する（HUDのインジケータ判定にも使う）
        if (lastActivation.HasValue && Time.time - lastActivation.Value.time > ComboWindowSeconds)
        {
            lastActivation = null;
        }

        // 防衛(Defense)フェーズ中のみ発動可能。Setup/Rewardフェーズでは1/2キーを無視する
        if (GameManager.Instance.CurrentPhase != GamePhase.Defense) return;

        int activeOwnerId = GameManager.Instance.ActiveOwnerId;
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            TryActivateAbility(activeOwnerId, 0);
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            TryActivateAbility(activeOwnerId, 1);
        }
    }

    public OperatorAbilityType GetLoadout(int ownerId, int slotIndex)
    {
        AbilityLoadout loadout = ownerId == 1 ? player2Loadout : player1Loadout;
        return slotIndex == 1 ? loadout.slot2 : loadout.slot1;
    }

    public float GetCooldownRemaining(int ownerId, int slotIndex)
    {
        int p = Mathf.Clamp(ownerId, 0, 1);
        int s = Mathf.Clamp(slotIndex, 0, 1);
        return cooldownRemaining[p, s];
    }

    public static float GetCooldownDuration(OperatorAbilityType type)
    {
        switch (type)
        {
            case OperatorAbilityType.Overcharge: return OverchargeCooldown;
            case OperatorAbilityType.FieldRepair: return FieldRepairCooldown;
            case OperatorAbilityType.FreezeZone: return FreezeZoneCooldown;
            case OperatorAbilityType.TauntBeacon: return TauntBeaconCooldown;
            default: return 0f;
        }
    }

    // HUDのアイコン代わりに使うテキストラベル（英語）
    public static string GetAbilityLabel(OperatorAbilityType type)
    {
        switch (type)
        {
            case OperatorAbilityType.Overcharge: return "OVERCHARGE";
            case OperatorAbilityType.FieldRepair: return "FIELD REPAIR";
            case OperatorAbilityType.FreezeZone: return "FREEZE ZONE";
            case OperatorAbilityType.TauntBeacon: return "TAUNT BEACON";
            default: return type.ToString();
        }
    }

    // 相手(ownerIdでない方)が直近ComboWindowSeconds以内にアビリティを発動しており、
    // 今なら自分がSync Comboを狙える状態かどうか（HUDのCombo可能インジケータ表示用）
    public bool IsComboWindowOpenFor(int ownerId)
    {
        if (!lastActivation.HasValue) return false;
        AbilityActivation act = lastActivation.Value;
        if (act.ownerId == ownerId) return false;
        return (Time.time - act.time) <= ComboWindowSeconds;
    }

    /// <summary>
    /// アビリティの発動を試みる。ownerIdは要求元プレイヤー（ネットワーク層実装後は要求元クライアントの
    /// プレイヤーIDとして扱う想定。Step 4-0未実装の現段階ではGameManager.ActiveOwnerIdと一致する場合のみ受理する）。
    /// </summary>
    public void TryActivateAbility(int ownerId, int slotIndex)
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsCoop) return;
        if (GameManager.Instance.CurrentPhase != GamePhase.Defense) return;
        if (ownerId != GameManager.Instance.ActiveOwnerId) return;
        if (TowerManager.Instance == null) return;

        int p = Mathf.Clamp(ownerId, 0, 1);
        int s = Mathf.Clamp(slotIndex, 0, 1);

        OperatorAbilityType type = GetLoadout(p, s);

        if (cooldownRemaining[p, s] > 0f)
        {
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.ShowToast($"{GetAbilityLabel(type)} is on cooldown ({Mathf.CeilToInt(cooldownRemaining[p, s])}s)");
            }
            return;
        }

        Vector3 castPosition = GetMouseWorldPosition();

        // Overchargeは対象タワー必須。見つからない場合は発動自体を不成立にする（CD消費・Combo登録もしない）
        if (type == OperatorAbilityType.Overcharge && FindTowerAtPoint(castPosition) == null)
        {
            if (HUDManager.Instance != null)
            {
                HUDManager.Instance.ShowToast("Overcharge: no tower under cursor");
            }
            return;
        }

        AbilityActivation curr = new AbilityActivation
        {
            ownerId = p,
            slotIndex = s,
            type = type,
            position = castPosition,
            time = Time.time,
        };

        // ===== Sync Combo判定 =====
        // 「2人が2.0秒以内に半径3.0以内でアビリティを使用すると強化版が発動する」
        if (lastActivation.HasValue)
        {
            AbilityActivation prev = lastActivation.Value;
            float elapsed = Time.time - prev.time;
            float dist = Vector3.Distance(prev.position, castPosition);
            if (prev.ownerId != p && elapsed <= ComboWindowSeconds && dist <= ComboRadius)
            {
                ComboKind kind = ResolveComboKind(prev.type, type);
                ExecuteCombo(kind, prev, curr);

                // Combo成立時は両者のCDを20%短縮する。
                // curr側はまず通常どおりフルCDを設定してから短縮し、prev側は残っているCDに対して短縮を適用する
                cooldownRemaining[p, s] = GetCooldownDuration(type) * (1f - ComboCooldownReductionRatio);
                cooldownRemaining[prev.ownerId, prev.slotIndex] =
                    Mathf.Max(0f, cooldownRemaining[prev.ownerId, prev.slotIndex] * (1f - ComboCooldownReductionRatio));

                string comboName = GetComboDisplayName(kind);
                Debug.Log($"[OperatorAbilityManager] Sync Combo! {comboName} triggered by Player {p} ({type}) + Player {prev.ownerId} ({prev.type}).");
                SpawnComboFlash(castPosition, ComboRadius);
                OnComboTriggered?.Invoke(comboName);

                // 消費済み。3人目以降の連鎖は仕様上存在しない（2人固定）ため明示的にクリアする
                lastActivation = null;
                return;
            }
        }

        // ===== Combo不成立: 通常の単体効果 =====
        ExecuteAbilityEffect(type, castPosition, p, 1.0f);
        cooldownRemaining[p, s] = GetCooldownDuration(type);
        lastActivation = curr;
        Debug.Log($"[OperatorAbilityManager] Player {p} activated {type} at {castPosition}.");
    }

    private Vector3 GetMouseWorldPosition()
    {
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0f;
        return worldPos;
    }

    private Tower FindTowerAtPoint(Vector3 point)
    {
        if (TowerManager.Instance == null) return null;

        Tower best = null;
        float bestDist = OverchargeTargetPickTolerance;
        foreach (Tower t in TowerManager.Instance.GetActiveTowers())
        {
            if (t == null) continue;
            float d = Vector3.Distance(t.transform.position, point);
            if (d <= bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    // ===== 単体効果の実行 =====
    // radiusMultiplierは通常1.0。Focus Fireコンボから呼ばれる場合のみFocusFireRangeMultiplier(1.5)を渡す
    private void ExecuteAbilityEffect(OperatorAbilityType type, Vector3 position, int casterOwnerId, float radiusMultiplier)
    {
        switch (type)
        {
            case OperatorAbilityType.Overcharge:
                Tower target = FindTowerAtPoint(position);
                if (target != null)
                {
                    target.ApplyOvercharge(OverchargeDuration);
                }
                break;

            case OperatorAbilityType.FieldRepair:
                AreaHealTowers(position, FieldRepairRadius * radiusMultiplier);
                break;

            case OperatorAbilityType.FreezeZone:
                AreaSlowEnemies(position, FreezeZoneRadius * radiusMultiplier, FreezeZoneSlowPercent, FreezeZoneSlowDuration);
                break;

            case OperatorAbilityType.TauntBeacon:
                AreaTauntEnemies(position, TauntBeaconRadius * radiusMultiplier, TauntBeaconDuration);
                break;
        }
    }

    private void AreaHealTowers(Vector3 center, float radius)
    {
        if (TowerManager.Instance == null) return;
        foreach (Tower t in TowerManager.Instance.GetActiveTowers())
        {
            if (t == null || t.IsBarricade) continue;
            if (Vector3.Distance(t.transform.position, center) <= radius)
            {
                // Field Repair: 被回復キャップ(15%/秒)を無視する専用経路
                t.HealUncapped(t.MaxHp * FieldRepairHealPercent);
            }
        }
    }

    private void AreaSlowEnemies(Vector3 center, float radius, float percent, float duration)
    {
        if (EnemySpawner.Instance == null) return;
        foreach (Enemy e in EnemySpawner.Instance.GetActiveEnemies())
        {
            if (e == null) continue;
            if (Vector3.Distance(e.transform.position, center) <= radius)
            {
                // 既存のEnemy.ApplySlow()を経由するため、フロストタワーとの「強い方・長い方が優先」ルールがそのまま適用される
                e.ApplySlow(percent, duration);
            }
        }
    }

    private void AreaTauntEnemies(Vector3 center, float radius, float duration)
    {
        if (EnemySpawner.Instance == null) return;
        foreach (Enemy e in EnemySpawner.Instance.GetActiveEnemies())
        {
            if (e == null) continue;
            if (Vector3.Distance(e.transform.position, center) <= radius)
            {
                e.ApplyTaunt(center, duration);
            }
        }
    }

    // ===== Sync Combo =====

    // 優先順位: 同種コンボ(Full Burst/Deep Freeze/Full Restore) > Shatter > Focus Fire > Reinforce。
    // 組み合わせ表に複数該当するペア（例: Field Repair×Taunt BeaconはFocus Fire条件とReinforce条件の両方に
    // 該当する）が存在するため、CO-OPモード仕様書 5章の例示どおりにこの優先順位で確定させた
    private ComboKind ResolveComboKind(OperatorAbilityType a, OperatorAbilityType b)
    {
        if (a == b)
        {
            switch (a)
            {
                case OperatorAbilityType.Overcharge: return ComboKind.FullBurst;
                case OperatorAbilityType.FreezeZone: return ComboKind.DeepFreeze;
                case OperatorAbilityType.FieldRepair: return ComboKind.FullRestore;
                // Taunt Beacon×Taunt Beaconには専用コンボが定義されていないため、
                // 「Taunt Beacon×任意」のルール(Focus Fire＝自分自身の効果を1.5倍)をそのまま適用する
                case OperatorAbilityType.TauntBeacon: return ComboKind.FocusFire;
            }
        }

        bool hasOvercharge = a == OperatorAbilityType.Overcharge || b == OperatorAbilityType.Overcharge;
        bool hasFreezeZone = a == OperatorAbilityType.FreezeZone || b == OperatorAbilityType.FreezeZone;
        if (hasOvercharge && hasFreezeZone) return ComboKind.Shatter;

        bool hasTaunt = a == OperatorAbilityType.TauntBeacon || b == OperatorAbilityType.TauntBeacon;
        if (hasTaunt) return ComboKind.FocusFire;

        // ここに到達するのはField Repair×(Overcharge/FreezeZone)のみ（4種の全組み合わせを網羅済み）
        return ComboKind.Reinforce;
    }

    private void ExecuteCombo(ComboKind kind, AbilityActivation prev, AbilityActivation curr)
    {
        switch (kind)
        {
            case ComboKind.FullBurst:
                AreaOverchargeTowers(curr.position, ComboRadius);
                break;

            case ComboKind.DeepFreeze:
                AreaSlowEnemies(curr.position, ComboRadius, DeepFreezeSlowPercent, DeepFreezeSlowDuration);
                break;

            case ComboKind.FullRestore:
                AreaFullRestoreTowers(curr.position, ComboRadius);
                break;

            case ComboKind.Shatter:
            {
                // スロウを実際に適用したFreezeZone側の位置を中心にすることで、スロウが実際に届いた
                // エネミー群と確実に一致させる（OC側の位置を使うと、OC/FZの両位置がコンボ判定の
                // 半径3.0以内であっても、FZの効果範囲と完全には一致しない場合があるため）
                Vector3 freezeZonePos = prev.type == OperatorAbilityType.FreezeZone ? prev.position : curr.position;
                ApplyShatterDamage(freezeZonePos, ComboRadius, curr.ownerId);
                break;
            }

            case ComboKind.FocusFire:
            {
                // 「もう一方の効果」= Taunt Beacon以外の側（Taunt Beacon×Taunt Beaconの場合はcurr側=自分自身）を
                // 1.5倍の範囲で（再）実行する
                bool prevIsTaunt = prev.type == OperatorAbilityType.TauntBeacon;
                OperatorAbilityType otherType = prevIsTaunt ? curr.type : prev.type;
                Vector3 otherPos = prevIsTaunt ? curr.position : prev.position;
                int otherOwner = prevIsTaunt ? curr.ownerId : prev.ownerId;
                ExecuteAbilityEffect(otherType, otherPos, otherOwner, FocusFireRangeMultiplier);
                break;
            }

            case ComboKind.Reinforce:
            {
                Vector3 fieldRepairPos = prev.type == OperatorAbilityType.FieldRepair ? prev.position : curr.position;
                AreaReinforceTowers(fieldRepairPos, ComboRadius);
                break;
            }
        }
    }

    private void AreaOverchargeTowers(Vector3 center, float radius)
    {
        if (TowerManager.Instance == null) return;
        foreach (Tower t in TowerManager.Instance.GetActiveTowers())
        {
            if (t == null || t.IsBarricade) continue;
            if (Vector3.Distance(t.transform.position, center) <= radius)
            {
                t.ApplyOvercharge(OverchargeDuration);
            }
        }
    }

    private void AreaFullRestoreTowers(Vector3 center, float radius)
    {
        if (TowerManager.Instance == null) return;
        foreach (Tower t in TowerManager.Instance.GetActiveTowers())
        {
            if (t == null || t.IsBarricade) continue;
            if (Vector3.Distance(t.transform.position, center) <= radius)
            {
                t.HealFull();
                t.ResetOfflineGraceTimer();
            }
        }
    }

    private void AreaReinforceTowers(Vector3 center, float radius)
    {
        if (TowerManager.Instance == null) return;
        foreach (Tower t in TowerManager.Instance.GetActiveTowers())
        {
            if (t == null || t.IsBarricade) continue;
            if (Vector3.Distance(t.transform.position, center) <= radius)
            {
                t.HealUncapped(t.MaxHp * FieldRepairHealPercent);
                t.ApplyReinforce(ReinforceDuration);
            }
        }
    }

    // Shatter: スロウ中のエネミーへ固定ダメージ（そのウェーブの通常Enemy基準HPの40%相当）を与える。
    // 撃破の帰属はコンボを成立させた側(2人目に発動したプレイヤー=triggeringOwnerId)にする
    // （CO-OPモード仕様書 2章の簡略化ルール。既存のLast Hit経路(SetLastDamageOwner)をそのまま再利用する）
    private void ApplyShatterDamage(Vector3 center, float radius, int triggeringOwnerId)
    {
        if (EnemySpawner.Instance == null || GameManager.Instance == null) return;

        float bonusDamage = Enemy.GetStandardEnemyHpForWave(GameManager.Instance.CurrentWave) * ShatterDamagePercentOfBaselineHp;

        // 範囲ダメージによる撃破でリストが変動しうるため、Bullet.ApplySplashDamage()と同様にインデックス方式で走査する
        List<Enemy> enemies = EnemySpawner.Instance.GetActiveEnemies();
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            Enemy e = enemies[i];
            if (e == null || !e.IsSlowed) continue;
            if (Vector3.Distance(e.transform.position, center) > radius) continue;

            e.SetLastDamageOwner(triggeringOwnerId);
            e.TakeDamage(bonusDamage);
        }
    }

    private static string GetComboDisplayName(ComboKind kind)
    {
        switch (kind)
        {
            case ComboKind.FullBurst: return "Full Burst";
            case ComboKind.DeepFreeze: return "Deep Freeze";
            case ComboKind.FullRestore: return "Full Restore";
            case ComboKind.Shatter: return "Shatter";
            case ComboKind.FocusFire: return "Focus Fire";
            case ComboKind.Reinforce: return "Reinforce";
            default: return "Combo";
        }
    }

    // Combo成立位置に短時間表示する簡易な画面エフェクト（半径を示すフラッシュ）。
    // 既存のTowerRangeIndicatorは親タワー無しでも単独動作するためそのまま流用する
    private void SpawnComboFlash(Vector3 position, float radius)
    {
        GameObject obj = new GameObject("ComboFlash");
        obj.transform.position = position;
        TowerRangeIndicator indicator = obj.AddComponent<TowerRangeIndicator>();
        indicator.Init(radius, new Color(1f, 0.85f, 0.2f, 0.55f));
        indicator.SetVisible(true);
        Destroy(obj, 0.8f);
    }
}
