using System;

namespace NugzzMinimap.Helpers;

/// <summary>Shared shape/scale state used when the phone map opens and closes.</summary>
public static class MinimapState
{
    private const float BaseRadius = 75f;

    public static bool IsCircleMode { get; private set; } = true;
    public static float MinimapRadius { get; private set; } = BaseRadius;
    public static float ScaleFactor { get; private set; } = 1f;

    public static void UpdateState(bool isCircle, float scaleFactor)
    {
        IsCircleMode = isCircle;
        ScaleFactor = Math.Max(0.1f, scaleFactor);
        MinimapRadius = BaseRadius * ScaleFactor;
    }
}
