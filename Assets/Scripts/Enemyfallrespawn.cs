using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// Failsafe respawn for enemies that fall below a kill plane.
/// - Works with Rigidbody or CharacterController.
/// - Warps NavMeshAgent (if present).
/// - Optionally prefers the last grounded position (via EnemyGroundCheck if present).
/// - No hard references to custom Health/Damageable types.
[DefaultExecutionOrder(-10)]
public class EnemyFallRespawn : MonoBehaviour
{
    [Header("Kill Plane")]
    [Tooltip("If this object's Y position goes below this, it will respawn.")]
    public float killY = -50f;

    [Header("Respawn Target")]
    [Tooltip("Optional explicit spawn point; if null, uses initial position at Awake().")]
    public Transform respawnPoint;

    [Header("Safe Spot Tracking")]
    [Tooltip("If true and EnemyGroundCheck exists, remember last grounded position and prefer it.")]
    public bool useLastGroundedSafeSpot = true;

    [Header("Reset Options")]
    [Tooltip("Zero linear & angular velocity of Rigidbody on respawn.")]
    public bool resetPhysics = true;
    [Tooltip("Warp NavMeshAgent to respawn position if present and enabled.")]
    public bool warpNavMeshAgent = true;

    [Header("Callbacks (no dependencies)")]
    [Tooltip("Invoked after a successful respawn.")]
    public UnityEvent onRespawn;

    [Header("Debug")]
    public bool drawGizmos = true;
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.6f);

    // cached
    Vector3 _initialPos;
    Quaternion _initialRot;
    Vector3 _lastGroundedPos;
    Quaternion _lastGroundedRot;

    Rigidbody _rb;
    CharacterController _cc;
    NavMeshAgent _agent;
    EnemyGroundCheck _groundCheck;

    void Awake()
    {
        _initialPos = transform.position;
        _initialRot = transform.rotation;

        _lastGroundedPos = _initialPos;
        _lastGroundedRot = _initialRot;

        _rb = GetComponent<Rigidbody>();
        _cc = GetComponent<CharacterController>();
        _agent = GetComponent<NavMeshAgent>();
        _groundCheck = GetComponentInChildren<EnemyGroundCheck>() ?? GetComponent<EnemyGroundCheck>();
    }

    void Update()
    {
        // Track a safe place to return to
        if (useLastGroundedSafeSpot && _groundCheck && _groundCheck.IsGrounded)
        {
            _lastGroundedPos = transform.position;
            _lastGroundedRot = transform.rotation;
        }

        // Kill-plane check
        if (transform.position.y < killY)
        {
            RespawnNow();
        }
    }

    /// Public so triggers/kill volumes can call it.
    public void RespawnNow()
    {
        // Choose destination
        Vector3 targetPos = _initialPos;
        Quaternion targetRot = _initialRot;

        if (respawnPoint)
        {
            targetPos = respawnPoint.position;
            targetRot = respawnPoint.rotation;
        }
        else if (useLastGroundedSafeSpot)
        {
            targetPos = _lastGroundedPos;
            targetRot = _lastGroundedRot;
        }

        // small lift to avoid immediate re-penetration
        targetPos += Vector3.up * 0.05f;

        // Teleport safely depending on components present
        if (_cc && _cc.enabled)
        {
            bool prevDetect = _cc.detectCollisions;
            _cc.detectCollisions = false;
            _cc.enabled = false;
            transform.SetPositionAndRotation(targetPos, targetRot);
            _cc.enabled = true;
            _cc.detectCollisions = prevDetect;
        }
        else if (_rb)
        {
            if (resetPhysics)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(targetPos, targetRot);
        }
        else
        {
            transform.SetPositionAndRotation(targetPos, targetRot);
        }

        // NavMesh fix-up
        if (_agent && _agent.enabled && warpNavMeshAgent)
        {
            _agent.Warp(transform.position);
        }

        // Fire loose callbacks without hard dependencies:
        // 1) UnityEvent (assign in Inspector)
        onRespawn?.Invoke();
        // 2) SendMessage hooks (optional methods on your scripts)
        SendMessage("OnRespawned", SendMessageOptions.DontRequireReceiver);
        SendMessage("ResetHealth", SendMessageOptions.DontRequireReceiver);
        // If you have your own Health/Damage system, just implement one of those methods on it.
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Kill plane gizmo (local indicator)
        var c = gizmoColor; c.a = 0.25f;
        Gizmos.color = c;
        Vector3 p = transform.position;
        Gizmos.DrawCube(new Vector3(p.x, killY, p.z), new Vector3(3f, 0.02f, 3f));

        // Respawn point gizmo
        if (respawnPoint)
        {
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(respawnPoint.position + Vector3.up * 0.5f, 0.25f);
        }
    }
}