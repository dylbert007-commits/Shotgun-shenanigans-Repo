using UnityEngine;
using System.Collections; // for IEnumerator

/// Grapple system with projectile hook:
/// - Press key → shoot hook forward that travels until it hits an enemy
/// - AIRBORNE: pull PLAYER toward ENEMY (zip-in).
/// - GROUNDED: pull ENEMY toward PLAYER along ground plane (uses Rigidbody velocity).
/// - On grounded finish: snap enemy in front and kill momentum.
/// - On cancel (key released): enemy keeps Y (falls with gravity) but horizontal momentum is zeroed.
/// Includes small lift on grounded latch, cooldown, rope, control lock, travel timeout, and retract.
[RequireComponent(typeof(CharacterController))]
public class Grapple : MonoBehaviour
{
    [Header("References")]
    public Camera cam;                      // auto = Camera.main
    public Transform ropeStart;             // left-hand socket
    private PlayerMovement pm;
    private CharacterController cc;

    [Header("Targeting")]
    public KeyCode grappleKey = KeyCode.F;
    public LayerMask grappleMask = ~0;      // include Enemy layer(s)
    public float maxDistance = 40f;
    public float radiusAssist = 0.25f;

    [Header("Hook Projectile")]
    public float hookSpeed = 50f;           // how fast the hook travels
    public GameObject hookPrefab;           // optional visual for the hook
    public float hookWidth = 0.05f;         // LineRenderer width for hook rope
    public float maxTravelTime = 1.5f;      // guard: max seconds hook can travel

    [Header("Pull (player→enemy when airborne)")]
    public float basePullSpeed = 22f;
    public float distanceBoost = 6f;
    public float maxPullSpeed = 48f;
    public float stopDistance = 1.25f;

    [Header("Pull (enemy→player when grounded)")]
    public float enemyPullSpeed = 20f;      // m/s while reeling enemy
    public float enemyStopDistance = 1.6f;  // final distance from player center

    [Header("Finish Behavior")]
    [Tooltip("When a grounded reel finishes, zero enemy velocity and snap to stop distance.")]
    public bool killEnemyMomentumOnFinish = true;

    [Header("Enemy pop on grounded latch")]
    public float enemyLiftHeight = 0.5f;

    [Header("Common Feel")]
    public float maxDuration = 3f;
    public float gravityDuringGrapple = -2.0f;
    public float verticalDamp = 10f;

    [Header("Cooldown")]
    public float cooldown = 2f;
    private float cooldownTimer;

    [Header("Rope (optional)")]
    public LineRenderer rope;
    public float ropeWidth = 0.025f;

    [Header("Debug")]
    public bool showHitGizmo = false;

    // Hook projectile state
    private enum GrappleState { Idle, HookTraveling, Latched }
    private GrappleState state = GrappleState.Idle;
    private Vector3 hookPosition;
    private Vector3 hookDirection;
    private float hookDistanceTraveled;
    private float hookTravelTime;
    private GameObject hookVisual;

    // Latched state
    private Damageable target;
    private Collider targetCol;
    private Transform targetRoot;
    private Vector3 anchorLocal;
    private float tActive;
    private Vector3 vVel;
    private float cachedStepOffset;
    private bool beganGrounded;
    
    [Header("Input System")]
    public bool useNewInputSystem = true;
#if ENABLE_INPUT_SYSTEM
    public UnityEngine.InputSystem.InputActionReference grappleAction;
#endif

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        pm = GetComponent<PlayerMovement>();
        if (!cam) cam = Camera.main;

        if (rope)
        {
            rope.positionCount = 2;
            rope.startWidth = ropeWidth;
            rope.endWidth = ropeWidth;
            rope.enabled = false;
        }
    }

    void Update()
    {
        if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;

        // Fire hook
        if (state == GrappleState.Idle && cooldownTimer <= 0f && GrapplePressedDown())
            FireHook();

        // Hook traveling
        if (state == GrappleState.HookTraveling)
        {
            // Cancel if key released
            if (!GrappleHeld()) { CancelHook(); return; }

            UpdateHookTravel();
        }

        // Latched and pulling
        if (state == GrappleState.Latched)
        {
            // CANCEL: key released → stop enemy horizontal momentum, let them fall
            if (!GrappleHeld()) { FinishGrapple(canceled: true); return; }

            // Target lost?
            if (!target || target.IsDead) { FinishGrapple(canceled: false); return; }

            Vector3 anchorWorld = GetAnchorWorld();
            if (PullLogic(anchorWorld)) { FinishGrapple(canceled: false); return; }
            UpdateRope(anchorWorld);
        }
    }

    // ---------- Hook Projectile ----------
    void FireHook()
    {
        state = GrappleState.HookTraveling;

        Vector3 start = ropeStart ? (ropeStart.position + cam.transform.forward * 0.1f) : cam.transform.position;
        hookPosition = start;
        hookDirection = cam.transform.forward;
        hookDistanceTraveled = 0f;
        hookTravelTime = 0f;

        // Spawn visual if provided
        if (hookPrefab)
        {
            hookVisual = Instantiate(hookPrefab, hookPosition, Quaternion.LookRotation(hookDirection));
        }

        if (rope)
        {
            rope.enabled = true;
            rope.startWidth = hookWidth;
            rope.endWidth = hookWidth;
            rope.SetPosition(0, start);
            rope.SetPosition(1, start);
        }
    }

    void UpdateHookTravel()
    {
        hookTravelTime += Time.deltaTime;
        if (hookTravelTime > maxTravelTime) { StartCoroutine(RetractHookCoroutine()); return; }

        float stepDistance = hookSpeed * Time.deltaTime;
        Vector3 stepStart = hookPosition;
        Vector3 stepEnd = hookPosition + hookDirection * stepDistance;

        // Check for hits along the path (ray first)
        if (Physics.Raycast(stepStart, hookDirection, out RaycastHit hit, stepDistance, grappleMask, QueryTriggerInteraction.Ignore))
        {
            var dmg = hit.collider.GetComponentInParent<Damageable>();
            if (dmg)
            {
                HookHitTarget(dmg, hit.collider, hit.point, hit.normal);
                return;
            }
        }
        // Assist with sphere cast
        else if (radiusAssist > 0f &&
                 Physics.SphereCast(stepStart, radiusAssist, hookDirection, out RaycastHit sphereHit, stepDistance, grappleMask, QueryTriggerInteraction.Ignore))
        {
            var dmg = sphereHit.collider.GetComponentInParent<Damageable>();
            if (dmg)
            {
                HookHitTarget(dmg, sphereHit.collider, sphereHit.point, sphereHit.normal);
                return;
            }
        }

        // Move hook forward
        hookPosition = stepEnd;
        hookDistanceTraveled += stepDistance;

        // Update visual
        if (hookVisual)
            hookVisual.transform.position = hookPosition;

        // Update rope
        if (rope)
        {
            Vector3 start = ropeStart ? (ropeStart.position + cam.transform.forward * 0.1f) : cam.transform.position;
            rope.SetPosition(0, start);
            rope.SetPosition(1, hookPosition);
        }

        // Max distance reached → retract (no cooldown)
        if (hookDistanceTraveled >= maxDistance)
        {
            StartCoroutine(RetractHookCoroutine());
        }
    }

    void HookHitTarget(Damageable dmg, Collider col, Vector3 hitPoint, Vector3 hitNormal)
    {
        target = dmg;
        targetCol = col;
        targetRoot = dmg.transform;
        state = GrappleState.Latched;
        tActive = 0f;
        vVel = Vector3.zero;

        // Stable anchor
        anchorLocal = targetRoot.InverseTransformPoint(hitPoint);

        // Lock controls + remove step snapping
        if (pm) pm.SetControlsLocked(true);
        cachedStepOffset = cc.stepOffset;
        cc.stepOffset = 0f;

        beganGrounded = cc.isGrounded;
        if (beganGrounded) PopEnemyUpOnce();

        // Update rope style for pulling
        if (rope)
        {
            rope.startWidth = ropeWidth;
            rope.endWidth = ropeWidth;
        }

        // Align & clean up hook visual
        if (hookVisual)
        {
            hookVisual.transform.position = hitPoint + hitNormal * 0.005f; // slight push-out
            hookVisual.transform.forward = -hitNormal;                      // face into surface
            Destroy(hookVisual);
            hookVisual = null;
        }
    }

    void CancelHook(bool applyCooldown = true)
    {
        state = GrappleState.Idle;
        if (applyCooldown) cooldownTimer = cooldown;

        if (hookVisual)
        {
            Destroy(hookVisual);
            hookVisual = null;
        }

        if (rope)
            rope.enabled = false;
    }

    IEnumerator RetractHookCoroutine()
    {
        if (!rope)
        {
            CancelHook(applyCooldown: false);
            yield break;
        }

        Vector3 startP = hookPosition;
        Vector3 endP = ropeStart ? ropeStart.position : (cam ? cam.transform.position : transform.position);
        float t = 0f, dur = 0.15f;

        while (t < dur)
        {
            t += Time.deltaTime;
            hookPosition = Vector3.Lerp(startP, endP, t / dur);
            rope.SetPosition(1, hookPosition);

            // Keep rope start fresh if player moves
            Vector3 start = ropeStart ? (ropeStart.position + cam.transform.forward * 0.1f) : cam.transform.position;
            rope.SetPosition(0, start);

            yield return null;
        }

        CancelHook(applyCooldown: false);
    }

    // ---------- Grapple Logic ----------
    void FinishGrapple(bool canceled)
    {
        // If we were pulling an enemy (grounded mode) and the user canceled,
        // stop horizontal motion but allow vertical gravity to continue.
        if (canceled && cc.isGrounded)
            HaltEnemyHorizontal();

        // Restore player control
        state = GrappleState.Idle;

        if (pm) pm.SetControlsLocked(false);
        cc.stepOffset = cachedStepOffset;

        if (rope) rope.enabled = false;
        cooldownTimer = cooldown;

        // Clear refs
        target = null;
        targetCol = null;
        targetRoot = null;
    }

    Vector3 GetAnchorWorld()
    {
        if (!targetRoot) return Vector3.zero;
        return targetRoot.TransformPoint(anchorLocal);
    }

    /// Returns true if finished this frame.
    bool PullLogic(Vector3 anchorWorld)
    {
        tActive += Time.deltaTime;
        if (tActive > maxDuration) return true;

        if (cc.isGrounded)
            return PullEnemyTowardPlayer(anchorWorld);
        else
            return PullPlayerTowardEnemy(anchorWorld);
    }

    bool PullPlayerTowardEnemy(Vector3 anchorWorld)
    {
        Vector3 myCenter = transform.position + cc.center;
        Vector3 to = anchorWorld - myCenter;
        float dist = to.magnitude;

        if (dist <= stopDistance) return true;

        Vector3 dir = (dist > 0.0001f) ? (to / dist) : Vector3.zero;
        float speed = Mathf.Min(maxPullSpeed, basePullSpeed + distanceBoost * dist);

        Vector3 planarDir = new Vector3(dir.x, 0f, dir.z).normalized;
        Vector3 planarMove = planarDir * speed * Time.deltaTime;

        float verticalTarget = dir.y * speed;
        vVel.y = Mathf.MoveTowards(vVel.y, verticalTarget, verticalDamp * Time.deltaTime);
        vVel.y += gravityDuringGrapple * Time.deltaTime;

        cc.Move(planarMove + new Vector3(0f, vVel.y * Time.deltaTime, 0f));
        return false;
    }

    bool PullEnemyTowardPlayer(Vector3 anchorWorld)
    {
        if (!targetRoot) return true;

        Vector3 myCenter = transform.position + cc.center;

        // Ground-plane only (no sinking)
        Vector3 toMeXZ = new Vector3(myCenter.x - anchorWorld.x, 0f, myCenter.z - anchorWorld.z);
        float distXZ = toMeXZ.magnitude;

        // Finished this frame: snap & kill momentum (optional)
        if (distXZ <= enemyStopDistance)
        {
            if (killEnemyMomentumOnFinish) SnapEnemyInFront(myCenter);
            return true;
        }

        Vector3 dirXZ = (distXZ > 0.0001f) ? (toMeXZ / distXZ) : Vector3.zero;

        Rigidbody rb = targetRoot.GetComponent<Rigidbody>();
        if (rb && !rb.isKinematic)
        {
            // Drive velocity toward player (XZ only). Keep Y for gravity/pop
            Vector3 vel = dirXZ.normalized * enemyPullSpeed;
            vel.y = rb.linearVelocity.y;
            rb.linearVelocity = vel;
        }
        else
        {
            targetRoot.position += dirXZ * enemyPullSpeed * Time.deltaTime;
        }

        return false;
    }

    void HaltEnemyHorizontal()
    {
        if (!targetRoot) return;
        Rigidbody rb = targetRoot.GetComponent<Rigidbody>();
        if (rb && !rb.isKinematic)
        {
            // Zero horizontal momentum; keep vertical so gravity continues naturally
            rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            rb.angularVelocity = Vector3.zero;
        }
    }

    void SnapEnemyInFront(Vector3 playerCenter)
    {
        if (!targetRoot) return;

        Vector3 toPlayer = new Vector3(playerCenter.x - targetRoot.position.x, 0f, playerCenter.z - targetRoot.position.z);
        Vector3 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward;

        Vector3 desired = playerCenter - dir * enemyStopDistance;
        desired.y = targetRoot.position.y; // Keep current height

        Rigidbody rb = targetRoot.GetComponent<Rigidbody>();
        if (rb && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.MovePosition(desired);
        }
        else
        {
            targetRoot.position = desired;
        }
    }

    void PopEnemyUpOnce()
    {
        if (!targetRoot) return;

        Rigidbody rb = targetRoot.GetComponent<Rigidbody>();
        if (rb && !rb.isKinematic)
        {
            float g = Mathf.Abs(Physics.gravity.y);
            float vUp = Mathf.Sqrt(Mathf.Max(0f, 2f * g * enemyLiftHeight));
            Vector3 vel = rb.linearVelocity;
            if (vel.y < vUp) vel.y = vUp;
            rb.linearVelocity = vel;
        }
        else
        {
            targetRoot.position += Vector3.up * enemyLiftHeight;
        }
    }

    void UpdateRope(Vector3 anchorWorld)
    {
        if (!rope) return;
        Vector3 start = ropeStart ? (ropeStart.position + cam.transform.forward * 0.1f) : (cam ? cam.transform.position : transform.position);
        rope.SetPosition(0, start);
        rope.SetPosition(1, anchorWorld);
    }

    void OnDrawGizmosSelected()
    {
        if (!showHitGizmo) return;

        if (state == GrappleState.HookTraveling)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(hookPosition, 0.08f);
        }
        else if (state == GrappleState.Latched)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(GetAnchorWorld(), 0.08f);
        }
    }

    void OnEnable()
    {
#if ENABLE_INPUT_SYSTEM
        if (useNewInputSystem && grappleAction && grappleAction.action != null)
        {
            try { if (!grappleAction.action.enabled) grappleAction.action.Enable(); } catch {}
        }
#endif
    }

    void OnDisable()
    {
#if ENABLE_INPUT_SYSTEM
        if (grappleAction && grappleAction.action != null)
        {
            try { grappleAction.action.Disable(); } catch {}
        }
#endif
    }

    // --------- Input Helpers ---------
    bool GrappleHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (useNewInputSystem && grappleAction && grappleAction.action != null)
        {
            return grappleAction.action.IsPressed();
        }
#endif
        return Input.GetKey(grappleKey);
    }

    bool GrapplePressedDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (useNewInputSystem && grappleAction && grappleAction.action != null)
        {
            return grappleAction.action.WasPressedThisFrame();
        }
#endif
        return Input.GetKeyDown(grappleKey);
    }
}
