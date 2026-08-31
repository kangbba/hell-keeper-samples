using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// One run: wait for the first slot roll, fight, then play the result out and hand the
/// outcome to the result phase.
///
/// Every await takes Token, so a phase transition unwinds the run wherever it is. Giving
/// up is the same path as losing, minus the battle.
///
/// Dependencies come in through BattleContext, resolved once in OnEnter; only the
/// enter/exit boundary talks to globals directly.
/// </summary>
public class BattlePhase : GamePhaseBase
{
    public override GamePhase Phase => GamePhase.Battle;

    // Result presentation, in the order it plays.
    private const float VictoryPauseSeconds = 1f;
    private const float VictoryFadeOutSeconds = 0.5f;
    private const float VictoryPortalEffectSeconds = 3f;
    private const float VictoryResultPopupSeconds = 3f;

    private const float DefeatPauseSeconds = 1f;
    private const float DefeatFadeOutSeconds = 0.5f;
    private const float DefeatIslandFallSeconds = 2.5f;
    private const float DefeatResultPopupSeconds = 3f;

    private BattlePhaseManagers _bundle;
    private BattleContext _ctx;

    private bool _giveUpRequested;

    // Once the win or loss sequence starts, giving up is too late: the outcome is already
    // being presented and would otherwise be reported twice.
    private bool _resultPlaying;

    protected override void OnEnter()
    {
        _giveUpRequested = false;
        _resultPlaying = false;

        // Singletons are resolved here, once. The run body only touches _ctx.
        _ctx = BattleContext.FromManagers();

        _bundle = Resource.Instantiate<BattlePhaseManagers>(ResourceId.BattlePhaseManagers);

        var battleField = _bundle.BattleManager.MakeBattleField();
        battleField.LockCornerCells(GrowthManager.Instance.GetCurrentCornerUnlockLevel());
        _bundle.BattleManager.MakeSellBox();

        DamageTracker.Instance.ResetAndStop();
        DamageTracker.Instance.StartTracking();
    }

    public override async UniTask RunAsync()
    {
        // The run does not start until the player spins, but giving up has to be possible
        // while they sit on that decision.
        await UniTask.WaitUntil(
            () =>
            {
                if (_giveUpRequested) return true;

                var slotUI = _ctx.Slot != null ? _ctx.Slot.CurrentSlotUI : null;
                return slotUI == null || slotUI.RollCount > 0;
            },
            cancellationToken: Token);

        if (_giveUpRequested)
        {
            return;
        }

        BattleResult result = await _ctx.Battle.BattleAsync(Token);

        // GiveUp means OnGiveUpAsync is already presenting the loss.
        if (result == BattleResult.GiveUp)
        {
            return;
        }

        switch (result)
        {
            case BattleResult.Victory:
                await PlayVictoryAsync();
                await ShowResultPopupAsync(result, VictoryResultPopupSeconds);
                break;

            case BattleResult.Defeat_Overcrowding:
            case BattleResult.Defeat_Timeout:
                await PlayDefeatAsync();
                await ShowResultPopupAsync(result, DefeatResultPopupSeconds);
                break;
        }

        GoToResultPhase(result);
    }

    /// <summary>Give up button. Ends the run through the defeat presentation.</summary>
    public async UniTask OnGiveUpAsync()
    {
        if (_resultPlaying)
        {
            return;
        }

        // Releases the WaitUntil above if the player never spun.
        _giveUpRequested = true;

        _ctx.Battle.CurrentBattleField?.BattleStop(destroyEnemies: false);

        await PlayDefeatAsync();
        await ShowResultPopupAsync(BattleResult.GiveUp, DefeatResultPopupSeconds);

        GoToResultPhase(BattleResult.GiveUp);
    }

    /// <summary>
    /// The one place a run ends. Reads what the result screen needs before the battle
    /// field is torn down, since Exit runs as part of the transition.
    /// </summary>
    private void GoToResultPhase(BattleResult result)
    {
        var battleField = _ctx.Battle.CurrentBattleField;
        int waveNumber = battleField?.WaveManager?.CurrentWaveNumber?.Value ?? 0;
        int stageNumber = _ctx.GetStageNumber();

        GamePhaseManager.Instance.SetPhase(GamePhase.Result, (result, waveNumber, stageNumber));
    }

    // ---- Result presentation ----

    /// <summary>Stops everything that is still acting, so the outcome plays over a still field.</summary>
    private void StopBattleActivity(BattleScreen battleScreen)
    {
        var battleField = _ctx.Battle.CurrentBattleField;

        _ctx.Battle.DestroySellBox();
        _ctx.Slot.CurrentSlotUI.ForceStopRolling();
        battleScreen.WarningTimer.Stop();
        _ctx.Heroes.AllHeroesStopAttack();
        battleField.WaveManager.PauseWaves(true);
        battleField.EnemyManager.SetAllEnemiesActive(false);
    }

    /// <summary>Shared opening of both sequences: freeze the field, hold, fade the HUD out.</summary>
    private async UniTask<BattleField> BeginResultAsync(float pauseSeconds, float fadeOutSeconds)
    {
        _resultPlaying = true;

        var battleScreen = _ctx.UI.CurrentScreen as BattleScreen;
        if (battleScreen == null)
        {
            return null;
        }

        StopBattleActivity(battleScreen);

        await UniTask.WaitForSeconds(pauseSeconds, cancellationToken: Token);

        battleScreen.FadeOut(fadeOutSeconds);
        await UniTask.WaitForSeconds(fadeOutSeconds, cancellationToken: Token);

        return _ctx.Battle.CurrentBattleField;
    }

    private async UniTask PlayVictoryAsync()
    {
        var battleField = await BeginResultAsync(VictoryPauseSeconds, VictoryFadeOutSeconds);
        if (battleField == null)
        {
            return;
        }

        battleField.DestroyPortalWithEffect();
        await UniTask.WaitForSeconds(VictoryPortalEffectSeconds, cancellationToken: Token);
    }

    private async UniTask PlayDefeatAsync()
    {
        var battleField = await BeginResultAsync(DefeatPauseSeconds, DefeatFadeOutSeconds);
        if (battleField == null)
        {
            return;
        }

        // Copied first: DestroyHero mutates ActiveHeroes.
        foreach (var hero in new List<Hero>(_ctx.Heroes.ActiveHeroes))
        {
            if (hero == null) continue;

            ParticleManager.PlayParticle(
                Particles.Root_Par_EnemyDie,
                hero.transform.localPosition,
                scale: 1f,
                destroyAfter: 1f);

            _ctx.Heroes.DestroyHero(hero);
        }

        await battleField.PlayIslandFallDownAsync(DefeatIslandFallSeconds);
    }

    private async UniTask ShowResultPopupAsync(BattleResult result, float seconds)
    {
        var battleScreen = _ctx.UI.CurrentScreen as BattleScreen;

        GameObject prefab = battleScreen?.GetResultPanelPrefab(result);
        if (prefab == null)
        {
            // GiveUp has no panel of its own; the run still ends.
            return;
        }

        GameObject popup = _ctx.Popups.InstantiateShow(prefab);

        try
        {
            await UniTask.WaitForSeconds(seconds, cancellationToken: Token);
        }
        finally
        {
            if (popup != null)
            {
                _ctx.Popups.Close(popup);
            }
        }
    }

    protected override void OnExit()
    {
        DamageTracker.Instance.StopTracking();
        DataManager.SetElement(0);

        if (_bundle != null)
        {
            Object.Destroy(_bundle.gameObject);
            _bundle = null;
        }

        SettingManager.Instance.OpenSettingPanel(false);
    }
}
