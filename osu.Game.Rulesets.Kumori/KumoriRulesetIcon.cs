// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Kumori
{
    /// <summary>
    /// A compact version of Kumori's ringed wordmark, designed to remain legible in osu!'s
    /// smallest ruleset-icon placements.
    /// </summary>
    public partial class KumoriRulesetIcon : CompositeDrawable
    {
        public KumoriRulesetIcon()
        {
            Size = new Vector2(32);

            InternalChildren =
            [
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(30),
                    Masking = true,
                    BorderThickness = 2.5f,
                    BorderColour = Color4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "K",
                    Y = -1,
                    Font = OsuFont.TorusAlternate.With(size: 22, weight: FontWeight.Bold),
                },
            ];
        }
    }
}
