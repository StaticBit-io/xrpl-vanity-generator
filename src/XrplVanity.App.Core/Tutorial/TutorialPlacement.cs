namespace XrplVanity.App.Core.Tutorial;

/// <summary>A rectangle in viewport pixels, as the tutorial script measures lit regions and the bubble.</summary>
public readonly record struct TutorialRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// What <c>tutorial.js</c> reports: the rectangle of every lit region on the page (empty when
/// nothing is lit or nothing is on the page), the viewport and the bubble's own size, which
/// depends on its copy.
/// </summary>
public sealed record TutorialMeasurement(
    IReadOnlyList<TutorialRect>? Targets,
    double ViewWidth,
    double ViewHeight,
    double BubbleWidth,
    double BubbleHeight);

/// <summary>Decides where the bubble goes. Pure geometry, so it is tested without a browser.</summary>
public static class TutorialPlacement
{
    /// <summary>Space between the bubble and the region it points at.</summary>
    public const double Gap = 12;

    /// <summary>The closest the bubble gets to a window edge.</summary>
    public const double Margin = 8;

    /// <summary>
    /// Returns the bubble's rectangle. The candidates are the sides of each lit region in turn, in
    /// the order right, left, below, above, each slid along its side to stay in view. The first
    /// that is fully in view and covers no lit region wins. Judging against the regions one by one,
    /// not against their union, matters when several are lit: their union can span the window and
    /// swallow unlit space that is free. When every candidate covers something, the one that
    /// covers the least wins. With nothing lit the bubble sits at the top centre. The result lies
    /// inside the viewport whenever the bubble fits in it.
    /// </summary>
    public static TutorialRect Place(IReadOnlyList<TutorialRect>? lit, double bubbleWidth, double bubbleHeight, double viewWidth, double viewHeight)
    {
        if (lit is null || lit.Count == 0)
        {
            return new TutorialRect(ClampX((viewWidth - bubbleWidth) / 2, bubbleWidth, viewWidth), Margin, bubbleWidth, bubbleHeight);
        }

        List<TutorialRect> candidates = new(lit.Count * 4);
        foreach (TutorialRect t in lit)
        {
            candidates.Add(new TutorialRect(t.Right + Gap, ClampY(t.Top, bubbleHeight, viewHeight), bubbleWidth, bubbleHeight));
            candidates.Add(new TutorialRect(t.Left - Gap - bubbleWidth, ClampY(t.Top, bubbleHeight, viewHeight), bubbleWidth, bubbleHeight));
            candidates.Add(new TutorialRect(ClampX(t.Left, bubbleWidth, viewWidth), t.Bottom + Gap, bubbleWidth, bubbleHeight));
            candidates.Add(new TutorialRect(ClampX(t.Left, bubbleWidth, viewWidth), t.Top - Gap - bubbleHeight, bubbleWidth, bubbleHeight));
        }

        foreach (TutorialRect candidate in candidates)
        {
            if (Fits(candidate, viewWidth, viewHeight) && OverlapArea(candidate, lit) == 0)
            {
                return candidate;
            }
        }

        TutorialRect best = default;
        double bestOverlap = double.MaxValue;
        foreach (TutorialRect candidate in candidates)
        {
            TutorialRect inView = new(ClampX(candidate.Left, bubbleWidth, viewWidth), ClampY(candidate.Top, bubbleHeight, viewHeight), bubbleWidth, bubbleHeight);
            double overlap = OverlapArea(inView, lit);
            if (overlap < bestOverlap)
            {
                best = inView;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    private static bool Fits(TutorialRect r, double viewWidth, double viewHeight) =>
        r.Left >= Margin && r.Top >= Margin && r.Right <= viewWidth - Margin && r.Bottom <= viewHeight - Margin;

    // Math.Max last: a bubble wider than the window keeps its left edge in view, not its right.
    private static double ClampX(double left, double width, double viewWidth) => Math.Max(Margin, Math.Min(left, viewWidth - width - Margin));

    private static double ClampY(double top, double height, double viewHeight) => Math.Max(Margin, Math.Min(top, viewHeight - height - Margin));

    private static double OverlapArea(TutorialRect bubble, IReadOnlyList<TutorialRect> lit)
    {
        double total = 0;
        foreach (TutorialRect region in lit)
        {
            double w = Math.Min(bubble.Right, region.Right) - Math.Max(bubble.Left, region.Left);
            double h = Math.Min(bubble.Bottom, region.Bottom) - Math.Max(bubble.Top, region.Top);
            if (w > 0 && h > 0)
            {
                total += w * h;
            }
        }

        return total;
    }
}
