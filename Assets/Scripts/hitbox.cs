using UnityEngine;

public class Hitbox : MonoBehaviour
{
    [Tooltip("Damage multiplier when this collider is hit (e.g., 2 = headshot).")]
    public float damageMultiplier = 2f;

    [Tooltip("If true, damage numbers will show as crit when this hitbox is struck.")]
    public bool countsAsCrit = true;
}