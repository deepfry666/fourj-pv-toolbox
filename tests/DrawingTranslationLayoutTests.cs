using System;
using StructuralToolbox.DrawingTranslation.Core;

internal static class DrawingTranslationLayoutTests
{
    private static int Main()
    {
        try
        {
            double height;
            Require(TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h * 3, 55, 0), null, out height)
                && height == 10, "free space should wrap without shrinking");
            Require(TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h * 3, 55, 0),
                new[] { new TextLayoutBounds(0, -40, 60, -27) }, out height)
                && height >= 7.5 && height < 9,
                "dense rows should fit above their neighbour with clearance");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h * 6, 55, 0),
                new[] { new TextLayoutBounds(0, -35, 60, -25) }, out height),
                "an unreadably small translation must be refused");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h, h * 12, 0), null, out height),
                "an unbreakable word must not escape the width constraint");
            Require(TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h * 3, 55, 0),
                new[] { new TextLayoutBounds(80, -40, 140, 0) }, out height)
                && height == 10, "another column must not force shrinking");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -50, 55, 0),
                new[] { new TextLayoutBounds(0, -40, 60, -27) }, out height),
                "absolute inline font sizes must be measured, not estimated");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, Double.NaN, 55, 0), null, out height)
                && !TranslationLayout.TryFit(10, Double.PositiveInfinity, 80,
                h => new TextLayoutBounds(0, -h, 55, 0), null, out height),
                "invalid measurements must fail closed");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -100, 55, 0), null, out height),
                "a long note must not grow without a height limit");
            Require(!TranslationLayout.TryFit(10, 60, 80,
                h => new TextLayoutBounds(0, -h * 4, 55, 0),
                new[] { new TextLayoutBounds(-10, -25, 80, -25) }, out height),
                "a zero-height drawing edge must constrain text growth");
            Console.WriteLine("PASS drawing translation layout: 9 scenarios");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
