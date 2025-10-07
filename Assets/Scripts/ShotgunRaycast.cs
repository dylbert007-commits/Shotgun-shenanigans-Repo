using UnityEngine;
using System.Collections.Generic;

public class ShotgunRaycast : MonoBehaviour
{
    [Header("References")]
    public Transform firePoint;
    public GameObject muzzleFlashPrefab;
    public GameObject tracerPrefab;
    public GameObject impactDecalPrefab;

    [Header("Ballistics")]
    public int pelletCount = 7;
    public float spreadAngle = 4f;          // degrees
    public float range = 60f;
    public float damagePerPellet = 10f;

    [Header("Damage Falloff")]
    public bool enableFalloff = true;
    public float falloffStart = 8f;         // full dmg until here
    public float falloffEnd = 25f;          // min dmg at/after here
    [Range(0f, 1f)] public float minDamagePercent = 0.3f;

    [Header("Crits")]
    public float critChance = 0.05f;
    public float critMultiplier = 2f;

    [Header("Fire Control")]
    public float fireRate = 0.9f;
    public bool isAutomatic = false;  // hold to fire vs tap to fire
    private float nextFireTime;

    [Header("Layers")]
    public LayerMask hitMask;            // what the ray can hit
    public LayerMask damageableMask;     // optional: which ROOT layers count as damageable

    [Header("FX Lifetimes")]
    public float muzzleFlashLife = 0.08f;
    public float tracerLife = 0.35f;
    public float decalLife = 12f;

    [Header("Decal Settings")]
    [Range(0.02f, 1f)] public float decalSize = 0.18f;
    [Range(0.0005f, 0.02f)] public float decalPushOut = 0.004f;
    public bool parentDecalToHit = true;
    public bool spawnDecalsOnEnemies = true;
    public bool parentDecalsToCharacters = false;  // can cause stretching/skewing

    [Header("Debug / Visualization")]
    public bool showDebugRays = false;
    public float debugRayDuration = 2f;
    public float tracerWidth = 0.02f;

    [Header("Anti Self-Hit")]
    [Range(0f, 0.5f)] public float selfHitForwardBias = 0.15f;

    [Header("Knockback (airborne only, via receiver)")]
    public float airborneKnockback = 6f;  // impulse per pellet
    public float knockbackUpBias = 0.2f;
    [Range(0f, 100f)] public float maxTotalKnockback = 30f;  // clamp to prevent excessive launch

    private struct DamageData
    {
        public Damageable target;
        public float totalDamage;
        public Vector3 posAccum;
        public Vector3 normalAccum;
        public Vector3 impulseAccum;
        public int hitCount;
        public bool anyCrit;
    }

    // Reusable dictionary to reduce GC allocations (pre-sized for larger crowds)
    private Dictionary<Damageable, DamageData> damageAggregation = new Dictionary<Damageable, DamageData>(16);

    private void Awake()
    {
        if (hitMask == 0)
            hitMask = ~LayerMask.GetMask("Player", "Ignore Raycast");
        if (falloffEnd <= 0f) falloffEnd = range;

        // Guardrail: ensure falloff range is valid
        if (falloffStart > falloffEnd)
        {
            Debug.LogWarning($"[ShotgunRaycast] falloffStart ({falloffStart}) > falloffEnd ({falloffEnd}). Swapping values.");
            float temp = falloffStart;
            falloffStart = falloffEnd;
            falloffEnd = temp;
        }

        minDamagePercent = Mathf.Clamp01(minDamagePercent);
    }

    private void Update()
    {
        bool fireInput = isAutomatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (fireInput) TryFire();
    }

    private void TryFire()
    {
        if (Time.time < nextFireTime) return;
        nextFireTime = Time.time + fireRate;

        SpawnMuzzleFlash();
        FirePelletsAggregated();
    }

    private void FirePelletsAggregated()
    {
        damageAggregation.Clear();

        for (int i = 0; i < pelletCount; i++)
        {
            // Uniform cone spread
            Vector3 start = firePoint.position + firePoint.forward * selfHitForwardBias;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
            Vector3 dir = (firePoint.forward + firePoint.right * r.x + firePoint.up * r.y).normalized;

            if (Physics.Raycast(start, dir, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
            {
                // Measure distance from the biased start point for accurate falloff
                float dist = Vector3.Distance(start, hit.point);

                // ----- damage calc (falloff + hitbox + crit) -----
                float finalDamage = damagePerPellet * DamageFalloff(dist);
                bool crit = false;

                if (hit.collider.TryGetComponent(out Hitbox hitbox))
                {
                    finalDamage *= hitbox.damageMultiplier;
                    if (hitbox.countsAsCrit) crit = true;
                }
                if (Random.value < critChance)
                {
                    finalDamage *= critMultiplier;
                    crit = true;
                }

                // ----- check if this target can be damaged -----
                bool rootLayerAllowed = true;
                if (damageableMask.value != 0)
                {
                    int rootLayer = hit.transform.root.gameObject.layer;
                    rootLayerAllowed = (damageableMask.value & (1 << rootLayer)) != 0;
                }

                // ----- aggregate damage for batch application -----
                bool willDamage = false;
                if (hit.collider.TryGetComponent(out Damageable dmg))
                {
                    willDamage = rootLayerAllowed;
                }
                else
                {
                    dmg = hit.collider.GetComponentInParent<Damageable>();
                    willDamage = (dmg != null && rootLayerAllowed);
                }

                if (willDamage)
                {
                    // Accumulate damage, position, and knockback impulse
                    if (!damageAggregation.TryGetValue(dmg, out DamageData data))
                    {
                        data = new DamageData { target = dmg };
                    }

                    data.totalDamage += finalDamage;
                    data.posAccum += hit.point;
                    data.normalAccum += hit.normal;
                    data.hitCount += 1;
                    data.anyCrit = data.anyCrit || crit;

                    // Calculate knockback impulse for this pellet (use biased start for consistency)
                    Vector3 dirToHit = (hit.point - start).normalized;
                    dirToHit.y += knockbackUpBias;
                    data.impulseAccum += dirToHit.normalized * airborneKnockback;

                    damageAggregation[dmg] = data;
                }

                // ----- spawn decals for all hits (world and enemies) -----
                if (impactDecalPrefab != null)
                {
                    if (!willDamage || spawnDecalsOnEnemies)
                    {
                        SpawnImpactDecal(hit);
                    }
                }

                if (showDebugRays) Debug.DrawLine(start, hit.point, Color.red, debugRayDuration);
                SpawnTracer(start, hit.point);
            }
            else
            {
                Vector3 end = start + dir * range;
                if (showDebugRays) Debug.DrawLine(start, end, Color.red, debugRayDuration);
                SpawnTracer(start, end);
            }
        }

        // Apply all accumulated damage in a single batch per target
        foreach (var kvp in damageAggregation)
        {
            DamageData data = kvp.Value;
            if (data.hitCount <= 0) continue;

            // Apply total aggregated damage once
            data.target.ApplyDamage(data.totalDamage);

            // Apply total aggregated knockback once (from root/parent to ensure we find the receiver)
            EnemyKnockbackReceiver receiver = data.target.GetComponentInParent<EnemyKnockbackReceiver>();
            if (receiver != null)
            {
                // Clamp total knockback to prevent excessive launch at close range
                Vector3 finalImpulse = data.impulseAccum;
                if (maxTotalKnockback > 0f && finalImpulse.magnitude > maxTotalKnockback)
                {
                    finalImpulse = finalImpulse.normalized * maxTotalKnockback;
                }
                receiver.ApplyImpulse(finalImpulse);
            }

            // Spawn one damage number at average hit position, offset by average normal
            Vector3 avgPos = data.posAccum / data.hitCount;
            Vector3 avgNormal = (data.normalAccum / data.hitCount).normalized;
            Vector3 spawnPos = avgPos + avgNormal * 0.1f;  // offset to avoid clipping
            DamageNumberSystem.Spawn(spawnPos, data.totalDamage, data.anyCrit);
        }
    }

    // ---------- helpers ----------

    private float DamageFalloff(float d)
    {
        if (!enableFalloff) return 1f;
        if (d <= falloffStart) return 1f;
        if (d >= falloffEnd) return minDamagePercent;
        float t = Mathf.InverseLerp(falloffStart, falloffEnd, d);
        return Mathf.Lerp(1f, minDamagePercent, t);
    }

    private void SpawnMuzzleFlash()
    {
        if (muzzleFlashPrefab == null || firePoint == null) return;
        GameObject fx = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation, firePoint);
        Destroy(fx, muzzleFlashLife);
    }

    private void SpawnTracer(Vector3 start, Vector3 end)
    {
        if (tracerPrefab == null) return;
        GameObject go = Instantiate(tracerPrefab);
        LineRenderer lr = go.GetComponent<LineRenderer>();
        if (lr != null)
        {
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            lr.startWidth = tracerWidth;
            lr.endWidth = tracerWidth;
        }
        Destroy(go, tracerLife);
    }

    private void SpawnImpactDecal(RaycastHit hit)
    {
        if (impactDecalPrefab == null) return;

        Vector3 pos = hit.point + hit.normal * decalPushOut;
        Quaternion rot = Quaternion.LookRotation(-hit.normal) *
                         Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        GameObject decal = Instantiate(impactDecalPrefab, pos, rot);
        decal.transform.localScale = Vector3.one * decalSize;

        if (parentDecalToHit && hit.collider != null)
        {
            // Only parent to static world objects to avoid stretching on animated characters
            bool isCharacter = hit.collider.GetComponentInParent<Damageable>() != null;
            if (!isCharacter || parentDecalsToCharacters)
            {
                decal.transform.SetParent(hit.collider.transform, true);
            }
        }

        Destroy(decal, decalLife);
    }
}
