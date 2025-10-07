using System.Collections;
using UnityEngine;

/// Centralized knockback handling that makes enemies feel solid:
/// - Contact-based grounded detection (layer filtered) + grace window.
/// - Rejects impulses while grounded.
/// - Sticky brake to kill slide after airborne hits.
[RequireComponent(typeof(Rigidbody))]
public class EnemyKnockbackReceiver : MonoBehaviour
{
    [Header("References (optional)")]
    public EnemyGroundCheck groundCheck;              // optional; contact check is primary

    [Header("Ground Detection")]
    [Tooltip("Layers considered 'ground'. Ensure this matches your floor layers.")]
    public LayerMask groundLayers = ~0;
    [Tooltip("Keep treating as grounded for this long after last ground contact (sec).")]
    public float groundedGrace = 0.08f;

    [Header("Impulse Policy")]
    [Tooltip("Ignore ALL impulses while effectively grounded (recommended).")]
    public bool rejectGroundedImpulses = true;
    [Tooltip("If an impulse sneaks in while grounded, snap out small horizontal drift (m/s).")]
    public float groundedSnapThreshold = 0.1f;

    [Header("Speed Caps")]
    public float maxHorizontalSpeedAir = 10f;

    [Header("Sticky Brake (after airborne hit, on land)")]
    public bool useStickyBrake = true;
    public float stickyBrakeDuration = 0.25f;
    public float stickyBrakeStrength = 40f;

    [Header("Debug")]
    public bool showDebug = false;

    Rigidbody _rb;
    Coroutine _brakeCo;

    // grounded state
    float _lastContactTime;
    bool _airborneHitPending;

    // scratch buffer to avoid GC
    readonly Collider[] _overlapScratch = new Collider[8];

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (!groundCheck) groundCheck = GetComponentInChildren<EnemyGroundCheck>() ?? GetComponent<EnemyGroundCheck>();
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    void FixedUpdate()
    {
        bool contactGrounded = ContactGrounded();

        // also trust EnemyGroundCheck if provided
        bool gcGrounded = groundCheck ? groundCheck.IsGrounded : false;
        if (contactGrounded || gcGrounded)
            _lastContactTime = Time.time;

        // if we just landed after an airborne hit, apply sticky brake + snap tiny drift
        if (_airborneHitPending && IsEffectivelyGrounded())
        {
            _airborneHitPending = false;

            if (useStickyBrake)
            {
                if (_brakeCo != null) StopCoroutine(_brakeCo);
                _brakeCo = StartCoroutine(StickyBrake());
            }

            Vector3 v = _rb.linearVelocity;
            Vector3 vH = new Vector3(v.x, 0f, v.z);
            if (vH.magnitude < groundedSnapThreshold)
            {
                v.x = 0f; v.z = 0f;
                _rb.linearVelocity = v;
            }
        }
    }

    /// Apply an impulse (world space) from weapons.
    public void ApplyImpulse(Vector3 impulse)
    {
        if (_rb.isKinematic) return;

        if (rejectGroundedImpulses && IsEffectivelyGrounded())
        {
            if (showDebug) Debug.DrawRay(transform.position, impulse * 0.05f, Color.gray, 0.25f, false);
            return; // hard block while grounded
        }

        _airborneHitPending = true;

        // J = m * Δv
        Vector3 dv = impulse / Mathf.Max(0.0001f, _rb.mass);
        Vector3 v = _rb.linearVelocity + dv;

        // clamp horizontal while airborne
        Vector3 vH = new Vector3(v.x, 0f, v.z);
        float cap = Mathf.Max(0.1f, maxHorizontalSpeedAir);
        if (vH.magnitude > cap)
        {
            vH = vH.normalized * cap;
            v.x = vH.x; v.z = vH.z;
        }

        _rb.linearVelocity = v;

        if (showDebug) Debug.DrawRay(transform.position, impulse * 0.05f, Color.magenta, 0.25f, false);
    }

    IEnumerator StickyBrake()
    {
        float t = 0f;
        while (t < stickyBrakeDuration)
        {
            t += Time.fixedDeltaTime;
            if (!IsEffectivelyGrounded()) yield break;

            Vector3 v = _rb.linearVelocity;
            Vector3 vH = new Vector3(v.x, 0f, v.z);

            float decel = stickyBrakeStrength * Time.fixedDeltaTime;
            float mag = vH.magnitude;
            float newMag = Mathf.Max(0f, mag - decel);

            vH = (mag > 0.0001f) ? vH * (newMag / mag) : Vector3.zero;
            v.x = vH.x; v.z = vH.z;
            _rb.linearVelocity = v;

            yield return new WaitForFixedUpdate();
        }
    }

    bool IsEffectivelyGrounded()
    {
        if ((Time.time - _lastContactTime) <= groundedGrace) return true;
        if (groundCheck && groundCheck.IsGrounded) return true;
        return false;
    }

    // Contact-based grounded detection using an overlap near the feet.
    bool ContactGrounded()
    {
        Collider col = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
        Vector3 center;
        float radius;
        float height;

        if (col != null)
        {
            Bounds b = col.bounds;
            center = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            radius = Mathf.Min(b.extents.x, b.extents.z) * 0.45f;
            height = 0.02f;
        }
        else
        {
            center = transform.position + Vector3.up * 0.1f;
            radius = 0.2f;
            height = 0.02f;
        }

        Vector3 p1 = center;
        Vector3 p2 = center + Vector3.up * height;

        int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, _overlapScratch, groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = _overlapScratch[i];
            if (c == null) continue;
            if (c.attachedRigidbody == _rb) continue;
            return true;
        }
        return false;
    }
}
