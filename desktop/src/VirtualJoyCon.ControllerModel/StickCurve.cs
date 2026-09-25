namespace VirtualJoyCon.ControllerModel;

/// <summary>
/// Applies deadzone / sensitivity / response curve / inversion / max intensity
/// to a raw -1..1 stick vector. Shared by mouse/touch stick, keyboard sticks and
/// physical gamepads so behavior is identical regardless of the source.
/// </summary>
public sealed class StickCurve
{
    public float Deadzone = 0.10f;       // circular deadzone, 0..0.9
    public float Sensitivity = 1.0f;     // multiplier applied after deadzone
    public float MaxIntensity = 1.0f;    // outer cap 0.1..1.0
    public bool InvertY;
    public CurveKind Curve = CurveKind.Linear;
    public float CurveExponent = 1.0f;   // used when Curve == Custom

    public enum CurveKind { Linear, Gentle, Aggressive, Custom }

    public float RawMagnitude(float x, float y) => MathF.Sqrt(x * x + y * y);

    /// <summary>Normalize raw input (-1..1 per axis) into processed output (-1..1).</summary>
    public void Process(float rawX, float rawY, out float outX, out float outY)
    {
        float x = Math.Clamp(rawX, -1f, 1f);
        float y = Math.Clamp(rawY, -1f, 1f);

        if (InvertY) y = -y;

        float mag = MathF.Sqrt(x * x + y * y);
        if (mag < 1e-6f || mag <= Deadzone)
        {
            outX = 0f; outY = 0f;
            return;
        }

        // Rescale so the area outside the deadzone maps 0..1 (avoids the "jump" at edge of dz).
        float scaled = (mag - Deadzone) / (1f - Deadzone);
        float t = ResponseCurve(scaled);
        float k = t / mag * Sensitivity;
        float nx = x * k;
        float ny = y * k;

        float mag2 = MathF.Sqrt(nx * nx + ny * ny);
        float cap = Math.Clamp(MaxIntensity, 0.05f, 1f);
        if (mag2 > cap)
        {
            float s = cap / mag2;
            nx *= s; ny *= s;
        }

        outX = Math.Clamp(nx, -1f, 1f);
        outY = Math.Clamp(ny, -1f, 1f);
    }

    private float ResponseCurve(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Curve switch
        {
            CurveKind.Gentle => t * t * (3f - 2f * t),                       // smoothstep
            CurveKind.Aggressive => MathF.Pow(t, 0.65f),                     // fast start
            CurveKind.Custom => MathF.Pow(t, Math.Clamp(CurveExponent, 0.2f, 4f)),
            _ => t,
        };
    }
}
