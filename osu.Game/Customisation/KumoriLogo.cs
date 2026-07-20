// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Screens.Menu;
using osu.Game.Seasonal;

namespace osu.Game.Customisation
{
    /// <summary>
    /// The Kumori-branded main menu logo.
    /// </summary>
    public partial class KumoriLogo : OsuLogo
    {
        protected override string LogoTextureName => @"Menu/kumori-logo";
    }

    /// <summary>
    /// The seasonal main menu logo with Kumori branding.
    /// </summary>
    public partial class KumoriLogoChristmas : OsuLogoChristmas
    {
        protected override string LogoTextureName => @"Menu/kumori-logo";
    }
}
