namespace Harbor.Core.Services;

/// <summary>
/// Pure math for the dock magnification (fishbowl) effect.
/// Computes scale factors using cosine falloff based on distance from cursor.
/// </summary>
public static class DockMagnificationCalculator
{
    /// <summary>
    /// Computes the scale factor for each icon based on cursor proximity.
    /// Uses cosine falloff: icons near the cursor scale up to maxScale,
    /// icons beyond effectRadius remain at 1.0.
    /// </summary>
    /// <param name="mouseX">Cursor X position relative to the dock panel.</param>
    /// <param name="iconCenters">Center X position of each icon.</param>
    /// <param name="maxScale">Maximum scale factor (e.g., 1.5).</param>
    /// <param name="effectRadius">Radius of the effect in icon-slot units (e.g., 3.0).</param>
    /// <param name="iconPitch">Distance between icon centers in pixels (e.g., 56.0).</param>
    /// <returns>Array of scale factors, one per icon.</returns>
    public static double[] ComputeScales(
        double mouseX,
        double[] iconCenters,
        double maxScale,
        double effectRadius,
        double iconPitch)
    {
        if (iconCenters.Length == 0)
            return [];

        var scales = new double[iconCenters.Length];

        for (int i = 0; i < iconCenters.Length; i++)
        {
            var distance = Math.Abs(mouseX - iconCenters[i]) / iconPitch;

            if (distance > effectRadius)
            {
                scales[i] = 1.0;
            }
            else
            {
                scales[i] = 1.0 + (maxScale - 1.0) * (1.0 + Math.Cos(Math.PI * distance / effectRadius)) / 2.0;
            }
        }

        return scales;
    }

    /// <summary>
    /// Computes the vertical offset (upward Y translation) to keep icons bottom-aligned
    /// when scaled.
    /// </summary>
    /// <param name="scale">The icon's current scale factor.</param>
    /// <param name="iconHeight">The icon's unscaled height in pixels.</param>
    /// <returns>Negative Y translation to apply (moves icon upward).</returns>
    public static double ComputeVerticalOffset(double scale, double iconHeight)
    {
        // With RenderTransformOrigin at (0.5, 1.0), scaling grows upward from bottom.
        // No additional Y translation needed for bottom-aligned growth.
        return 0;
    }
}
