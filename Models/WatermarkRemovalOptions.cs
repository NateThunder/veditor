namespace VeditorWindow.Models;

public sealed record WatermarkRemovalOptions
{
    public string DetectionPrompt { get; init; } = "watermark";

    public double MaxBoundingBoxPercent { get; init; } = 10D;

    public int DetectionSkip { get; init; } = 3;

    public double FadeInSeconds { get; init; } = 0.25D;

    public double FadeOutSeconds { get; init; } = 0.25D;

    public bool UseGpuWhenAvailable { get; init; } = true;

    public bool PreviewOnly { get; init; }

    public bool SelectionPreviewOnly { get; init; }

    public int? PreviewFrameIndex { get; init; }

    public double MaskPaddingPercent { get; init; } = 0.5D;

    public IReadOnlyList<WatermarkRegion> Regions { get; init; } = Array.Empty<WatermarkRegion>();

    public string? OutputPath { get; init; }

    public bool Overwrite { get; init; }

    public IReadOnlyList<string> Validate()
    {
        //== input validation ==================================================
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(DetectionPrompt))
        {
            errors.Add("Detection prompt is required.");
        }

        if (!double.IsFinite(MaxBoundingBoxPercent) ||
            MaxBoundingBoxPercent <= 0D ||
            MaxBoundingBoxPercent > 100D)
        {
            errors.Add("Maximum detection size must be greater than 0 and no more than 100 percent.");
        }

        if (DetectionSkip is < 1 or > 10)
        {
            errors.Add("Detection interval must be between 1 and 10 frames.");
        }

        if (!double.IsFinite(FadeInSeconds) || FadeInSeconds < 0D)
        {
            errors.Add("Fade-in seconds must be a finite, non-negative value.");
        }

        if (!double.IsFinite(FadeOutSeconds) || FadeOutSeconds < 0D)
        {
            errors.Add("Fade-out seconds must be a finite, non-negative value.");
        }

        if (PreviewFrameIndex < 0)
        {
            errors.Add("The preview frame index cannot be negative.");
        }

        if (!double.IsFinite(MaskPaddingPercent) ||
            MaskPaddingPercent < 0D ||
            MaskPaddingPercent > 10D)
        {
            errors.Add("Mask padding must be between 0 and 10 percent.");
        }

        if (Regions.Count > 64)
        {
            errors.Add("No more than 64 watermark regions can be selected.");
        }

        if (Regions.Any(region => region is null || !region.IsValid))
        {
            errors.Add("Every watermark region must be a valid rectangle inside the media bounds.");
        }

        if (OutputPath is not null && string.IsNullOrWhiteSpace(OutputPath))
        {
            errors.Add("The selected output path cannot be blank.");
        }

        return errors;
        //=====================================================================
    }
}
