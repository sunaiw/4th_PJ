using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RewardUI : MonoBehaviour
{
    [Header("UI Panels")]
    [SerializeField] private GameObject rewardPanel;

    [Header("Reward Cards (Buttons)")]
    [SerializeField] private Button[] cardButtons;
    [SerializeField] private TMP_Text[] titleTexts;
    [SerializeField] private TMP_Text[] descriptionTexts;

    // HUDManagerのOwnerColorBlue/OwnerColorOrangeと同じ値（プレイヤー識別色の一貫性のため）
    private static readonly Color OwnerColorBlue = new Color(0.3f, 0.6f, 1.0f);
    private static readonly Color OwnerColorOrange = new Color(1.0f, 0.6f, 0.2f);

    // Step 4-8: 実行時に生成する見出しと、カードごとのホバー強調枠
    private TMP_Text headingText;
    private Outline[] hoverFrames;

    // Step 4-10: CO-OPのみ表示する貢献ストリップ（パネル下部）。タイトル1行＋プレイヤーごとに1行
    private GameObject contributionStripObj;
    private TMP_Text contributionTitleText;
    private readonly TMP_Text[] contributionRowTexts = new TMP_Text[2];

    // 現在の表示状態（RewardManagerからSetChooserState/DisplayRewardsで更新される）
    private int chooserOwnerId = -1;
    private bool networked;
    private bool localCanPick = true;
    private bool cardsLocked;
    private int localHoverIndex = -1;
    private int sharedHoverIndex = -1;

    private void Start()
    {
        // 他のUIと同じダークスレート＋ゴールドのテーマに揃える。
        // rewardPanel自体は全画面の暗転オーバーレイなので、スプライト/枠は付けず色だけ変える
        if (rewardPanel != null)
        {
            Image overlay = rewardPanel.GetComponent<Image>();
            if (overlay != null) overlay.color = new Color(0.02f, 0.03f, 0.06f, 0.8f);
        }
        hoverFrames = new Outline[cardButtons.Length];
        for (int i = 0; i < cardButtons.Length; i++)
        {
            Button cardButton = cardButtons[i];
            if (cardButton == null) continue;
            Image cardImage = cardButton.GetComponent<Image>();
            UITheme.ApplyButton(cardImage);
            if (cardImage != null) cardImage.color = UITheme.ButtonHover;
            // Image.colorに対するステート倍率で、通常=ButtonBg相当/ホバー=ButtonHover相当になるようにする
            ColorBlock colors = cardButton.colors;
            colors.normalColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.selectedColor = colors.normalColor;
            // 非選択者(操作不可)でもカードが暗く沈まないよう、無効時の色は通常色に揃える
            colors.disabledColor = colors.normalColor;
            cardButton.colors = colors;

            // Step 4-8: 選択者のホバー共有用の強調枠（HUDManager/AbilityLoadoutUIと同じOutline方式）と、ホバー通知
            Outline frame = cardButton.gameObject.AddComponent<Outline>();
            frame.effectDistance = new Vector2(5f, -5f);
            frame.enabled = false;
            hoverFrames[i] = frame;

            RewardCardHover hover = cardButton.gameObject.AddComponent<RewardCardHover>();
            hover.Init(this, i);
        }
        for (int i = 0; i < titleTexts.Length; i++)
        {
            if (titleTexts[i] != null) titleTexts[i].color = UITheme.Gold;
        }
        for (int i = 0; i < descriptionTexts.Length; i++)
        {
            if (descriptionTexts[i] != null) descriptionTexts[i].color = new Color(0.9f, 0.92f, 0.96f, 1f);
        }

        CreateHeading();
        CreateContributionStrip();

        // 初期状態は非表示
        if (rewardPanel != null)
        {
            rewardPanel.SetActive(false);
        }

        // 3択ボタンのリスナー登録
        for (int i = 0; i < cardButtons.Length; i++)
        {
            int index = i; // クロージャ問題を回避するためのローカルコピー
            if (cardButtons[i] != null)
            {
                cardButtons[i].onClick.AddListener(() => OnCardSelected(index));
            }
        }
    }

    // パネル上部の見出し（誰が選ぶか）を実行時に生成する
    private void CreateHeading()
    {
        if (rewardPanel == null) return;

        GameObject obj = new GameObject("ChooserHeading");
        obj.transform.SetParent(rewardPanel.transform, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontSize = 44;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = UITheme.Gold;
        tmp.raycastTarget = false;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -90f);
        rect.sizeDelta = new Vector2(1400f, 70f);

        headingText = tmp;
        headingText.gameObject.SetActive(false);
    }

    // Step 4-10: 直前に終えたウェーブの撃破数・与ダメージ・救援回数を表示するストリップを実行時に生成する。
    // 初期は非表示。表示可否と内容はRefreshContribution()が決める
    private void CreateContributionStrip()
    {
        if (rewardPanel == null) return;

        GameObject obj = new GameObject("ContributionStrip");
        obj.transform.SetParent(rewardPanel.transform, false);

        Image bg = obj.AddComponent<Image>();
        bg.color = UITheme.PanelBgDeep;
        UITheme.ApplyPanel(bg);
        bg.raycastTarget = false;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 40f);
        rect.sizeDelta = new Vector2(1100f, 140f);

        contributionTitleText = CreateStripText("ContributionTitle", obj.transform, new Vector2(0f, 48f), 28, UITheme.Gold);
        for (int owner = 0; owner < 2; owner++)
        {
            contributionRowTexts[owner] = CreateStripText("ContributionRow" + owner, obj.transform,
                new Vector2(0f, 8f - 40f * owner), 30, GetOwnerColor(owner));
        }

        contributionStripObj = obj;
        contributionStripObj.SetActive(false);
    }

    private static TMP_Text CreateStripText(string name, Transform parent, Vector2 anchoredPos, float fontSize, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(1060f, 40f);
        return tmp;
    }

    // 貢献ストリップの表示可否と内容を更新する。CO-OP以外では常に非表示。
    // 値はGameManagerのウェーブ別統計（ホストは集計値、クライアントはスナップショット反映値）を参照する
    private void RefreshContribution()
    {
        if (contributionStripObj == null) return;

        GameManager gm = GameManager.Instance;
        bool show = gm != null && gm.IsCoop && rewardPanel != null && rewardPanel.activeSelf;
        if (contributionStripObj.activeSelf != show) contributionStripObj.SetActive(show);
        if (!show) return;

        contributionTitleText.text = $"WAVE {gm.CurrentWave} CONTRIBUTION";
        for (int owner = 0; owner < 2; owner++)
        {
            string label = owner == 1 ? "PLAYER 2 (ORANGE)" : "PLAYER 1 (BLUE)";
            contributionRowTexts[owner].text =
                $"{label}   Kills {gm.GetKillCount(owner)}   Damage {Mathf.RoundToInt(gm.GetWaveDamageDealt(owner))}   Rescues {gm.GetWaveRescues(owner)}";
        }
    }

    // クライアントはパネル表示中にもスナップショットで統計が更新され得るため、表示中は毎フレーム内容を追随させる
    // （テキスト代入のみで、パネルが閉じている間は何もしない）
    private void Update()
    {
        if (rewardPanel != null && rewardPanel.activeSelf) RefreshContribution();
    }

    private static Color GetOwnerColor(int ownerId)
    {
        return ownerId == 1 ? OwnerColorOrange : OwnerColorBlue;
    }

    // offers: 提示する報酬。以降の引数は見出しと操作可否（SetChooserStateと同じ意味）
    public void DisplayRewards(List<RewardOffer> offers, bool showHeading, int chooserOwnerId, bool networked, bool localCanPick)
    {
        if (rewardPanel == null) return;

        rewardPanel.SetActive(true);
        localHoverIndex = -1;
        sharedHoverIndex = -1;

        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (i < offers.Count)
            {
                if (cardButtons[i] != null) cardButtons[i].gameObject.SetActive(true);
                if (titleTexts[i] != null && i < titleTexts.Length) titleTexts[i].text = offers[i].Title;
                if (descriptionTexts[i] != null && i < descriptionTexts.Length) descriptionTexts[i].text = offers[i].Description;
            }
            else
            {
                if (cardButtons[i] != null) cardButtons[i].gameObject.SetActive(false);
            }
        }

        SetChooserState(showHeading, chooserOwnerId, networked, localCanPick, false);
        RefreshContribution();
    }

    // 再抽選せずに見出し・操作可否・ホバー強調枠だけを更新する
    // showHeading=false（シングルプレイ）では見出しを出さない
    public void SetChooserState(bool showHeading, int chooserOwnerId, bool networked, bool localCanPick, bool cardsLocked)
    {
        this.chooserOwnerId = chooserOwnerId;
        this.networked = networked;
        this.localCanPick = localCanPick;
        this.cardsLocked = cardsLocked;

        if (headingText != null)
        {
            headingText.gameObject.SetActive(showHeading);
            if (showHeading)
            {
                string playerLabel = chooserOwnerId == 1 ? "PLAYER 2 (ORANGE)" : "PLAYER 1 (BLUE)";
                if (!networked)
                {
                    headingText.text = $"{playerLabel} PICKS"; // 1台プレイ: 誰がクリックしてもよい
                }
                else if (localCanPick)
                {
                    headingText.text = "YOUR PICK";
                }
                else
                {
                    headingText.text = $"{playerLabel} IS CHOOSING...";
                }
                headingText.color = GetOwnerColor(chooserOwnerId);
            }
        }

        bool interactable = localCanPick && !cardsLocked;
        for (int i = 0; i < cardButtons.Length; i++)
        {
            if (cardButtons[i] != null) cardButtons[i].interactable = interactable;
        }

        UpdateHoverFrames();
    }

    // 選択者のホバー位置（-1..2）を反映する。強調枠は非選択者側の端末にだけ表示する
    // （選択者側は通常のボタンホバー表示で足りる）
    public void SetHoverHighlight(int index)
    {
        sharedHoverIndex = index;
        UpdateHoverFrames();
    }

    private void UpdateHoverFrames()
    {
        if (hoverFrames == null) return;
        bool show = networked && !localCanPick && chooserOwnerId >= 0;
        for (int i = 0; i < hoverFrames.Length; i++)
        {
            if (hoverFrames[i] == null) continue;
            hoverFrames[i].effectColor = GetOwnerColor(chooserOwnerId);
            hoverFrames[i].enabled = show && i == sharedHoverIndex;
        }
    }

    public void HidePanel()
    {
        if (rewardPanel != null) rewardPanel.SetActive(false);
        RefreshContribution();
        localHoverIndex = -1;
        sharedHoverIndex = -1;
        UpdateHoverFrames();
    }

    // RewardCardHoverからの通知。共有するのは選択者本人の端末だけ（RewardManager側でも判定される）
    public void OnCardHoverEnter(int index)
    {
        localHoverIndex = index;
        if (!localCanPick || cardsLocked) return;
        if (RewardManager.Instance != null) RewardManager.Instance.ReportLocalHover(index);
    }

    public void OnCardHoverExit(int index)
    {
        if (localHoverIndex != index) return; // 別カードへのEnterが先に来ていた場合は-1にしない
        localHoverIndex = -1;
        if (!localCanPick) return;
        if (RewardManager.Instance != null) RewardManager.Instance.ReportLocalHover(-1);
    }

    private void OnCardSelected(int index)
    {
        if (!localCanPick || cardsLocked) return;

        // パネルはここでは閉じない。選択が受理された時だけRewardManagerが閉じる
        // （ホスト: 確定時にHidePanel / クライアント: ホストがRewardフェーズを抜けたスナップショットで閉じる）
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.SelectReward(index);
        }
    }
}
