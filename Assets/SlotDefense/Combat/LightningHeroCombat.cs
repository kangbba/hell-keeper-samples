using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fires a homing bolt that chains between enemies on impact. The chain itself
/// lives in LightningBullet, so this class only picks targets and hands off.
/// </summary>
public class LightningHeroCombat : HeroCombat
{
    private LightningUniqueData _lightningData;

    protected override void LoadUniqueData()
    {
        _lightningData = ReadUniqueData<LightningUniqueData>();
    }

    protected override void Attack()
    {
        if (this.IsDestroyed() || Hero.IsDestroyed()) return;

        // Reinforced doubles the bolt count; the chain length is unchanged.
        List<Enemy> targets = GetTargetsForAttack(HasReinforcedAttack ? 2 : 1);
        if (targets.Count == 0) return;

        Hero.SetFacingAndPlayAttackAnim(targets[0]);

        foreach (var target in targets)
        {
            ShootLightningBullet(target);
        }
    }

    private void ShootLightningBullet(Enemy target)
    {
        if (target.IsDestroyed() || target.Hp.Value <= 0) return;

        var bulletObj = ParticleManager.PlayParticle(
            Particles.HeroBullets_EffectPrefab_LightningBullet,
            Hero.transform.position,
            scale: 1f,
            destroyAfter: null); // LightningBullet destroys it once the chain ends.

        if (bulletObj == null)
        {
            Log.w("[LightningHeroCombat] LightningBullet prefab not found.");
            return;
        }

        var lightningBullet = bulletObj.GetComponent<LightningBullet>();
        if (lightningBullet == null)
        {
            // Nothing else will clean it up: the bullet destroys itself once its chain
            // ends, and without the component there is no chain.
            Log.e("[LightningHeroCombat] LightningBullet component missing on the prefab.");
            Destroy(bulletObj);
            return;
        }

        lightningBullet.Init(
            owner: Hero,
            initialTarget: target,
            lightningData: _lightningData,
            damage: base.FinalDamage,
            onApplyWeaponBuffs: ApplyWeaponBuffs
        );
    }
}
