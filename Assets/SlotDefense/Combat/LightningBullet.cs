using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>
/// Homing bolt for the lightning hero. Tracks its first target, then arcs from
/// enemy to enemy on impact.
///
/// Every await is bound to the bullet's destroy token, and the chain effect is
/// released in a finally, so a bullet destroyed mid-flight cannot leave a particle
/// behind. Each hop re-checks the owner, so a hero sold or merged part-way through
/// a chain stops dealing damage while the visual still resolves.
/// </summary>
public class LightningBullet : MonoBehaviour
{
    private const float BulletSpeed = 40f;
    private const float ChainFadeSeconds = 1f;

    private static readonly Collider2D[] ChainHitBuffer = new Collider2D[32];

    private Hero _owner;
    private Enemy _initialTarget;
    private LightningUniqueData _lightningData;
    private int _damage;
    private Action<Enemy> _onApplyWeaponBuffs;

    public void Init(
        Hero owner,
        Enemy initialTarget,
        LightningUniqueData lightningData,
        int damage,
        Action<Enemy> onApplyWeaponBuffs)
    {
        _owner = owner;
        _initialTarget = initialTarget;
        _lightningData = lightningData;
        _damage = damage;
        _onApplyWeaponBuffs = onApplyWeaponBuffs;

        RunAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid RunAsync(CancellationToken token)
    {
        bool reachedTarget = await TrackTargetAsync(token);

        if (reachedTarget)
        {
            await CastLightningChainAsync(_initialTarget, token);
        }

        // The bullet owns its own lifetime: it dies once its chain is finished,
        // rather than on a timer that can outlive or undercut the chain.
        if (this != null)
        {
            Destroy(gameObject);
        }
    }

    private async UniTask<bool> TrackTargetAsync(CancellationToken token)
    {
        while (!_initialTarget.IsDestroyed() && _initialTarget.Hp.Value > 0)
        {
            Vector3 dir = _initialTarget.transform.position - transform.position;
            float distanceThisFrame = BulletSpeed * Time.deltaTime;

            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, angle);

            if (dir.sqrMagnitude <= distanceThisFrame * distanceThisFrame)
            {
                transform.position = _initialTarget.transform.position;
                return true;
            }

            transform.position += dir.normalized * distanceThisFrame;
            await UniTask.Yield(token);
        }

        return false;
    }

    private async UniTask CastLightningChainAsync(Enemy start, CancellationToken token)
    {
        if (start.IsDestroyed() || start.Hp.Value <= 0) return;

        GameObject fx = ParticleManager.PlayParticle(
            Particles.HeroBullets_EffectPrefab_LightningChain,
            start.transform.position,
            scale: 1f,
            destroyAfter: null);

        try
        {
            var alreadyHit = new HashSet<Enemy>();
            Enemy current = start;

            for (int i = 0; i < _lightningData.MaxChains; i++)
            {
                if (current.IsDestroyed() || current.Hp.Value <= 0) break;

                if (!_owner.IsDestroyed())
                {
                    current.TakeDamage(_damage, _owner);
                    _onApplyWeaponBuffs?.Invoke(current);
                }

                alreadyHit.Add(current);

                Enemy next = FindNextTarget(current, alreadyHit);
                if (next == null || next.IsDestroyed()) break;

                Vector3 from = current.transform.position;
                Vector3 to = next.transform.position;

                // The hop always takes JumpDuration, whether or not there is an effect to
                // move: how fast the chain damages is gameplay, and the particle is not.
                if (fx != null)
                {
                    Vector3 dir = (to - from).normalized;
                    fx.transform.rotation =
                        Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

                    await MoveOverTimeAsync(fx.transform, from, to, _lightningData.JumpDuration, token);
                }
                else
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(_lightningData.JumpDuration),
                        cancellationToken: token);
                }

                current = next;
            }

            await UniTask.Delay(TimeSpan.FromSeconds(ChainFadeSeconds), cancellationToken: token);
        }
        finally
        {
            // Runs on cancellation too, so the manually managed effect is never orphaned.
            if (fx != null)
            {
                Destroy(fx);
            }
        }
    }

    private Enemy FindNextTarget(Enemy from, HashSet<Enemy> exclude)
    {
        if (from.IsDestroyed()) return null;

        Vector2 origin = from.transform.position;
        int hitCount = Physics2D.OverlapCircleNonAlloc(
            origin, _lightningData.StrikeRadius, ChainHitBuffer, Layers.Enemy.ToLayerMask());

        float best = float.MaxValue;
        Enemy closest = null;

        for (int i = 0; i < hitCount; i++)
        {
            var enemy = ChainHitBuffer[i].GetComponent<Enemy>();
            if (enemy == null || enemy.IsDestroyed() || enemy.Hp.Value <= 0) continue;
            if (exclude.Contains(enemy)) continue;

            float distance = ((Vector2)enemy.transform.position - origin).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                closest = enemy;
            }
        }

        return closest;
    }

    private async UniTask MoveOverTimeAsync(
        Transform tr, Vector3 from, Vector3 to, float duration, CancellationToken token)
    {
        if (duration <= 0f)
        {
            tr.position = to;
            return;
        }

        float t = 0f;
        while (t < 1f)
        {
            if (tr == null) return;

            t += Time.deltaTime / duration;
            tr.position = Vector3.Lerp(from, to, t);
            await UniTask.Yield(token);
        }
    }
}
