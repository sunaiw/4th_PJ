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
