using UnityEngine;
using UniRx;

[System.Serializable]
public class InGameUpgradeData
{
    public int TargetStarLevel;
    public Sprite Icon;

    // The level is the one writable fact; the manager increments it.
    public ReactiveProperty<int> CurrentUpgradeLevel { get; private set; }

    // Cost is a read-only view derived from the level — never stored, so it can never drift.
    public IReadOnlyReactiveProperty<int> UpgradeCost { get; private set; }

    public InGameUpgradeData(
        int targetStarLevel,
        Sprite icon,
        ReactiveProperty<int> currentUpgradeLevel,
        IReadOnlyReactiveProperty<int> upgradeCost)
    {
        TargetStarLevel = targetStarLevel;
        Icon = icon;
        CurrentUpgradeLevel = currentUpgradeLevel;
        UpgradeCost = upgradeCost;
    }
}
