using Cysharp.Threading.Tasks;
using UnityEngine;

public class MainPhase : GamePhaseBase
{
    public override GamePhase Phase => GamePhase.Main;

    protected override void OnEnter()
    {
        System.GC.Collect();
        PopupManager.Instance.CloseAll();
    }

    protected override void OnExit()
    {
    }

    public override async UniTask RunAsync()
    {
        await UniTask.Never(Token);
    }
}
