namespace Shotlight.Core;

public static class PreviewScale
{
    // A preview scale never alters the stored capture dimensions.
    public static double Fit(double imageWidth, double imageHeight, double viewportWidth, double viewportHeight, double padding = 28)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || !double.IsFinite(imageWidth) || !double.IsFinite(imageHeight))
            throw new ArgumentOutOfRangeException(nameof(imageWidth));
        return Math.Clamp(Math.Min(1, Math.Min(Math.Max(1, viewportWidth-2*padding)/imageWidth, Math.Max(1, viewportHeight-2*padding)/imageHeight)), .01, 1);
    }
}
