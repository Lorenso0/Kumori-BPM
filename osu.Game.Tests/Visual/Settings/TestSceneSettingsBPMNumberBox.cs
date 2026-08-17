// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.Visual.Settings
{
    public partial class TestSceneSettingsBPMNumberBox : OsuTestScene
    {
        [Cached]
        private readonly BPMStarRatingCalculationController calculationController = new BPMStarRatingCalculationController();

        private SettingsBPMNumberBox numberBox = null!;
        private OsuTextBox textBox = null!;
        private RoundedSliderBar<double> slider = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("clear BPM presets", () =>
            {
                config.SetValue(OsuSetting.BPMAdjustPresets, string.Empty);
                BPMPresetStore.Save(LocalStorage, Array.Empty<BPMPreset>());
            });
            AddStep("clear star rating filter", () =>
            {
                config.SetValue(OsuSetting.BPMStarRatingFilterMode, BPMStarRatingFilterMode.Disabled);
                config.SetValue(OsuSetting.BPMStarRatingFilterMinimum, string.Empty);
                config.SetValue(OsuSetting.BPMStarRatingFilterMaximum, string.Empty);
            });
            AddStep("create BPM number box", () => Child = numberBox = new SettingsBPMNumberBox());
            AddStep("get target BPM text box", () => textBox = numberBox.ChildrenOfType<OsuTextBox>().Single(box => box.Name == "Target BPM input"));
            AddStep("get BPM slider", () => slider = numberBox.ChildrenOfType<RoundedSliderBar<double>>().Single());
        }

        [Test]
        public void TestSliderRangeAndTextSynchronisation()
        {
            AddStep("move slider below range", () => slider.Current.Value = 0);
            AddAssert("slider minimum is 140", () => slider.Current.Value, () => Is.EqualTo(140));
            AddStep("move slider above range", () => slider.Current.Value = 1000);
            AddAssert("slider maximum is 320", () => slider.Current.Value, () => Is.EqualTo(320));
            AddStep("move slider to 220", () => slider.Current.Value = 220);
            AddAssert("slider updates target", () => numberBox.Current.Value, () => Is.EqualTo(220));
            AddAssert("slider updates text", () => textBox.Text, () => Is.EqualTo("220"));

            AddStep("type target below slider", () => textBox.Text = "100");
            AddAssert("typed low target remains valid", () => numberBox.Current.Value, () => Is.EqualTo(100));
            AddAssert("slider clamps visually to minimum", () => slider.Current.Value, () => Is.EqualTo(140));

            AddStep("type target above slider", () => textBox.Text = "400");
            AddAssert("typed high target remains valid", () => numberBox.Current.Value, () => Is.EqualTo(400));
            AddAssert("slider clamps visually to maximum", () => slider.Current.Value, () => Is.EqualTo(320));
        }

        [Test]
        public void TestZeroAndEmptyAreNeutral()
        {
            AddStep("set target to 180", () => numberBox.Current.Value = 180);
            AddStep("enter zero", () => textBox.Text = "0");
            AddAssert("zero becomes empty target", () => numberBox.Current.Value, () => Is.Null);
            AddAssert("zero remains editable", () => textBox.Text, () => Is.EqualTo("0"));
            AddStep("clear field", () => textBox.Text = string.Empty);
            AddAssert("empty field remains neutral", () => numberBox.Current.Value, () => Is.Null);
            AddStep("enter decimal target", () => textBox.Text = "174.5");
            AddAssert("decimal text retained", () => textBox.Text, () => Is.EqualTo("174.5"));
            AddAssert("decimal target accepted", () => numberBox.Current.Value, () => Is.EqualTo(174.5));
        }

        [Test]
        public void TestSaveAndApplyPreset()
        {
            AddStep("set target to 174.5", () => numberBox.Current.Value = 174.5);
            AddStep("save preset", () => getButton("+ SAVE").TriggerClick());
            AddAssert("preset persisted as versioned JSON", () =>
            {
                var presets = BPMPresetStore.Deserialise(config.Get<string>(OsuSetting.BPMAdjustPresets));
                return presets.Count == 1 && presets[0].BPM == 174.5 && presets[0].Name == "174.5 BPM";
            });
            AddAssert("preset button shown", () => getButton("174.5 BPM").IsPresent);
            AddStep("set a different target", () => numberBox.Current.Value = 200);
            AddStep("apply preset", () => getButton("174.5 BPM").TriggerClick());
            AddAssert("saved BPM applied", () => numberBox.Current.Value, () => Is.EqualTo(174.5));
        }

        [Test]
        public void TestLegacyPresetMigration()
        {
            AddStep("load legacy presets", () => config.SetValue(OsuSetting.BPMAdjustPresets, "120;174.5;200"));
            AddAssert("legacy buttons shown", () => getButton("120 BPM").IsPresent
                                                    && getButton("174.5 BPM").IsPresent
                                                    && getButton("200 BPM").IsPresent);
            AddStep("set and save another target", () => numberBox.Current.Value = 160);
            AddStep("save preset", () => getButton("+ SAVE").TriggerClick());
            AddAssert("migrated to JSON", () => config.Get<string>(OsuSetting.BPMAdjustPresets).StartsWith("{\"version\":1", StringComparison.Ordinal));
        }

        [Test]
        public void TestStarRatingFilterSettings()
        {
            OsuEnumDropdown<BPMStarRatingFilterMode> mode = null!;
            Menu menu = null!;
            OsuSpriteText filterLabel = null!;
            OsuTextBox minimum = null!;
            OsuTextBox maximum = null!;

            AddStep("get star rating controls", () =>
            {
                mode = numberBox.ChildrenOfType<OsuEnumDropdown<BPMStarRatingFilterMode>>().Single();
                menu = mode.ChildrenOfType<Menu>().Single();
                filterLabel = numberBox.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == "Show only maps:");
                minimum = numberBox.ChildrenOfType<OsuTextBox>().Single(box => box.PlaceholderText.ToString() == "Min");
                maximum = numberBox.ChildrenOfType<OsuTextBox>().Single(box => box.PlaceholderText.ToString() == "Max");
            });
            AddAssert("setting draws above following controls", () => numberBox.Depth, () => Is.LessThan(0));
            AddAssert("dropdown menu is constrained", () => menu.MaxHeight, () => Is.EqualTo(100));
            AddAssert("filter label has correct tooltip", () => ((IHasTooltip)filterLabel).TooltipText.ToString(), () => Does.Contain("original rating"));
            AddAssert("filter dropdown has correct tooltip", () => ((IHasTooltip)mode).TooltipText.ToString(), () => Does.Contain("after BPM Adjust"));
            AddAssert("filter range has correct tooltip", () => ((IHasTooltip)minimum).TooltipText.ToString(), () => Does.Contain("inclusive star range"));
            AddStep("select post-mod filter", () => mode.Current.Value = BPMStarRatingFilterMode.PostMod);
            AddAssert("calculate maps button shown", () => getButton("Calculate maps").IsPresent);
            bool calculationRequested = false;
            AddStep("bind calculation request", () => calculationController.CalculateRequested += () => calculationRequested = true);
            AddStep("request map calculation", () => getButton("Calculate maps").TriggerClick());
            AddAssert("calculation request sent", () => calculationRequested);
            AddStep("begin loading saved profile", () => calculationController.BeginLoading());
            AddAssert("loading saved profile shown", () => numberBox.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == "Loading saved maps…"));
            AddAssert("calculate disabled while loading", () => getButton("Calculate maps").Enabled.Value, () => Is.False);
            AddStep("complete saved profile load", () => calculationController.Complete(123));
            AddAssert("cached map count shown", () => numberBox.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == "123 maps cached"));
            AddStep("enter inclusive range", () =>
            {
                minimum.Text = "4.99";
                maximum.Text = "5.99";
            });
            AddAssert("mode persisted", () => config.Get<BPMStarRatingFilterMode>(OsuSetting.BPMStarRatingFilterMode), () => Is.EqualTo(BPMStarRatingFilterMode.PostMod));
            AddAssert("minimum persisted", () => config.Get<string>(OsuSetting.BPMStarRatingFilterMinimum), () => Is.EqualTo("4.99"));
            AddAssert("maximum persisted", () => config.Get<string>(OsuSetting.BPMStarRatingFilterMaximum), () => Is.EqualTo("5.99"));
        }

        private RoundedButton getButton(string text) =>
            numberBox.ChildrenOfType<RoundedButton>().Single(button => button.Text.ToString() == text);
    }
}
