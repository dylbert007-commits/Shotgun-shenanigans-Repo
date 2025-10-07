using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class DamageNumberSystem : MonoBehaviour
{
    public static DamageNumberSystem Instance;

    [Header("Prefab (assign in scene or auto-load from Resources/DamageNumberText)")]
    public DamageNumberText damagePrefab;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // If user didn't assign the prefab, try auto-load from Resources
        TryAutoLoadPrefab();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null)
        {
            // Try find existing in scene
            var found = FindObjectOfType<DamageNumberSystem>();
            if (found != null) { Instance = found; Instance.TryAutoLoadPrefab(); return; }

            // Create one if none exists
            var go = new GameObject("DamageNumberSystem (auto)");
            Instance = go.AddComponent<DamageNumberSystem>();
            Instance.TryAutoLoadPrefab();
        }
    }

    void TryAutoLoadPrefab()
    {
        if (damagePrefab != null) return;

        // Will succeed if you place the prefab at Assets/Resources/DamageNumberText.prefab
        var loaded = Resources.Load<DamageNumberText>("DamageNumberText");
        if (loaded != null) damagePrefab = loaded;
    }

    /// Spawn at position; pushes slightly toward camera so it doesn’t clip.
    public static void Spawn(Vector3 worldPos, float amount, bool crit = false, float cameraPush = 0.06f)
    {
        if (!EnsureReady()) return;

        Vector3 pos = worldPos;
        if (Camera.main) pos += -Camera.main.transform.forward * cameraPush;

        var dn = Instantiate(Instance.damagePrefab, pos, Quaternion.identity);
        dn.Init(amount, crit);
    }

    /// Back-compat wrapper (safe to leave calls in older code)
    public static void SpawnAtHit(Vector3 worldPos, Vector3 surfaceNormal, float amount, bool crit = false)
        => Spawn(worldPos, amount, crit, 0.06f);

    static bool EnsureReady()
    {
        if (Instance == null)
        {
            Bootstrap();
            if (Instance == null)
            {
                Debug.LogWarning("DamageNumberSystem: no Instance in scene and auto-bootstrap failed.");
                return false;
            }
        }

        if (Instance.damagePrefab == null)
        {
            Instance.TryAutoLoadPrefab();
            if (Instance.damagePrefab == null)
            {
                Debug.LogWarning("DamageNumberSystem: damagePrefab not assigned. " +
                                 "Assign it on the system object OR place the prefab at Resources/DamageNumberText.");
                return false;
            }
        }
        return true;
    }
}