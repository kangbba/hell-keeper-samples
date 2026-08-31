using UnityEngine;
using System.Collections.Generic;
using UniRx;
using System;

/// <summary>
/// In-run star-level upgrades. One ReactiveProperty per star level is the only state;
/// price is derived from it with Select(), damage multiplier is computed from it on
/// read, and the panel UI just subscribes. Raising the level (one line in
/// OnUpgradeButtonClicked) updates all three for free.
/// </summary>
public class InGameUpgradeManager : SingletonMono<InGameUpgradeManager>
{
    [Header("Prefab Reference")]
    [SerializeField] private InGameUpgradePanel _upgradePanelPrefab;

    [Header("Star Icons")]
    [SerializeField] private Sprite _star1Icon;
    [SerializeField] private Sprite _star2Icon;
    [SerializeField] private Sprite _star3Icon;
    [SerializeField] private Sprite _star4Icon;
    [SerializeField] private Sprite _star5Icon;

    private InGameUpgradePanel _activePanel;
    private readonly Dictionary<int, Sprite> _iconCache = new();
    private readonly Dictionary<int, InGameUpgradeData> _upgradeDataDict = new();

    protected override bool UseDontDestroyOnLoad => false;

    protected override void Release()
    {
        foreach (var data in _upgradeDataDict.Values)
        {
            data?.CurrentUpgradeLevel?.Dispose();
            // UpgradeCost comes from ToReactiveProperty(), so it owns a subscription to dispose.
            if (data?.UpgradeCost is IDisposable disposable)
                disposable?.Dispose();
        }
        _upgradeDataDict.Clear();
        _iconCache.Clear();
    }

    protected override void Awake()
    {
        base.Awake();
        LoadStarIcons();
        InitUpgradeData(); // per-star data created once, shared with the panel
    }

    private void LoadStarIcons()
    {
        _iconCache.Clear();
        _iconCache[1] = _star1Icon;
        _iconCache[2] = _star2Icon;
        _iconCache[3] = _star3Icon;
        _iconCache[4] = _star4Icon;
        _iconCache[5] = _star5Icon;
    }

    private void InitUpgradeData()
    {
        _upgradeDataDict.Clear();

        for (int star = 1; star <= 5; star++)
        {
            var icon = _iconCache.TryGetValue(star, out var s) ? s : null;

            // The writable fact.
            var upgradeLevel = new ReactiveProperty<int>(1);

            // Read-only derivation: reacts to level changes, formula lives in Balances.
            IReadOnlyReactiveProperty<int> upgradeCost =
                upgradeLevel.Select(lv => InGameUpgradeBalances.GetInGameUpgradePrice(lv)).ToReactiveProperty();

            var data = new InGameUpgradeData(
                targetStarLevel: star,
                icon: icon,
                currentUpgradeLevel: upgradeLevel,
                upgradeCost: upgradeCost
            );

            _upgradeDataDict[star] = data;
        }
    }

    // ---- Panel control ----

    public void ShowUpgradePanel()
    {
        if (_activePanel != null) return;

        if (_upgradePanelPrefab == null)
        {
            Log.e("[InGameUpgradeManager] UpgradePanel prefab not assigned");
            return;
        }

        _activePanel = PopupManager.Instance.InstantiateShow(_upgradePanelPrefab);

        // Shared data — buttons are built once and stay subscribed.
        _activePanel.Init(new List<InGameUpgradeData>(_upgradeDataDict.Values), this);
        _activePanel.FadeIn(0.5f);
    }

    public void HideUpgradePanel()
    {
        if (_activePanel == null) return;
        _activePanel.FadeOutAndDestroy(0.5f);
        _activePanel = null;
    }

    public void ToggleUpgradePanel()
    {
        if (_activePanel == null) ShowUpgradePanel();
        else HideUpgradePanel();
    }

    public bool IsPanelVisible => _activePanel != null;

    /// <summary>Damage multiplier for a star level, computed from the current level on read.</summary>
    public float GetUpgradeMultiplier(int starLevel)
    {
        if (!_upgradeDataDict.TryGetValue(starLevel, out var data))
            return 1f;

        return InGameUpgradeBalances.GetInGameUpgradeDamageMultiplier(data.CurrentUpgradeLevel.Value);
    }

    public void OnUpgradeButtonClicked(int targetStarLevel)
    {
        if (!_upgradeDataDict.TryGetValue(targetStarLevel, out var data))
        {
            Log.e($"[InGameUpgradeManager] no data for star level {targetStarLevel}");
            return;
        }

        int cost = data.UpgradeCost.Value;
        if (DataManager.Element.Value < cost)
        {
            ToastManager.ShowToast("엘리먼트가 부족합니다"); // "Not enough Element" — Korean-only release, no localization table
            return;
        }

        // Pay, then bump the level — cost display, battle multiplier and button UI all follow.
        DataManager.SpendElement(cost);
        data.CurrentUpgradeLevel.Value++;

        var heroes = HeroManager.Instance.GetActiveHeroesByStarLevel(targetStarLevel);
        foreach (var hero in heroes)
        {
            hero.PlayHighlightEffect();
        }
    }
}
