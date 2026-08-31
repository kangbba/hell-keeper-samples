using Cysharp.Threading.Tasks;
using UnityEngine;
using System.Threading;

public abstract class GamePhaseBase
{
    private CancellationTokenSource _cts;
    public CancellationToken Token => _cts?.Token ?? CancellationToken.None;

    public abstract GamePhase Phase { get; }

    /// <summary>
    /// Injects data into the phase. Optional; override in phases that need it.
    /// </summary>
    public virtual void SetData(object data)
    {
        // Default implementation: no-op.
    }

    /// <summary> Enters the phase. A fresh cancellation token is created automatically. </summary>
    public void Enter()
    {
        CancelToken();
        CreateToken();
        OnEnter();
    }

    protected abstract void OnEnter();

    /// <summary>
    /// Runs the phase; may await indefinitely. Every await inside must take Token, which
    /// Exit cancels — that is the only thing that stops a phase.
    /// </summary>
    public abstract UniTask RunAsync();

    /// <summary> Exits the phase. The token is cancelled and disposed automatically. </summary>
    public void Exit()
    {
        OnExit();
        CancelToken();
    }

    protected abstract void OnExit();

    protected void CreateToken()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    protected void CancelToken()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
