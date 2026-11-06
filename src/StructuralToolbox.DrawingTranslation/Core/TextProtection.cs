using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StructuralToolbox.DrawingTranslation.Core
{
    public static class TextProtection
    {
        private const string Number =
            @"[+\-−±]?(?:[0-9]+(?:[.,][0-9]+)*|\.[0-9]+)(?:[eE][+\-−]?[0-9]+)?";
        private const string Unit =
            @"(?:mm|cm|km|m|kN|N|GPa|MPa|kPa|Pa|MWp|kWp|Wp|MW|kW|W|kV|V|mA|A|kHz|Hz|kg|g|t|s|min|h|°C|℃|°|度|%(?!%))" +
            @"(?:\^?[+\-]?[0-9]+|[²³])?(?![A-Za-z])";
        private const string NumericParameter =
            @"(?<![0-9.])(?:(?:[<>]=?|[=≤≥≈≠])\s*)?" + Number +
            @"(?:\s*[-~～–—±×xX*/:]\s*" + Number + @")*" +
            @"(?:\s*" + Unit + @"(?:\s*[/·*]\s*" + Unit + @")*)?";

        private static readonly Regex ProtectedPattern = new Regex(
            @"%<[^%>]*>%|%%[A-Za-z0-9]|\\p[^;{}\\]*;|\\(?:U\+[0-9A-Fa-f]{4}|[~\\{}PLlOoKk])|\\(?:W|w|H|h|S|s|A|a|C|c|F|f|T|t|Q|q)[^;{}]*;?|[{}]|(?<![A-Za-z])(?:[A-Z]{1,6}[-/]?[0-9]+[A-Za-z0-9./+\-×xX*]*)|" + NumericParameter,
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static ProtectedText Protect(string input)
        {
            string value = input ?? String.Empty;
            List<string> tokens = new List<string>();
            string template = ProtectedPattern.Replace(value,
                delegate(Match match)
                {
                    int index = tokens.Count;
                    tokens.Add(match.Value);
                    return ProtectedText.TokenName(index);
                });
            return new ProtectedText(template, tokens);
        }

        public static string Restore(string translated,
            ProtectedText protection)
        {
            if (protection == null)
                return translated ?? String.Empty;
            return protection.Restore(translated);
        }
    }
}
