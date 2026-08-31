using UnityEngine;

/// <summary>
/// Drops an ice strike on the target, damaging an area and applying the hero's
/// slow through the shared weapon-buff path. No projectile travel, so unlike the
/// fire and lightning heroes this resolves entirely within the frame.
/// </summary>
public class IceHeroCombat : HeroCombat
{
    private IceUniqueData _iceData;

    protected override void LoadUniqueData()
    {
        _iceData = ReadUniqueData<IceUniqueData>();
    }

    protected override void Attack()
    {
        var targets = GetTargetsForAttack(1);
        if (targets.Count == 0) return;

        var target = targets[0];
        Hero.SetFacingAndPlayAttackAnim(target);

        // Reinforced widens the strike rather than adding a second one.
        float radius = HasReinforcedAttack
            ? _iceData.StrikeRadius * 1.5f
            : _iceData.StrikeRadius;

        CastIceStrike(target.transform.position, radius);
    }

    private void CastIceStrike(Vector3 targetPos, float radius)
    {
        ApplyAreaDamage(targetPos, radius);

        // Auto-released by the particle manager, so there is nothing to track here.
        ParticleManager.PlayParticle(
            Particles.HeroBullets_EffectPrefab_IceStrike,
            targetPos,
            scale: radius,
            destroyAfter: _iceData.StrikeTime + 2f);
    }

    private void ApplyAreaDamage(Vector3 position, float radius)
    {
        int finalDamage = base.FinalDamage;

        foreach (var enemy in FindEnemiesInCircle(position, radius))
        {
            enemy.TakeDamage(finalDamage, Hero);
            ApplyWeaponBuffs(enemy);
        }
    }
}
