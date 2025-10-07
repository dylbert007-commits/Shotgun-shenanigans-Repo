using UnityEngine;
using TMPro;

public class DamageNumberText : MonoBehaviour
{
    public TMP_Text label;                 // auto-finds if null
    public float riseSpeed = 0.8f;
    public float lifetime = 1.2f;
    public AnimationCurve alphaOverLife = AnimationCurve.EaseInOut(0, 1, 1, 0);

    float _t;
    Color _baseColor = Color.white;
    Transform _cam;                        // ← Transform, not Camera

    void Awake()
    {
        // find text (3D TMP, not UI)
        if (!label) label = GetComponent<TMP_Text>();
        if (!label) label = GetComponentInChildren<TMP_Text>(true);

        var c = label ? label.color : Color.white;
        if (label) { c.a = 1f; label.color = c; }

        // ✅ assign transform of Camera.main
        var cam = Camera.main;
        _cam = cam ? cam.transform : null;
    }

    public void Init(float amount, bool crit = false)
    {
        if (!label) label = GetComponentInChildren<TMP_Text>(true);
        if (!label)
        {
            Debug.LogError("[DamageNumberText] No 3D TMP_Text on prefab/children. Use TextMeshPro → Text (TextMeshPro), not UI.");
            Destroy(gameObject);
            return;
        }

        label.text = Mathf.RoundToInt(amount).ToString();
        _baseColor = crit ? new Color(1f, 0.9f, 0.25f, 1f) : Color.white;
        label.color = _baseColor;
    }

    void Update()
    {
        if (!label) return;

        _t += Time.deltaTime;

        // float up
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        // billboard to camera
        if (!_cam && Camera.main) _cam = Camera.main.transform;
        if (_cam) transform.rotation = Quaternion.LookRotation(transform.position - _cam.position);

        // fade
        float a = alphaOverLife.Evaluate(Mathf.Clamp01(_t / lifetime));
        var c = _baseColor; c.a = a; label.color = c;

        if (_t >= lifetime) Destroy(gameObject);
    }
}