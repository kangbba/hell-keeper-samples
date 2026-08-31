using UnityEngine;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using System;
using System.Threading;

/// <summary>
/// Lobs an arcing bomb that damages and knocks back everything in its blast radius.
/// The bomb effect is created with manual lifetime and released in a finally, so a
/// hero destroyed mid-flight cannot leave it behind.
/// </summary>
public class FireHeroCombat : HeroCombat
{
    private FireUniqueData _fireData;

    protected override void LoadUniqueData()
    {
        _fireData = ReadUniqueData<FireUniqueData>();
    }

    protected override void Attack()
    {
        var targets = GetTargetsForAttack(1);
        if (targets.Count == 0) return;

        var target = targets[0];
        Hero.SetFacingAndPlayAttackAnim(target);

        float radius = HasReinforcedAttack
            ? _fireData.ExplosionRadius * 1.5f
            : _fireData.ExplosionRadius;

        FireBombToPosition(target.transform.position, radius, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTask FireBombToPosition(Vector3 targetPos, float explosionRadius, CancellationToken token)
    {
        Vector3 startPos = transform.position;

        GameObject bomb = ParticleManager.PlayParticle(
            Particles.HeroBullets_EffectPrefab_FireBomb,
            startPos,
            scale: 1f,
            destroyAfter: null);

        try
        {
            if (bomb != null)
            {
                Vector3 midPoint = CalculateArcMidPoint(startPos, targetPos);
                Vector3[] path = { startPos, midPoint, targetPos };

                bomb.transform
                    .DOPath(path, _fireData.TravelTime, PathType.CatmullRom)
                    .SetEase(Ease.InQuad)
                    .SetLink(bomb);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(_fireData.TravelTime), cancellationToken: token);
        }
        finally
        {
            if (bomb != null)
            {
                Destroy(bomb);
            }
        }

        Explode(targetPos, explosionRadius);
    }

    private void Explode(Vector3 targetPos, float explosionRadius)
    {
        ParticleManager.PlayParticle(
            Particles.HeroBullets_EffectPrefab_FireExplosive,
            targetPos,
            scale: explosionRadius,
            destroyAfter: _fireData.ExplosiveTime);

        int finalDamage = base.FinalDamage;

        foreach (var enemy in FindEnemiesInCircle(targetPos, explosionRadius))
        {
            enemy.TakeDamage(finalDamage, Hero);
            ApplyWeaponBuffs(enemy);

            // Knockback falls off with distance from the centre of the blast. Measured
            // against the radius that was actually used, which reinforced attacks widen.
            float distance = Vector2.Distance(enemy.transform.position, targetPos);
            float distanceFactor = Mathf.Clamp01(1f - (distance / explosionRadius));

            Vector2 direction = (enemy.transform.position - targetPos).normalized;
            float knockDistance = Mathf.Lerp(0.3f, 0.6f, distanceFactor);
            float knockDuration = Mathf.Lerp(0.35f, 0.6f, distanceFactor);

            enemy.ApplyKnockback(direction, knockDistance, knockDuration);
        }
    }

    private Vector3 CalculateArcMidPoint(Vector3 start, Vector3 target)
    {
        Vector3 midPoint = (start + target) * 0.5f;
        midPoint.y += _fireData.ArcHeight;
        return midPoint;
    }
}
