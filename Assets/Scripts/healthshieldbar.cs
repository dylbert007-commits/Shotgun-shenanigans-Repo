using UnityEngine;
using UnityEngine.UI;

public class HealthShieldBarChip : MonoBehaviour
{
    [Header("Target")]
    public Damageable target;           // auto-find if left empty

    [Header("Health Images")]
    public Image healthFill;            // red (instant)
    public Image healthChip;            // yellow (lags)

    [Header("Shield Images (optional)")]
    public Image shieldFill;            // blue (instant)
    public Image shieldChip;            // lighter blue (lags)

    [Header("Chip Tuning")]
    public float chipDelay = 0.15f;     // wait before chip starts dropping
    public float chipSpeed = 1.6f;      // how fast chip closes the gap (units/sec of normalized fill)
    public float healSnapSpeed = 4.0f;  // how fast chip snaps up on heal (faster than damage drop)

    float healthTarget01, healthChip01;
    float shieldTarget01, shieldChip01;
    float chipDelayTimerH, chipDelayTimerS;
    float lastHealth, lastShield;

    void Awake()
    {
        if (!target) target = GetComponentInParent<Damageable>();
        if (!target) { enabled = false; return; }

        target.OnHealthChanged += OnHealthChanged;
        target.OnShieldChanged += OnShieldChanged;

        // init
        healthTarget01 = target.maxHealth > 0 ? target.currentHealth / target.maxHealth : 0f;
        healthChip01 = healthTarget01;
        lastHealth = target.currentHealth;

        if (target.maxShield > 0f)
        {
            shieldTarget01 = target.currentShield / Mathf.Max(1f, target.maxShield);
            shieldChip01 = shieldTarget01;
            lastShield = target.currentShield;
        }

        ApplyNow();
    }

    void OnDestroy()
    {
        if (!target) return;
        target.OnHealthChanged -= OnHealthChanged;
        target.OnShieldChanged -= OnShieldChanged;
    }

    void OnHealthChanged(float cur, float max)
    {
        float prev01 = healthTarget01;
        healthTarget01 = max > 0f ? Mathf.Clamp01(cur / max) : 0f;

        // instant bar snaps to new target
        if (healthFill) healthFill.fillAmount = healthTarget01;

        // detect damage vs heal to set behavior
        if (cur < lastHealth)          // took damage → start delay, chip will fall
            chipDelayTimerH = chipDelay;
        else if (cur > lastHealth)     // healed → chip should quickly rise to match
            healthChip01 = Mathf.MoveTowards(healthChip01, healthTarget01, healSnapSpeed * Time.deltaTime * 10f);

        lastHealth = cur;
    }

    void OnShieldChanged(float cur, float max)
    {
        float newTarget = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
        shieldTarget01 = newTarget;
        if (shieldFill) shieldFill.fillAmount = shieldTarget01;

        if (cur < lastShield)          // damage to shield → delay then drop
            chipDelayTimerS = chipDelay;
        else if (cur > lastShield)     // shield heal/regen → snap up quickly
            shieldChip01 = Mathf.MoveTowards(shieldChip01, shieldTarget01, healSnapSpeed * Time.deltaTime * 10f);

        lastShield = cur;

        // hide shield bars if no shield enabled
        bool hasShield = max > 0f;
        if (shieldFill) shieldFill.enabled = hasShield && shieldTarget01 > 0f;
        if (shieldChip) shieldChip.enabled = hasShield && shieldChip01 > 0f;
    }

    void Update()
    {
        // HEALTH chip behavior
        if (chipDelayTimerH > 0f) chipDelayTimerH -= Time.deltaTime;
        else if (healthChip01 > healthTarget01) // drop towards target
            healthChip01 = Mathf.MoveTowards(healthChip01, healthTarget01, chipSpeed * Time.deltaTime);
        else // if target moved up (heal), close faster
            healthChip01 = Mathf.MoveTowards(healthChip01, healthTarget01, healSnapSpeed * Time.deltaTime);

        // SHIELD chip behavior
        if (shieldChip)
        {
            if (chipDelayTimerS > 0f) chipDelayTimerS -= Time.deltaTime;
            else if (shieldChip01 > shieldTarget01)
                shieldChip01 = Mathf.MoveTowards(shieldChip01, shieldTarget01, chipSpeed * Time.deltaTime);
            else
                shieldChip01 = Mathf.MoveTowards(shieldChip01, shieldTarget01, healSnapSpeed * Time.deltaTime);
        }

        ApplyNow();
    }

    void ApplyNow()
    {
        if (healthChip) healthChip.fillAmount = Mathf.Clamp01(healthChip01);
        if (shieldChip) shieldChip.fillAmount = Mathf.Clamp01(shieldChip01);
        if (healthFill) healthFill.fillAmount = Mathf.Clamp01(healthTarget01);
        if (shieldFill) shieldFill.fillAmount = Mathf.Clamp01(shieldTarget01);
    }
}