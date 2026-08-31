using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

// ---- Interfaces ----
public interface IAttackable
{
    void StartAttackLoop();
    void StopAttackLoop();
}

public enum TargetingMode
{
    Nearest,
    Random
}

[RequireComponent(typeof(Hero))]
public abstract class HeroCombat : MonoBehaviour, IAttackable
{
    private Hero _hero;

    /// <summary>The hero this drives, on the same object.</summary>
    protected Hero Hero => _hero;

    // Attack loop state
    private bool _isAttacking = false;
    private float _nextAttackTime;

    public HeroUniqueData UniqueData => Hero.UniqueData;

    // ---- Buff checks ----
    protected bool HasReinforcedAttack => Hero.HeroBuff.HasBuff(BuffType.ReinforcedAttack);

    // ---- Final stat calculation (buffs applied) ----
    public float FinalAttackInterval
    {
        get
        {
            float baseInterval = Hero.RuntimeBaseData.AttackInterval;

            // Attack speed boost (multiplicative stacking)
            float attackSpeedMultiplier = Hero.HeroAttackBoost?.GetMultiplier(AttackBoostType.AttackSpeed) ?? 1f;

            // Apply DoubleAttackSpeed buff (2x speed)
            if (Hero.HeroBuff.HasBuff(BuffType.DoubleAttackSpeed))
            {
                attackSpeedMultiplier *= 2f;
            }

            // Final interval = base interval / total multiplier
            float finalInterval = baseInterval / Mathf.Max(0.01f, attackSpeedMultiplier);
            return finalInterval;
        }
    }

    public float FinalAttackRange
    {
        get
        {
            float baseRange = Hero.RuntimeBaseData.AttackRange;

            // Attack range multiplier from HeroAttackBoost
            float rangeMultiplier = Hero.HeroAttackBoost?.GetMultiplier(AttackBoostType.AttackRange) ?? 1f;

            // Final range (multiplicative stacking)
            float finalRange = baseRange * Mathf.Max(0.01f, rangeMultiplier);

            return finalRange;
        }
    }

    /// <summary>Multiplicative on the sheet's base value: star 2 = 2.1x, star 3 = 4.41x.</summary>
    public static int CalculateDamage(
        float baseDamage,
        int starLevel,
        float inGameUpgradeMultiplier,
        float attackBoostMultiplier)
    {
        float starLevelMultiplier = Mathf.Pow(2.1f, starLevel - 1);
        float finalValue = baseDamage * starLevelMultiplier * inGameUpgradeMultiplier * attackBoostMultiplier;

        return Mathf.RoundToInt(finalValue);
    }

    public int FinalDamage => CalculateDamage(
        Hero.RuntimeBaseData.AttackPower,
        Hero.StarLevel,
        InGameUpgradeManager.Instance?.GetUpgradeMultiplier(Hero.StarLevel) ?? 1f,
        Hero.HeroAttackBoost?.GetMultiplier(AttackBoostType.AttackPower) ?? 1f);

    protected virtual void Awake()
    {
        _hero = GetComponent<Hero>();
    }

    protected abstract void LoadUniqueData();

    /// <summary>
    /// The hero's own row of the balance sheet, as the type that hero expects. Wrong
    /// wiring is reported here rather than surfacing as a null field mid-battle.
    /// </summary>
    protected T ReadUniqueData<T>() where T : HeroUniqueData
    {
        if (UniqueData is T typed)
        {
            return typed;
        }

        Log.e($"[{GetType().Name}] Expected {typeof(T).Name}, got {UniqueData?.GetType().Name ?? "null"}.");
        return null;
    }

    // ---- Initialization ----

    /// <summary>Called by the spawner once the hero knows its type and star level.</summary>
    public void Init()
    {
        if (Hero.UniqueData == null)
        {
            Log.e($"[{GetType().Name}] Init before the hero's data was assigned.");
            return;
        }

        LoadUniqueData();
    }
    // ---- Attack loop ----
    private void OnDisable() => StopAttackLoop();
    private void OnDestroy() => StopAttackLoop();

    private void Update()
    {
        if (!_isAttacking) return;

        // Time.time is scaled, so this only covers the frame a pause lands on.
        if (Time.timeScale <= 0f) return;

        if (Time.time >= _nextAttackTime)
        {
            Attack();
            _nextAttackTime = Time.time + FinalAttackInterval;
        }
    }

    public void StartAttackLoop()
    {
        _isAttacking = true;
        _nextAttackTime = Time.time; // Attack immediately
    }

    /// <summary>Unlike StartAttackLoop, waits a full interval before the first hit,
    /// so a hero returning from a pause cannot get a free attack.</summary>
    public void ResumeAttackLoop()
    {
        _isAttacking = true;
        _nextAttackTime = Time.time + FinalAttackInterval;
    }

    public void StopAttackLoop()
    {
        _isAttacking = false;
    }

    public void ApplyWeaponBuffs(Enemy target)
    {
        if (target == null) return;
        var buffMgr = Hero.HeroBuff;

        // Iterate only over active status-effect buffs
        foreach (var kvp in buffMgr.ActiveBuffs)
        {
            BuffType type = kvp.Key;
            var data = kvp.Value;

            // Skip if inactive or the probability roll fails
            if (!data.Has.Value || !buffMgr.RollBuff(type))
                continue;

            switch (type)
            {
                case BuffType.Stun:
                    target.Status.ApplyStun(data.Value.Value);
                    break;

                case BuffType.Slow:
                    target.Status.ApplySlow(data.Value.Value);
                    break;

                case BuffType.ReinforcedAttack:
                    // Handled separately in each hero's attack logic
                    break;

                case BuffType.DoubleAttackSpeed:
                    // Applied in FinalAttackInterval
                    break;

                default:
                    Log.w($"[HeroCombat] Unhandled BuffType: {type}");
                    break;
            }
        }
    }

    // Area queries run on every hit of every hero, so the collider buffer is shared and
    // the result list belongs to the hero. Both are overwritten by the next query.
    private static readonly Collider2D[] AreaHitBuffer = new Collider2D[64];
    private readonly List<Enemy> _areaHits = new();

    /// <summary>Living enemies inside a circle. Valid until this hero queries again.</summary>
    protected List<Enemy> FindEnemiesInCircle(Vector2 center, float radius)
    {
        _areaHits.Clear();

        int hitCount = Physics2D.OverlapCircleNonAlloc(
            center, radius, AreaHitBuffer, Layers.Enemy.ToLayerMask());

        for (int i = 0; i < hitCount; i++)
        {
            var enemy = AreaHitBuffer[i].GetComponent<Enemy>();
            if (enemy == null || enemy.Hp.Value <= 0) continue;

            _areaHits.Add(enemy);
        }

        return _areaHits;
    }

    // ---- Targeting and attack logic (must be implemented) ----
    protected abstract void Attack();
    protected List<Enemy> GetTargetsForAttack(int count)
    {
        switch (Hero.TargetingMode)
        {
            case TargetingMode.Nearest:
                return EnemyLocatorService.FindEnemiesOrderedByDistance(transform.position, FinalAttackRange, count);

            case TargetingMode.Random:
                return EnemyLocatorService.FindEnemiesRandomized(transform.position, FinalAttackRange, count);

            default:
                Log.e($"[HeroCombat] Unhandled TargetingMode: {Hero.TargetingMode}. Falling back to nearest.");
                return EnemyLocatorService.FindEnemiesOrderedByDistance(transform.position, FinalAttackRange, count);
        }
    }
}
