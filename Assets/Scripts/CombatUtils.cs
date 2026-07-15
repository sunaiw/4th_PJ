using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 戦闘関連の共通処理（ターゲット探索・アーマー軽減計算）。
/// </summary>
public static class CombatUtils
{
    /// <summary>
    /// 射程内で最も近い候補を返す。filterを指定した場合は条件を満たすもののみ対象。
    /// </summary>
    public static T FindNearestInRange<T>(Vector3 origin, float range, List<T> candidates, Func<T, bool> filter = null) where T : Component
    {
        T best = null;
        float shortestDistance = float.MaxValue;

        foreach (T candidate in candidates)
        {
            if (candidate == null) continue;
            if (filter != null && !filter(candidate)) continue;

            float distance = Vector3.Distance(origin, candidate.transform.position);
            if (distance <= range && distance < shortestDistance)
            {
                shortestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// アーマー値(0～100)による軽減後のダメージを返す。
    /// </summary>
    public static float ApplyArmorReduction(float damage, float armor)
    {
        float damageReduction = Mathf.Clamp(armor, 0f, 100f) / 100.0f;
        return damage * (1.0f - damageReduction);
    }
}
