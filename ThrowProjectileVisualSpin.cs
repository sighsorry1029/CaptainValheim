using UnityEngine;

namespace CaptainValheim;

internal sealed class ThrowProjectileVisualSpin : MonoBehaviour
{
    private const float DegreesPerSecond = 720f;

    private void LateUpdate()
    {
        transform.Rotate(Vector3.up, DegreesPerSecond * Time.deltaTime, Space.World);
    }

    internal static void Ensure(GameObject? visual)
    {
        if (visual == null)
        {
            return;
        }

        ThrowProjectileVisualSpin spin = visual.GetComponent<ThrowProjectileVisualSpin>() ?? visual.AddComponent<ThrowProjectileVisualSpin>();
        spin.enabled = true;
    }
}
