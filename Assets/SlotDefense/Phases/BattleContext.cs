using System;

/// <summary>
/// What one battle run needs from the rest of the game, resolved once at phase entry.
/// The run body reads this instead of reaching for globals, so the phase's real
/// dependencies are visible in one place — FromManagers() is the only spot that
/// knows the singletons exist.
/// </summary>
public readonly struct BattleContext
{
    public readonly BattleManager Battle;
    public readonly HeroManager Heroes;
    public readonly SlotManager Slot;
    public readonly UIManager UI;
    public readonly PopupManager Popups;
    public readonly Func<int> GetStageNumber;

    public BattleContext(
        BattleManager battle,
        HeroManager heroes,
        SlotManager slot,
        UIManager ui,
        PopupManager popups,
        Func<int> getStageNumber)
    {
        Battle = battle;
        Heroes = heroes;
        Slot = slot;
        UI = ui;
        Popups = popups;
        GetStageNumber = getStageNumber;
    }

    public static BattleContext FromManagers() => new(
        BattleManager.Instance,
        HeroManager.Instance,
        SlotManager.Instance,
        UIManager.Instance,
        PopupManager.Instance,
        () => DataManager.StageNumber.Value);
}
