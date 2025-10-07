using UnityEngine;

public class SpinBob : MonoBehaviour
{
    public Vector3 spin = new Vector3(0f, 90f, 0f); // deg/sec around Y
    public float bobAmplitude = 0.15f;
    public float bobSpeed = 2f;

    private Vector3 startPos;
    private float t;

    void Awake() => startPos = transform.position;

    void Update()
    {
        // Spin
        transform.Rotate(spin * Time.deltaTime, Space.World);

        // Bob
        t += Time.deltaTime * bobSpeed;
        var pos = startPos;
        pos.y += Mathf.Sin(t) * bobAmplitude;
        transform.position = pos;
    }
}