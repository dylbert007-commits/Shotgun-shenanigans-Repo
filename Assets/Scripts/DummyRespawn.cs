using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Damageable))]
public class RespawnOnDeath : MonoBehaviour
{
    [Header("Respawn Settings")]
    public float respawnDelay = 2f;
    [Tooltip("Optional spawn transform; if null, uses initial position/rotation.")]
    public Transform spawnPoint;

    [Header("Objects To Hide")]
    [Tooltip("Leave empty to auto-grab all child Renderers.")]
    public Renderer[] renderersToHide;
    [Tooltip("Leave empty to auto-grab all child Colliders.")]
    public Collider[] collidersToToggle;

    private Damageable dmg;
    private Vector3 _initialPos;
    private Quaternion _initialRot;
    private Rigidbody _rb;

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        dmg.destroyOnDeath = false; // we handle respawn manually

        _rb = GetComponent<Rigidbody>();

        _initialPos = transform.position;
        _initialRot = transform.rotation;

        if (renderersToHide == null || renderersToHide.Length == 0)
            renderersToHide = GetComponentsInChildren<Renderer>(includeInactive: true);

        if (collidersToToggle == null || collidersToToggle.Length == 0)
            collidersToToggle = GetComponentsInChildren<Collider>(includeInactive: true);
    }

    void OnEnable()
    {
        dmg.OnDied += HandleDied;
    }

    void OnDisable()
    {
        dmg.OnDied -= HandleDied;
    }

    void HandleDied()
    {
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        // Hide & disable interaction
        foreach (var r in renderersToHide) if (r) r.enabled = false;
        foreach (var c in collidersToToggle) if (c) c.enabled = false;

        // Stop physics drift
        if (_rb)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        yield return new WaitForSeconds(respawnDelay);

        // Reset transform
        transform.position = spawnPoint ? spawnPoint.position : _initialPos;
        transform.rotation = spawnPoint ? spawnPoint.rotation : _initialRot;

        // Reset health + shield
        dmg.ResetAll();

        // Show & re-enable
        foreach (var r in renderersToHide) if (r) r.enabled = true;
        foreach (var c in collidersToToggle) if (c) c.enabled = true;
    }
}