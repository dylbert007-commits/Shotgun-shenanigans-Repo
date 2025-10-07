using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Damageable))]
public class DamageFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    public Color flashColor = new Color(1f, 0.2f, 0.2f, 1f);
    public float flashTime = 0.07f;
    public float flashFade = 0.07f;

    [Tooltip("Leave empty to auto-grab all child Renderers.")]
    public Renderer[] renderersToFlash;

    private Damageable dmg;

    static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    static readonly int ColorID = Shader.PropertyToID("_Color");

    private class FlashTarget
    {
        public Renderer r;
        public MaterialPropertyBlock block;
        public int colorProp;
        public Color baseColor;
    }
    private readonly List<FlashTarget> targets = new List<FlashTarget>();

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        if (!dmg) { enabled = false; return; }

        if (renderersToFlash == null || renderersToFlash.Length == 0)
            renderersToFlash = GetComponentsInChildren<Renderer>();

        foreach (var r in renderersToFlash)
        {
            if (!r) continue;
            int prop = BaseColorID;
            if (r.sharedMaterial && !r.sharedMaterial.HasProperty(BaseColorID))
                prop = ColorID;

            Color baseCol = Color.white;
            if (r.sharedMaterial) baseCol = r.sharedMaterial.GetColor(prop);

            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);

            targets.Add(new FlashTarget
            {
                r = r,
                block = block,
                colorProp = prop,
                baseColor = baseCol
            });
        }
    }

    void OnEnable() { if (dmg != null) dmg.OnDamaged += HandleDamaged; }
    void OnDisable() { if (dmg != null) dmg.OnDamaged -= HandleDamaged; }

    void HandleDamaged(float amt)
    {
        StopAllCoroutines();
        StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        // Set flash color
        foreach (var t in targets)
        {
            t.block.SetColor(t.colorProp, flashColor);
            t.r.SetPropertyBlock(t.block);
        }

        yield return new WaitForSeconds(flashTime);

        float elapsed = 0f;
        while (elapsed < flashFade)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Clamp01(elapsed / flashFade);

            foreach (var t in targets)
            {
                Color c = Color.Lerp(flashColor, t.baseColor, a);
                t.block.SetColor(t.colorProp, c);
                t.r.SetPropertyBlock(t.block);
            }
            yield return null;
        }

        // restore base
        foreach (var t in targets)
        {
            t.block.SetColor(t.colorProp, t.baseColor);
            t.r.SetPropertyBlock(t.block);
        }
    }
}