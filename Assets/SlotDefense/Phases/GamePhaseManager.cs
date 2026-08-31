using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UniRx;
using UnityEngine;

public enum GamePhase
{
    None   = -1,
    Main   = 0,
    Battle = 1,
    Result = 2,
}

public class GamePhaseManager : SingletonMono<GamePhaseManager>
{
    private readonly ReactiveProperty<GamePhase> _currentPhaseType = new(GamePhase.Main);
    public IReadOnlyReactiveProperty<GamePhase> CurrentPhaseType => _currentPhaseType;

    private GamePhaseBase _currentPhase;
    public GamePhaseBase CurrentPhase { get => _currentPhase; }
    protected override bool UseDontDestroyOnLoad => false;

    protected override void Release()
    {
        _currentPhase?.Exit();
        _currentPhase = null;

        _currentPhaseType?.Dispose();
    }

    public void SetPhase(GamePhase newPhase)
    {
        SetPhaseAsync(newPhase, null).Forget();
    }

    public void SetPhase(GamePhase newPhase, object data)
    {
        SetPhaseAsync(newPhase, data).Forget();
    }

    private async UniTask SetPhaseAsync(GamePhase newPhase, object data)
    {
        if (newPhase == GamePhase.None)
        {
            await SetPhaseAsync(GamePhase.Main, null);
            return;
        }

        // Ordering matters: exit old phase, hide old UI, then create/inject/show/enter the
        // new one. Exit cancels the phase's own token, which is what actually unwinds its
        // RunAsync and releases the await below in whichever call started it.
        if (_currentPhase != null)
        {
            _currentPhase.Exit();
            _currentPhase = null;
        }

        PopupManager.Instance.CloseAll();

        UIManager.Instance.HideCurrentScreen();

        _currentPhase = CreatePhase(newPhase);
        _currentPhaseType.Value = newPhase;

        if (_currentPhase != null && data != null)
        {
            _currentPhase.SetData(data);
        }

        UIManager.Instance.ShowScreen(newPhase);

        _currentPhase.Enter();

        try
        {
            await _currentPhase.RunAsync();
        }
        catch (OperationCanceledException)
        {
            // Expected: a phase transition cancelled this phase.
        }
    }

    // A new phase instance is created on every transition.
    private GamePhaseBase CreatePhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Main => new MainPhase(),
            GamePhase.Battle => new BattlePhase(),
            GamePhase.Result => new ResultPhase(),
            _ => null
        };
    }

    public T GetPhase<T>() where T : GamePhaseBase
    {
        return _currentPhase as T;
    }
}
