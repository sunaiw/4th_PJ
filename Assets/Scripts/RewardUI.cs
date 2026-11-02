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

    private void Start()
    {
        // 他のUIと同じダークスレート＋ゴールドのテーマに揃える。
        // rewardPanel自体は全画面の暗転オーバーレイなので、スプライト/枠は付けず色だけ変える
        if (rewardPanel != null)
        {
            Image overlay = rewardPanel.GetComponent<Image>();
            if (overlay != null) overlay.color = new Color(0.02f, 0.03f, 0.06f, 0.8f);
        }
        foreach (Button cardButton in cardButtons)
        {
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
            cardButton.colors = colors;
        }
        for (int i = 0; i < titleTexts.Length; i++)
        {
            if (titleTexts[i] != null) titleTexts[i].color = UITheme.Gold;
        }
        for (int i = 0; i < descriptionTexts.Length; i++)
        {
            if (descriptionTexts[i] != null) descriptionTexts[i].color = new Color(0.9f, 0.92f, 0.96f, 1f);
        }

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

    public void DisplayRewards(List<RewardOffer> offers)
    {
        if (rewardPanel == null) return;

        rewardPanel.SetActive(true);

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
    }

    private void OnCardSelected(int index)
    {
        if (rewardPanel != null)
        {
            rewardPanel.SetActive(false);
        }
        
        if (RewardManager.Instance != null)
        {
            RewardManager.Instance.SelectReward(index);
        }
    }
}
