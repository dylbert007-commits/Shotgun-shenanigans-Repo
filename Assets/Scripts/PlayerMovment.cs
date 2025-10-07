using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float jumpHeight = 2.0f;
    public float gravity = -28f;                 // constant gravity

    [Header("Mouse Look")]
    public Transform camHolder;                  // Camholder or Main Camera
    [Range(1f, 1000f)] public float mouseSensitivity = 300f;
    public bool lockCursor = true;

    [Header("Sprint")]
    public KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Abilities (unlock via pickups)")]
    public int maxAirJumps = 0;                  // set to 1 by pickup
    public bool canDash = false;                 // set true by pickup

    [Header("Dash")]
    public KeyCode dashKey = KeyCode.LeftControl;
    public float dashForce = 15f;
    public float dashDamp = 8f;
    public float dashCooldown = 1f;

    [Header("Grounding")]
    public Transform groundCheck;                // child at feet (auto-created)
    public float groundCheckRadius = 0.24f;
    public LayerMask groundMask;                 // ONLY floor layers
    public float groundStick = 2.0f;             // small push-down when grounded
    public float postJumpGroundLock = 0.06f;     // ignore ground briefly after jump

    [Header("Jump QoL")]
    public float coyoteTime = 0.12f;             // grace after leaving ground
    public float jumpBuffer = 0.12f;             // remember jump slightly early

    [Header("Limits / Respawn")]
    public float maxFallSpeed = -30f;            // terminal velocity clamp
    public float killY = -100f;                  // kill plane height
    public Vector3 respawnPoint = new Vector3(0, 3, 0);

    [Header("External Control")]
    [Tooltip("If true, disables movement/jump/dash while still allowing Look().")]
    public bool controlsLocked = false;

    // Internals
    CharacterController controller;
    Vector3 verticalVel;                         // y only
    Vector3 dashVel;                             // horizontal impulse
    float dashCooldownTimer;
    float xRotation;

    // timers/state
    float coyoteTimer;
    float jumpBufferTimer;
    float postJumpTimer;
    int airJumpsUsed = 0;
    float defaultStepOffset;
    bool isRespawning = false;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Start()
    {
        if (lockCursor) Cursor.lockState = CursorLockMode.Locked;

        if (!camHolder)
        {
            if (Camera.main && Camera.main.transform.parent) camHolder = Camera.main.transform.parent;
            else if (Camera.main) camHolder = Camera.main.transform;
        }

        if (!groundCheck)
        {
            var gc = new GameObject("GroundCheck");
            gc.transform.SetParent(transform);
            gc.transform.localPosition = new Vector3(0f, -(controller.height * 0.5f) + controller.radius + 0.02f, 0f);
            groundCheck = gc.transform;
        }

        defaultStepOffset = controller.stepOffset;
    }

    void Update()
    {
        // Always allow look
        Look();

        // Skip movement while respawning or if CC/GameObject is inactive
        if (isRespawning || controller == null || !controller.enabled || !gameObject.activeInHierarchy)
            return;

        // Kill-plane: start respawn and stop this frame immediately
        if (transform.position.y < killY)
        {
            StartCoroutine(RespawnRoutine(respawnPoint));
            return;
        }

        // If locked (e.g., grappling), do NOT read inputs or move
        if (controlsLocked) return;

        ReadInputs();
        MoveCharacter();
    }

    // --------- Public control from external systems (e.g., Grapple) ----------
    public void SetControlsLocked(bool locked)
    {
        controlsLocked = locked;
        if (locked)
        {
            // Stop all active motion so Grapple (or other system) fully owns movement
            verticalVel = Vector3.zero;
            dashVel = Vector3.zero;
            // Clear jump buffers/timers to avoid popping right after unlock
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            postJumpTimer = 0f;
        }
    }

    // ---------- Look ----------
    void Look()
    {
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        transform.Rotate(Vector3.up * mx);

        if (camHolder)
        {
            xRotation -= my;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);
            camHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }
    }

    // ---------- Inputs ----------
    void ReadInputs()
    {
        if (Input.GetButtonDown("Jump")) jumpBufferTimer = jumpBuffer;
        else jumpBufferTimer -= Time.deltaTime;

        // Dash in movement-key direction (fallback to forward)
        dashCooldownTimer -= Time.deltaTime;
        if (canDash && Input.GetKeyDown(dashKey) && dashCooldownTimer <= 0f)
        {
            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");
            Vector3 dir = transform.right * x + transform.forward * z;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.y = 0f;
            dir.Normalize();

            dashVel = dir * dashForce;
            dashCooldownTimer = dashCooldown;
        }
    }

    // ---------- Movement / Jumps / Gravity / Dash ----------
    void MoveCharacter()
    {
        if (controller == null || !controller.enabled || !gameObject.activeInHierarchy) return;

        float dt = Time.deltaTime;

        // Immediate horizontal
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        float speed = Input.GetKey(sprintKey) ? sprintSpeed : walkSpeed;

        Vector3 moveDir = transform.right * x + transform.forward * z;
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        Vector3 horizontalVel = moveDir * speed;

        // Ground probe
        postJumpTimer -= dt;
        bool grounded = Physics.CheckSphere(
            groundCheck.position, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore
        );
        if (postJumpTimer > 0f) grounded = false;
        bool stableGrounded = grounded && verticalVel.y <= 0.05f;

        controller.stepOffset = stableGrounded ? defaultStepOffset : 0f;

        if (stableGrounded)
        {
            coyoteTimer = coyoteTime;
            airJumpsUsed = 0;
            if (verticalVel.y < 0f && postJumpTimer <= 0f)
                verticalVel.y = -groundStick;
        }
        else
        {
            coyoteTimer -= dt;
        }

        // Jumps (press OR buffered)
        bool wantJumpNow = Input.GetButtonDown("Jump");
        bool wantJumpBuffered = (jumpBufferTimer > 0f);

        if ((wantJumpNow || wantJumpBuffered) && coyoteTimer > 0f)
        {
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            verticalVel.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            postJumpTimer = postJumpGroundLock;
        }
        else if ((wantJumpNow || wantJumpBuffered) && !stableGrounded && airJumpsUsed < maxAirJumps)
        {
            jumpBufferTimer = 0f;
            airJumpsUsed++;
            verticalVel.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            postJumpTimer = postJumpGroundLock;
        }

        // Gravity + clamp
        verticalVel.y += gravity * dt;
        if (verticalVel.y < maxFallSpeed) verticalVel.y = maxFallSpeed;

        // Dash decay
        if (dashVel.sqrMagnitude > 0.01f)
            dashVel = Vector3.Lerp(dashVel, Vector3.zero, dashDamp * dt);
        else
            dashVel = Vector3.zero;

        // Single Move
        Vector3 motion = horizontalVel + dashVel;
        motion.y = verticalVel.y;
        controller.Move(motion * dt);

        // Ceiling hit
        if ((controller.collisionFlags & CollisionFlags.Above) != 0 && verticalVel.y > 0f)
            verticalVel.y = 0f;
    }

    // ---------- Safe respawn WITHOUT disabling the CC ----------
    IEnumerator RespawnRoutine(Vector3 targetPos)
    {
        isRespawning = true;

        // stop motion
        verticalVel = Vector3.zero;
        dashVel = Vector3.zero;

        // Keep CC enabled; just disable collision so teleport won't snag
        controller.detectCollisions = false;

        // Wait a frame, teleport, wait one more, then re-enable collisions
        yield return null;                // frame 1
        transform.position = targetPos;
        yield return null;                // frame 2
        controller.detectCollisions = true;

        // small grace after reappear
        postJumpTimer = 0.08f;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;

        isRespawning = false;
    }

    public void SetCheckpoint(Vector3 worldPos) => respawnPoint = worldPos;

    void OnDrawGizmosSelected()
    {
        if (!groundCheck) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}