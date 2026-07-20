using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 戦闘関連の共通処理（ターゲット探索・アーマー軽減計算）。
/// </summary>
public static class CombatUtils
{
    /// <summary>
    /// 同距離とみなす許容誤差。この範囲内の最短距離候補は同格として扱う。
    /// </summary>
    private const float DistanceTieEpsilon = 0.01f;

    /// <summary>
    /// 射程内で最も近い候補を返す。filterを指定した場合は条件を満たすもののみ対象。
    /// 最短距離が同格（誤差DistanceTieEpsilon以内）の候補が複数ある場合はランダムに1つを選ぶ。
    /// </summary>
    public static T FindNearestInRange<T>(Vector3 origin, float range, List<T> candidates, Func<T, bool> filter = null) where T : Component
    {
        float shortestDistance = float.MaxValue;
        List<T> tiedBest = null;

        foreach (T candidate in candidates)
        {
            if (candidate == null) continue;
            if (filter != null && !filter(candidate)) continue;

            float distance = Vector3.Distance(origin, candidate.transform.position);
            if (distance > range) continue;

            if (distance < shortestDistance - DistanceTieEpsilon)
            {
                shortestDistance = distance;
                tiedBest = new List<T> { candidate };
            }
            else if (distance < shortestDistance + DistanceTieEpsilon)
            {
                if (distance < shortestDistance) shortestDistance = distance;
                tiedBest ??= new List<T>();
                tiedBest.Add(candidate);
            }
        }

        if (tiedBest == null || tiedBest.Count == 0) return null;
        return tiedBest.Count == 1 ? tiedBest[0] : tiedBest[UnityEngine.Random.Range(0, tiedBest.Count)];
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
