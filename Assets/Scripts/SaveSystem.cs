using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 1枠オートセーブの読み書きを担う静的クラス。Setupフェーズの盤面のみを対象とする
/// （敵・弾等の実行時状態が存在するDefense/Rewardフェーズ中は保存しない）。
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;

    private const string FileName = "savegame.json";

    public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    // タイトル画面のCONTINUEで読み込んだデータをMainGameシーンへ受け渡すための一時置き場。
    // GameManager.Start()が消費したら必ずnullへ戻すこと（Retry時の誤再適用を防ぐため）
    public static GameSaveData PendingLoad;

    public static bool HasSave()
    {
        if (!File.Exists(SavePath)) return false;
        return Load() != null;
    }

    public static GameSaveData Load()
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            string json = File.ReadAllText(SavePath);
            GameSaveData data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null || data.saveVersion != CurrentVersion) return null;
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Failed to load save file: {e.Message}");
            return null;
        }
    }

    public static bool Save(GameSaveData data)
    {
        if (data == null) return false;

        try
        {
            data.saveVersion = CurrentVersion;
            data.savedAtUtc = DateTime.UtcNow.ToString("o");

            string json = JsonUtility.ToJson(data);
            string tempPath = SavePath + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(SavePath))
            {
                File.Replace(tempPath, SavePath, null);
            }
            else
            {
                File.Move(tempPath, SavePath);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Failed to save: {e.Message}");
            return false;
        }
    }

    public static void DeleteSave()
    {
        try
        {
            if (File.Exists(SavePath))
            {
                File.Delete(SavePath);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveSystem] Failed to delete save file: {e.Message}");
        }
    }

    // 各マネージャから現在の状態を吸い上げて1つのGameSaveDataにまとめる
    public static GameSaveData CaptureCurrentState()
    {
        GameSaveData data = new GameSaveData();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.CaptureInto(data);
        }
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.CaptureInto(data);
        }
        if (TowerManager.Instance != null)
        {
            TowerManager.Instance.CaptureInto(data);
        }

        return data;
    }
}
