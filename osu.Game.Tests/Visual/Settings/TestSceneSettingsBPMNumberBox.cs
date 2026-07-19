// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;

namespace osu.Game.Tests.Visual.Settings
{
    public partial class TestSceneSettingsBPMNumberBox : OsuTestScene
    {
        private SettingsBPMNumberBox numberBox = null!;
        private OsuTextBox textBox = null!;
        private RoundedSliderBar<double> slider = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("clear BPM presets", () => config.SetValue(OsuSetting.BPMAdjustPresets, string.Empty));
            AddStep("create BPM number box", () => Child = numberBox = new SettingsBPMNumberBox());
            AddStep("get inner text box", () => textBox = numberBox.ChildrenOfType<OsuTextBox>().Single());
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

        private RoundedButton getButton(string text) =>
            numberBox.ChildrenOfType<RoundedButton>().Single(button => button.Text.ToString() == text);
    }
}
