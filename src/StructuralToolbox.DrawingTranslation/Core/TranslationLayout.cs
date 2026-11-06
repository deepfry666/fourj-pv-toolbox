using System;
using System.Collections.Generic;

namespace StructuralToolbox.DrawingTranslation.Core
{
    // Coordinates are in the source text's own plane, never a world-axis box.
    internal struct TextLayoutBounds
    {
        internal readonly double Left, Bottom, Right, Top;

        internal TextLayoutBounds(double left, double bottom,
            double right, double top)
        {
            Left = left; Bottom = bottom; Right = right; Top = top;
        }

        internal double Width { get { return Right - Left; } }
        internal double Height { get { return Top - Bottom; } }
        internal bool IsValid
        {
            get
            {
                return Finite(Left) && Finite(Bottom) && Finite(Right) &&
                    Finite(Top) && Width >= 0 && Height >= 0;
            }
        }

        internal bool Intersects(TextLayoutBounds other, double clearance)
        {
            return Left < other.Right + clearance &&
                Right > other.Left - clearance &&
                Bottom < other.Top + clearance &&
                Top > other.Bottom - clearance;
        }

        internal static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }

    internal static class TranslationLayout
    {
        internal const double MinimumHeightRatio = 0.75;
        internal const double ClearanceRatio = 0.15;
        internal const double MaximumLineHeights = 8;

        // Measurement is supplied by the host: it must include formatting,
        // font metrics, paragraph breaks, attachment and unbreakable words.
        internal static bool TryFit(double sourceHeight, double width,
            double maximumHeight, Func<double, TextLayoutBounds> measure,
            IList<TextLayoutBounds> occupied, out double fittedHeight)
        {
            fittedHeight = sourceHeight;
            if (!TextLayoutBounds.Finite(sourceHeight) || sourceHeight <= 0 ||
                !TextLayoutBounds.Finite(width) || width <= 0 ||
                !TextLayoutBounds.Finite(maximumHeight) || maximumHeight <= 0)
                return false;
            double tolerance = sourceHeight * 0.001;
            for (int step = 0; step <= 5; step++)
            {
                double height = sourceHeight *
                    (1.0 - (1.0 - MinimumHeightRatio) * step / 5);
                TextLayoutBounds bounds = measure(height);
                if (!bounds.IsValid || bounds.Width <= 0 || bounds.Height <= 0 ||
                    bounds.Width > width + tolerance ||
                    bounds.Height > maximumHeight + tolerance)
                    continue;
                bool collision = false;
                if (occupied != null)
                    foreach (TextLayoutBounds obstacle in occupied)
                        if (obstacle.IsValid && bounds.Intersects(obstacle,
                            sourceHeight * ClearanceRatio))
                        {
                            collision = true;
                            break;
                        }
                if (collision) continue;
                fittedHeight = height;
                return true;
            }
            return false;
        }
    }
}
