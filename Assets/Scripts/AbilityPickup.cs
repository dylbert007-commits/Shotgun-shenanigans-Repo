using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class AbilityPickup : MonoBehaviour
{
    public enum AbilityType { DoubleJump, Dash }
    public AbilityType ability = AbilityType.DoubleJump;

    [Header("Optional FX")]
    public GameObject collectVfx;
    public AudioClip collectSfx;
    [Range(0f, 1f)] public float sfxVolume = 0.8f;

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        var rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerMovement>();
        if (!player) return;

        switch (ability)
        {
            case AbilityType.DoubleJump:
                // unlock exactly one air jump (double jump)
                player.maxAirJumps = Mathf.Max(player.maxAirJumps, 1);
                break;

            case AbilityType.Dash:
                player.canDash = true;
                break;
        }

        if (collectVfx)
        {
            var fx = Instantiate(collectVfx, transform.position, Quaternion.identity);
            Destroy(fx, 3f);
        }
        if (collectSfx)
            AudioSource.PlayClipAtPoint(collectSfx, transform.position, sfxVolume);

        Destroy(gameObject); // permanent unlock
    }
}