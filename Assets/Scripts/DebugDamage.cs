using UnityEngine;

public class DebugDamage : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            var cam = Camera.main;
            if (!cam) return;

            Vector3 pos = cam.transform.position + cam.transform.forward * 2f;
            // use Spawn (it already pushes a bit towards camera so it won't clip)
            DamageNumberSystem.Spawn(pos, Random.Range(10, 200), false);
        }
    }
}