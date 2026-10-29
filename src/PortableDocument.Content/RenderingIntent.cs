namespace PortableDocument.Content;

/// <summary>PDF rendering intents (ISO 32000-2, 8.6.5.8). The default is relative colorimetric.</summary>
public enum RenderingIntent
{
    RelativeColorimetric,
    Perceptual,
    Saturation,
    AbsoluteColorimetric
}

static class RenderingIntentNames
{
    public static RenderingIntent Parse(string? name) => name switch
    {
        "Perceptual" => RenderingIntent.Perceptual,
        "Saturation" => RenderingIntent.Saturation,
        "AbsoluteColorimetric" => RenderingIntent.AbsoluteColorimetric,
        _ => RenderingIntent.RelativeColorimetric
    };
}
