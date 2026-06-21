using UnityEngine;

namespace CaptainValheim;

internal sealed class ThrowProjectileVisualSpin : MonoBehaviour
{
    private const float DegreesPerSecond = 720f;

    internal enum AxisMode
    {
        None,
        HorizontalSide,
        WorldUp
    }

    private AxisMode _axisMode = AxisMode.HorizontalSide;
    private Vector3 _horizontalForward;
    private bool _hasHorizontalForward;

    private void LateUpdate()
    {
        if (_axisMode == AxisMode.None)
        {
            return;
        }

        Vector3 axis = _axisMode == AxisMode.WorldUp ? Vector3.up : ResolveHorizontalSideAxis();
        transform.Rotate(axis, DegreesPerSecond * Time.deltaTime, Space.World);
    }

    private Vector3 ResolveHorizontalSideAxis()
    {
        if (_hasHorizontalForward)
        {
            return ResolveSideAxis(_horizontalForward);
        }

        Transform axisSource = transform.parent != null ? transform.parent : transform;
        Vector3 forward = Vector3.ProjectOnPlane(axisSource.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.ProjectOnPlane(axisSource.right, Vector3.up);
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            return Vector3.right;
        }

        return ResolveSideAxis(forward);
    }

    private static Vector3 ResolveSideAxis(Vector3 forward)
    {
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            return Vector3.right;
        }

        Vector3 right = Vector3.Cross(Vector3.up, forward.normalized);
        return right.sqrMagnitude > 0.001f ? right.normalized : Vector3.right;
    }

    internal static void Ensure(GameObject? visual, AxisMode axisMode = AxisMode.HorizontalSide, Vector3 horizontalForward = default)
    {
        if (visual == null)
        {
            return;
        }

        if (axisMode == AxisMode.None)
        {
            ThrowProjectileVisualSpin? existing = visual.GetComponent<ThrowProjectileVisualSpin>();
            if (existing != null)
            {
                Object.Destroy(existing);
            }

            return;
        }

        ThrowProjectileVisualSpin spin = visual.GetComponent<ThrowProjectileVisualSpin>() ?? visual.AddComponent<ThrowProjectileVisualSpin>();
        spin._axisMode = axisMode;
        spin._horizontalForward = Vector3.ProjectOnPlane(horizontalForward, Vector3.up);
        spin._hasHorizontalForward = axisMode == AxisMode.HorizontalSide && spin._horizontalForward.sqrMagnitude > 0.001f;
    }
}

internal static class ProjectileSpinAxis
{
    internal const string None = "none";
    internal const string Horizontal = "horizontal";
    internal const string Vertical = "vertical";

    internal static string Normalize(string? raw)
    {
        string value = raw?.Trim() ?? "";
        if (value.Length == 0)
        {
            return "";
        }

        if (value.Equals(None, System.StringComparison.OrdinalIgnoreCase))
        {
            return None;
        }

        if (value.Equals(Horizontal, System.StringComparison.OrdinalIgnoreCase))
        {
            return Horizontal;
        }

        return value.Equals(Vertical, System.StringComparison.OrdinalIgnoreCase) ? Vertical : "";
    }

    internal static bool TryResolveAxisMode(string? raw, out ThrowProjectileVisualSpin.AxisMode axisMode)
    {
        string normalized = Normalize(raw);
        axisMode = default;
        return normalized switch
        {
            None => Set(ThrowProjectileVisualSpin.AxisMode.None, out axisMode),
            Horizontal => Set(ThrowProjectileVisualSpin.AxisMode.HorizontalSide, out axisMode),
            Vertical => Set(ThrowProjectileVisualSpin.AxisMode.WorldUp, out axisMode),
            _ => false
        };
    }

    private static bool Set(ThrowProjectileVisualSpin.AxisMode value, out ThrowProjectileVisualSpin.AxisMode axisMode)
    {
        axisMode = value;
        return true;
    }
}

internal sealed class ThrowProjectileVisualRotationOffset : MonoBehaviour
{
    private bool _hasBaseRotation;
    private Quaternion _baseLocalRotation;
    private Vector3 _offset;

    internal static void Ensure(GameObject? visual, Vector3 offset)
    {
        if (visual == null)
        {
            return;
        }

        ThrowProjectileVisualRotationOffset state =
            visual.GetComponent<ThrowProjectileVisualRotationOffset>() ??
            visual.AddComponent<ThrowProjectileVisualRotationOffset>();
        state.Apply(offset);
    }

    private void Apply(Vector3 offset)
    {
        if (!_hasBaseRotation)
        {
            _baseLocalRotation = transform.localRotation;
            _hasBaseRotation = true;
        }

        if (Approximately(_offset, offset))
        {
            return;
        }

        _offset = offset;
        transform.localRotation = _baseLocalRotation * Quaternion.Euler(offset);
    }

    private static bool Approximately(Vector3 left, Vector3 right)
    {
        return Mathf.Approximately(left.x, right.x) &&
               Mathf.Approximately(left.y, right.y) &&
               Mathf.Approximately(left.z, right.z);
    }
}
