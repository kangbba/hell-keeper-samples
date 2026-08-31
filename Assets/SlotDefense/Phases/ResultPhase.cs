using Cysharp.Threading.Tasks;
using UnityEngine;

public class ResultPhase : GamePhaseBase
{
    public override GamePhase Phase => GamePhase.Result;

    // Exposed so other systems can read the last battle outcome.
    public BattleResult BattleResult { get; private set; }
    public int CurrentWaveNumber { get; private set; }
    public int StageNumber { get; private set; }

    /// <summary>
    /// Receives battle result data from GamePhaseManager.
    /// </summary>
    public override void SetData(object data)
    {
        if (data is (BattleResult battleResult, int currentWaveNumber, int stageNumber))
        {
            BattleResult = battleResult;
            CurrentWaveNumber = currentWaveNumber;
            StageNumber = stageNumber;
            Log.d($"[ResultPhase] SetData - result: {battleResult}, Wave: {CurrentWaveNumber}, Stage: {stageNumber}");
        }
        else
        {
            Log.w($"[ResultPhase] Unexpected data type: {data?.GetType()}");
        }
    }

    protected override void OnEnter()
    {
        Log.d($"[ResultPhase] OnEnter - result: {BattleResult}, Wave: {CurrentWaveNumber}");
    }

    public override async UniTask RunAsync()
    {
        // ResultScreen drives its own presentation; nothing to run here.
        await UniTask.CompletedTask;
    }

    protected override void OnExit()
    {
        BattleResult = BattleResult.None;
        CurrentWaveNumber = 0;
        StageNumber = 0;
    }
}
