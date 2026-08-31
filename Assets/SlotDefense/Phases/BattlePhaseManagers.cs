using UnityEngine;

/// <summary>
/// Bundle of the managers the battle phase needs.
/// </summary>
public class BattlePhaseManagers : MonoBehaviour
{
    [Header("Manager References")]
    public BattleManager BattleManager;
    public HeroManager HeroManager;
    public MergeManager MergeManager;
    public DragManager DragManager;
    public EnemyUIManager EnemyUIManager;
    public InGameUpgradeManager InGameUpgradeManager;

    private void OnDestroy()
    {
        HeroManager.Instance.DestroyAllHeroes();
    }
}