using UnityEngine;

// Visual-only tracer that animates from start to end over time.
// Does not affect gameplay; ShotgunRaycast still uses raycasts for hits.
[RequireComponent(typeof(LineRenderer))]
public class BulletTracer : MonoBehaviour
{
    public float speed = 200f;          // units per second
    public float minSegment = 0.1f;     // minimum visible segment length
    public bool fadeOutOnFinish = true;
    public float fadeOutTime = 0.08f;
    public Color color = Color.yellow;  // tracer color
    public float startDelay = 0.05f;    // delay before appearing

    LineRenderer lr;
    Vector3 p0;
    Vector3 p1;
    float t;            // 0..1 progress along the line
    bool finished;
    float fadeT;
    Gradient originalGradient;
    float delayRemaining;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        if (lr == null) { enabled = false; return; }

        if (lr.positionCount >= 2)
        {
            p0 = lr.GetPosition(0);
            p1 = lr.GetPosition(1);
        }
        else
        {
            p0 = transform.position;
            p1 = p0 + transform.forward;
            lr.positionCount = 2;
            lr.SetPosition(0, p0);
            lr.SetPosition(1, p1);
        }

        // Start as a very short segment at the origin
        lr.positionCount = 2;
        lr.SetPosition(0, p0);
        lr.SetPosition(1, p0);

        // Apply color gradient based on desired color
        var g = new Gradient();
        g.SetKeys(
            new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );
        lr.colorGradient = g;
        originalGradient = g;

        // Hide until delay elapses
        delayRemaining = Mathf.Max(0f, startDelay);
        lr.enabled = (delayRemaining <= 0f);
    }

    public void Initialize(Vector3 start, Vector3 end)
    {
        p0 = start;
        p1 = end;
        if (lr == null) lr = GetComponent<LineRenderer>();
        if (lr)
        {
            lr.positionCount = 2;
            lr.SetPosition(0, p0);
            lr.SetPosition(1, p0);

            // Apply color gradient based on desired color
            var g = new Gradient();
            g.SetKeys(
                new GradientColorKey[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
            );
            lr.colorGradient = g;
            originalGradient = g;
        }
    }

    void Update()
    {
        if (finished)
        {
            if (fadeOutOnFinish && lr != null)
            {
                fadeT += Time.deltaTime / Mathf.Max(0.0001f, fadeOutTime);
                fadeT = Mathf.Clamp01(fadeT);
                var g = new Gradient();
                var c0 = originalGradient.colorKeys.Length > 0 ? originalGradient.colorKeys[0].color : Color.white;
                var c1 = originalGradient.colorKeys.Length > 1 ? originalGradient.colorKeys[1].color : c0;
                var a0 = Mathf.Lerp(1f, 0f, fadeT);
                var a1 = Mathf.Lerp(1f, 0f, fadeT);
                g.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(c0, 0f), new GradientColorKey(c1, 1f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) }
                );
                lr.colorGradient = g;
            }
            return;
        }

        // Delay appearance if requested
        if (delayRemaining > 0f)
        {
            delayRemaining -= Time.deltaTime;
            if (delayRemaining <= 0f && lr != null)
            {
                lr.enabled = true;
            }
            return;
        }

        float dist = Vector3.Distance(p0, p1);
        float move = speed * Time.deltaTime;
        float dt = dist > 0.0001f ? (move / dist) : 1f;
        t += dt;
        if (t >= 1f)
        {
            t = 1f;
            finished = true;
        }

        Vector3 tip = Vector3.Lerp(p0, p1, t);
        Vector3 tail;
        float seg = Mathf.Max(minSegment, dist * 0.1f); // 10% of path or minSegment
        if (seg >= dist)
            tail = p0;
        else
        {
            float tTail = Mathf.Clamp01(t - (seg / Mathf.Max(0.0001f, dist)));
            tail = Vector3.Lerp(p0, p1, tTail);
        }

        if (lr)
        {
            lr.SetPosition(0, tail);
            lr.SetPosition(1, tip);
        }
    }
}
