using UnityEngine;
using UnityEngine.UI;

public class HealthShieldBarWorld : MonoBehaviour
{
    [Header("Target")]
    public Damageable target;                 // auto-find parent if left null

    [Header("HEALTH (HP)")]
    public Image healthFill;                  // red (instant)
    public Image healthChip;                  // yellow (lags)

    [Header("SHIELD (optional)")]
    public Image shieldFill;                  // blue (instant)
    public Image shieldChip;                  // light-blue (lags)

    [Header("Chip Tuning (shared)")]
    [Tooltip("Delay before the chip starts falling after taking damage")]
    public float chipDelay = 0.15f;
    [Tooltip("Speed chip falls toward true value (normalized fill/sec)")]
    public float chipSpeed = 1.6f;
    [Tooltip("Speed chip snaps upward on heal/regen")]
    public float healSnapSpeed = 4.0f;

    [Header("Placement")]
    public Vector3 worldOffset = new Vector3(0f, 2f, 0f);
    public bool stickToTarget = true;

    [Header("Billboard")]
    public bool faceCamera = true;
    public bool onlyYaw = true;

    [Header("Visibility")]
    public bool hideWhenFull = false;         // hide whole bar when HP is 100%
    public bool showShieldWhenZero = false;   // keep blue visible at 0% (slot look)

    // internals
    Camera _cam;

    // HEALTH state
    float hTarget01, hFront01, hChip01, hDelay;
    float lastHP;

    // SHIELD state
    float sTarget01, sFront01, sChip01, sDelay;
    float lastShield;

    void Awake()
    {
        if (!target) target = GetComponentInParent<Damageable>();
        if (!target)
        {
            Debug.LogWarning($"{nameof(HealthShieldBarWorld)}: No Damageable found in parents.");
            enabled = false; return;
        }

        _cam = Camera.main;

        // Init health
        hTarget01 = hFront01 = hChip01 = Safe01(target.currentHealth, target.maxHealth);
        lastHP = target.currentHealth;

        // Init shield (fully symmetric to health)
        sTarget01 = sFront01 = sChip01 = (target.maxShield > 0f)
            ? Safe01(target.currentShield, target.maxShield) : 0f;
        lastShield = target.currentShield;

        // Subscribe
        target.OnHealthChanged += OnHealthChanged;
        target.OnShieldChanged += OnShieldChanged;

        ApplyNow();
        UpdateShieldVisibility();
        UpdateWholeVisibility();
    }

    void OnDestroy()
    {
        if (!target) return;
        target.OnHealthChanged -= OnHealthChanged;
        target.OnShieldChanged -= OnShieldChanged;
    }

    void LateUpdate()
    {
        if (!_cam) _cam = Camera.main;

        // follow
        if (stickToTarget && target)
            transform.position = target.transform.position + worldOffset;

        // billboard
        if (faceCamera && _cam)
        {
            Vector3 toCam = _cam.transform.position - transform.position;
            if (onlyYaw) toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        // HEALTH: instant (front) eases to target
        hFront01 = SmoothExp(hFront01, hTarget01, 16f);

        // HEALTH chip: delay then fall; snap on heal
        if (hChip01 > hFront01)  // took damage
        {
            if (hDelay > 0f) hDelay -= Time.deltaTime;
            else hChip01 = Mathf.MoveTowards(hChip01, hFront01, chipSpeed * Time.deltaTime);
        }
        else                     // heal
        {
            hChip01 = Mathf.MoveTowards(hChip01, hFront01, healSnapSpeed * Time.deltaTime);
        }

        // SHIELD: instant (front) eases to target
        sFront01 = SmoothExp(sFront01, sTarget01, 16f);

        // SHIELD chip: IDENTICAL behavior to health chip
        if (sChip01 > sFront01)  // took shield damage
        {
            if (sDelay > 0f) sDelay -= Time.deltaTime;
            else sChip01 = Mathf.MoveTowards(sChip01, sFront01, chipSpeed * Time.deltaTime);
        }
        else                     // shield heal/regen
        {
            sChip01 = Mathf.MoveTowards(sChip01, sFront01, healSnapSpeed * Time.deltaTime);
        }

        ApplyNow();
    }

    // -------- events from Damageable (symmetric) --------

    void OnHealthChanged(float cur, float max)
    {
        float new01 = Safe01(cur, max);
        hTarget01 = new01;

        if (cur < lastHP)  // damage → start delay, chip will fall later
            hDelay = chipDelay;
        else               // heal → snap chip upward toward front
            hChip01 = Mathf.Max(hChip01, hTarget01);

        lastHP = cur;
        UpdateWholeVisibility();
    }

    void OnShieldChanged(float cur, float max)
    {
        float new01 = Safe01(cur, max);
        sTarget01 = new01;

        if (cur < lastShield)  // damage → start delay, chip will fall later
            sDelay = chipDelay;
        else                   // regen/heal → snap chip upward toward front
            sChip01 = Mathf.Max(sChip01, sTarget01);

        lastShield = cur;
        UpdateShieldVisibility();
        UpdateWholeVisibility();
    }

    // -------- helpers --------

    void ApplyNow()
    {
        if (healthFill) healthFill.fillAmount = Mathf.Clamp01(hFront01);
        if (healthChip) healthChip.fillAmount = Mathf.Clamp01(hChip01);
        if (shieldFill) shieldFill.fillAmount = Mathf.Clamp01(sFront01);
        if (shieldChip) shieldChip.fillAmount = Mathf.Clamp01(sChip01);
    }

    void UpdateShieldVisibility()
    {
        bool hasShieldSystem = target && target.maxShield > 0f;
        bool showShield = hasShieldSystem && (showShieldWhenZero || sTarget01 > 0f);

        if (shieldFill) shieldFill.enabled = showShield;
        if (shieldChip) shieldChip.enabled = showShield;
    }

    void UpdateWholeVisibility()
    {
        bool show = true;
        if (hideWhenFull && hTarget01 >= 0.999f) show = false;

        bool noShieldToShow = (target.maxShield <= 0f) || (!showShieldWhenZero && sTarget01 <= 0.001f);
        if (hTarget01 <= 0.001f && noShieldToShow) show = false;

        var cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha = show ? 1f : 0f;
        cg.blocksRaycasts = false;
    }

    static float Safe01(float cur, float max) => (max <= 0f) ? 0f : Mathf.Clamp01(cur / max);

    static float SmoothExp(float current, float target, float speedPerSec)
    {
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-speedPerSec * Time.deltaTime));
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureFilled(healthFill);
        EnsureFilled(healthChip);
        EnsureFilled(shieldFill);
        EnsureFilled(shieldChip);
    }

    static void EnsureFilled(Image img)
    {
        if (!img) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
    }
#endif
}
