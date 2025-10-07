using UnityEngine;
using System;

[DisallowMultipleComponent]
public class Damageable : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Shield (optional)")]
    public float maxShield = 0f;          // set >0 to enable shields
    public float currentShield = 0f;
    public float shieldRegenPerSecond = 0f;
    public float shieldBreakCooldown = 0f; // seconds before regen can start after taking damage

    [Header("Death")]
    public bool destroyOnDeath = true;

    // Events
    public event Action<float, float> OnHealthChanged;   // current, max
    public event Action<float, float> OnShieldChanged;   // current, max
    public event Action<float> OnDamaged;                // total amount applied (post-hit)
    public event Action OnDied;

    float regenLockTimer;

    public bool IsDead => currentHealth <= 0f;
    public bool ShieldEnabled => maxShield > 0f;

    void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        currentShield = Mathf.Clamp(currentShield, 0f, maxShield);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        if (ShieldEnabled) OnShieldChanged?.Invoke(currentShield, maxShield);
    }

    void Update()
    {
        if (!ShieldEnabled || IsDead) return;

        if (regenLockTimer > 0f) regenLockTimer -= Time.deltaTime;
        else if (shieldRegenPerSecond > 0f && currentShield < maxShield)
        {
            currentShield = Mathf.Min(maxShield, currentShield + shieldRegenPerSecond * Time.deltaTime);
            OnShieldChanged?.Invoke(currentShield, maxShield);
        }
    }

    /// Apply damage. Handles shield first, then health, with overflow.
    public void ApplyDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;

        float remaining = amount;

        if (ShieldEnabled && currentShield > 0f)
        {
            float before = currentShield;
            currentShield = Mathf.Max(0f, currentShield - remaining);
            float consumed = before - currentShield;
            remaining -= consumed;

            // lock regen after any shield hit
            regenLockTimer = Mathf.Max(regenLockTimer, shieldBreakCooldown);
            OnShieldChanged?.Invoke(currentShield, maxShield);
        }

        if (remaining > 0f)
        {
            currentHealth = Mathf.Max(0f, currentHealth - remaining);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            if (currentHealth <= 0f) Die();
        }

        OnDamaged?.Invoke(amount);
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void RefillShield(float amount)
    {
        if (!ShieldEnabled || amount <= 0f) return;
        currentShield = Mathf.Clamp(currentShield + amount, 0f, maxShield);
        OnShieldChanged?.Invoke(currentShield, maxShield);
    }

    public void ResetAll(float? newMaxHP = null, float? newMaxShield = null)
    {
        if (newMaxHP.HasValue) maxHealth = Mathf.Max(1f, newMaxHP.Value);
        if (newMaxShield.HasValue) maxShield = Mathf.Max(0f, newMaxShield.Value);
        currentHealth = maxHealth;
        currentShield = maxShield;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        if (ShieldEnabled) OnShieldChanged?.Invoke(currentShield, maxShield);
    }

    void Die()
    {
        OnDied?.Invoke();
        if (destroyOnDeath) Destroy(gameObject);
    }
}