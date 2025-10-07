using UnityEngine;

/// Simple grounded check for enemies/dummies.
/// Attach this to your enemy prefab.
[DefaultExecutionOrder(-50)]
public class EnemyGroundCheck : MonoBehaviour
{
    [Header("Settings")]
    public LayerMask groundMask = ~0;       // set this to Ground layers
    [Tooltip("Extra distance below the collider to probe.")]
    public float probeDistance = 0.08f;
    [Tooltip("Radius used for the spherecast under feet.")]
    public float footRadius = 0.18f;
    [Tooltip("Optional vertical offset from the collider bottom.")]
    public float footOffsetY = 0.02f;

    [Header("Debug")]
    public bool drawGizmos = true;

    public bool IsGrounded { get; private set; }

    private RaycastHit _lastHit;
    public RaycastHit LastHit => _lastHit;

    Collider _col;

    void Awake()
    {
        _col = GetComponentInChildren<Collider>();
        if (!_col) _col = GetComponent<Collider>();
        if (_col == null)
            Debug.LogWarning($"{name}: EnemyGroundCheck found no Collider.");
    }

    void FixedUpdate()
    {
        IsGrounded = ComputeGrounded(out _lastHit);
    }

    bool ComputeGrounded(out RaycastHit hit)
    {
        hit = new RaycastHit();
        if (_col == null) return false;

        Bounds b = _col.bounds;
        Vector3 origin = new Vector3(b.center.x, b.min.y + footOffsetY + 0.01f, b.center.z);
        float dist = probeDistance + 0.02f;

        // Spherecast straight down
        if (Physics.SphereCast(origin, footRadius, Vector3.down, out hit, dist, groundMask, QueryTriggerInteraction.Ignore))
            return true;

        return false;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (_col == null) _col = GetComponentInChildren<Collider>() ?? GetComponent<Collider>();
        if (_col == null) return;

        Bounds b = _col.bounds;
        Vector3 origin = new Vector3(b.center.x, b.min.y + footOffsetY + 0.01f, b.center.z);

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(origin, footRadius);
        Gizmos.DrawLine(origin, origin + Vector3.down * (probeDistance + 0.02f));
    }
}
