// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;
using System.Globalization;
using osu.Game.Utils;

namespace osu.Game.Screens.Select
{
    public enum BPMStarRatingFilterMode
    {
        [Description("Disabled")]
        Disabled,

        [Description("Star rating pre-mod")]
        PreMod,

        [Description("Star rating post-mod")]
        PostMod,
    }

    public static class BPMStarRatingFilter
    {
        public static FilterCriteria.OptionalRange<double> CreateRange(string minimum, string maximum) => new FilterCriteria.OptionalRange<double>
        {
            Min = parseBound(minimum),
            Max = parseBound(maximum),
            IsLowerInclusive = true,
            IsUpperInclusive = true,
        };

        public static double ToDisplayedRating(double rating) => rating.FloorToDecimalDigits(2);

        public static bool TryParseBound(string text, out double value)
        {
            bool parsed = double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return parsed && double.IsFinite(value) && value >= 0;
        }

        private static double? parseBound(string text) => TryParseBound(text, out double value) ? value : null;
    }
}
