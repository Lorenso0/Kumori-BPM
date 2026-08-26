// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Framework.IO.Stores;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Kumori.BPM;
using osu.Game.Rulesets.Kumori.Updates;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Configuration;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;
using osu.Game.Utils;
using KumoriBPMResolver = osu.Game.Rulesets.Kumori.BPM.BPMResolver;

namespace osu.Game.Rulesets.Kumori
{
    /// <summary>
    /// An official-client custom ruleset which delegates gameplay to the official osu! implementation.
    /// It deliberately does not inherit <see cref="OsuRuleset"/>, because doing so would also inherit
    /// osu!'s legacy online ID and collide with the built-in ruleset in the official client.
    /// </summary>
    public class KumoriRuleset : Ruleset
    {
        private const string tachyon_ruleset_api_version = "2026.818.0";

        private readonly OsuRuleset osu = new OsuRuleset();
        private readonly TosuCompatibilityIdentity tosuCompatibility = new TosuCompatibilityIdentity();

        public KumoriRuleset()
        {
            KumoriAutoUpdater.StartIfInstalled();
        }

        /// <summary>
        /// Stable and Tachyon currently expose different ruleset API versions. The stable NuGet package cannot
        /// reference Tachyon's new API members directly, so use the renamed variants property as a runtime feature
        /// check and report the matching version to the host ruleset loader.
        /// </summary>
        public override string RulesetAPIVersionSupported => ResolveRulesetApiVersion(typeof(Ruleset).GetProperty("GameplayVariants") != null);

        internal static string ResolveRulesetApiVersion(bool supportsGameplayVariants) =>
            supportsGameplayVariants ? tachyon_ruleset_api_version : CURRENT_RULESET_API_VERSION;

        public override string Description => "Kumori";

        public override string ShortName => "kumori";

        public override string PlayingVerb => osu.PlayingVerb;

        public override DrawableRuleset CreateDrawableRulesetWith(IBeatmap beatmap, IReadOnlyList<Mod>? mods = null) =>
            new TosuCompatibleDrawableOsuRuleset(this, beatmap, mods, tosuCompatibility);

        public override ScoreProcessor CreateScoreProcessor() => new TosuCompatibleOsuScoreProcessor(tosuCompatibility);

        public override HealthProcessor CreateHealthProcessor(double drainStartTime) => new TosuCompatibleOsuHealthProcessor(drainStartTime, tosuCompatibility);

        public override IBeatmapConverter CreateBeatmapConverter(IBeatmap beatmap) => new OsuBeatmapConverter(beatmap, this);

        public override IBeatmapProcessor CreateBeatmapProcessor(IBeatmap beatmap) => new OsuBeatmapProcessor(beatmap);

        public override IEnumerable<KeyBinding> GetDefaultKeyBindings(int variant = 0) => osu.GetDefaultKeyBindings(variant);

        public override IEnumerable<Mod> ConvertFromLegacyMods(LegacyMods mods) => osu.ConvertFromLegacyMods(mods);

        public override LegacyMods ConvertToLegacyMods(Mod[] mods) => osu.ConvertToLegacyMods(mods);

        public override IEnumerable<Mod> GetModsFor(ModType type)
        {
            IEnumerable<Mod> officialMods = osu.GetModsFor(type).Where(mod => mod.Acronym != "BPM");

            if (type != ModType.DifficultyIncrease)
                return officialMods;

            List<Mod> difficultyMods = officialMods.ToList();
            int hiddenIndex = difficultyMods.FindIndex(mod => mod is ModHidden
                                                        || mod is MultiMod multiMod && multiMod.Mods.Any(nestedMod => nestedMod is ModHidden));

            difficultyMods.Insert(hiddenIndex >= 0 ? hiddenIndex + 1 : difficultyMods.Count, new KumoriModBPMAdjust());
            return difficultyMods;
        }

        public override ScoreMultiplierCalculator CreateScoreMultiplierCalculator(ScoreMultiplierContext context) => osu.CreateScoreMultiplierCalculator(context);

        public override Drawable CreateIcon() => new KumoriRulesetIcon();

        public override IResourceStore<byte[]> CreateResourceStore()
        {
            var resources = new ResourceStore<byte[]>(new KumoriSelectionSampleStore());
            resources.AddStore(base.CreateResourceStore());
            resources.AddStore(osu.CreateResourceStore());
            return resources;
        }

        public override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) =>
            new KumoriDifficultyCalculator(RulesetInfo, new BPMPreparedWorkingBeatmap(beatmap));

        internal KumoriStarOnlyBatchCalculator CreateStarOnlyBatchCalculator(IWorkingBeatmap beatmap) =>
            new KumoriStarOnlyBatchCalculator(RulesetInfo, new BPMPreparedWorkingBeatmap(beatmap));

        public override PerformanceCalculator? CreatePerformanceCalculator() => osu.CreatePerformanceCalculator();

        public override HitObjectComposer CreateHitObjectComposer() => new OsuHitObjectComposer(this);

        public override IBeatmapVerifier CreateBeatmapVerifier() => osu.CreateBeatmapVerifier();

        public override RulesetSettingsSubsection CreateSettings() => new OsuSettingsSubsection(this);

        public override IRulesetConfigManager CreateConfig(SettingsStore? settings) => new OsuRulesetConfigManager(settings, RulesetInfo);

        public override IRulesetFilterCriteria CreateRulesetFilterCriteria()
        {
            Logger.Log("Kumori song-list filter attached");
            return new KumoriRulesetFilterCriteria();
        }

        public override ISkin? CreateSkinTransformer(ISkin skin, IBeatmap beatmap) => osu.CreateSkinTransformer(skin, beatmap);

        public override IConvertibleReplayFrame CreateConvertibleReplayFrame() => new OsuReplayFrame();

        public override IEnumerable<HitResult> GetValidHitResults() => osu.GetValidHitResults();

        public override LocalisableString GetDisplayNameForHitResult(HitResult result) => osu.GetDisplayNameForHitResult(result);

        public override StatisticItem[] CreateStatisticsForScore(ScoreInfo score, IBeatmap playableBeatmap) => osu.CreateStatisticsForScore(score, playableBeatmap);

        public override IEnumerable<Drawable> CreateEditorSetupSections() => osu.CreateEditorSetupSections();

        public override BeatmapDifficulty GetAdjustedDisplayDifficulty(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        {
            prepareBeatmapDependentMods(beatmapInfo, mods);

            var adjustedDifficulty = new BeatmapDifficulty(beatmapInfo.Difficulty);

            foreach (IApplicableToDifficulty mod in mods.OfType<IApplicableToDifficulty>())
                mod.ApplyToDifficulty(adjustedDifficulty);

            foreach (KumoriModBPMAdjust bpm in mods.OfType<KumoriModBPMAdjust>())
                bpm.ApplyDifficultyCompensation(adjustedDifficulty);

            // Match osu!'s displayed/effective AR and OD calculation after the raw difficulty
            // has been compensated. With scaling disabled, this resolves back to the exact DA
            // values selected by the user.
            double rate = ModUtils.CalculateRateWithMods(mods);
            double preempt = IBeatmapDifficultyInfo.DifficultyRange(adjustedDifficulty.ApproachRate, OsuHitObject.PREEMPT_RANGE) / rate;
            adjustedDifficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, OsuHitObject.PREEMPT_RANGE);

            double greatHitWindow = IBeatmapDifficultyInfo.DifficultyRange(adjustedDifficulty.OverallDifficulty, OsuHitWindows.GREAT_WINDOW_RANGE) / rate;
            adjustedDifficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatHitWindow, OsuHitWindows.GREAT_WINDOW_RANGE);

            return adjustedDifficulty;
        }

        public override IEnumerable<RulesetBeatmapAttribute> GetBeatmapAttributesForDisplay(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods)
        {
            prepareBeatmapDependentMods(beatmapInfo, mods);

            foreach (RulesetBeatmapAttribute attribute in base.GetBeatmapAttributesForDisplay(beatmapInfo, mods))
            {
                // Song select gives every statistic the width of the longest label. Compact stock
                // labels leave enough room for all seven values without triggering its tall fallback.
                yield return new RulesetBeatmapAttribute(attribute.Acronym, attribute.Acronym, attribute.OriginalValue, attribute.AdjustedValue, attribute.MaxValue)
                {
                    Description = attribute.Label,
                    AdditionalMetrics = attribute.AdditionalMetrics,
                    ValueFormat = attribute.ValueFormat,
                };
            }

            float originalStarRating = (float)beatmapInfo.StarRating;
            float originalBPM = (float)KumoriBPMResolver.FromBeatmapInfo(beatmapInfo);
            float playRate = (float)(mods.OfType<KumoriModBPMAdjust>().SingleOrDefault()?.SpeedChange.Value ?? 1);

            yield return new RulesetBeatmapAttribute("OG SR", "SR", originalStarRating, originalStarRating, 10);
            yield return new RulesetBeatmapAttribute("OG BPM", "BPM", originalBPM, originalBPM, 400);
            yield return new RulesetBeatmapAttribute("Rate ×", "RT", playRate, playRate, 2);
        }

        public override bool EditorShowScrollSpeed => osu.EditorShowScrollSpeed;

        private static void prepareBeatmapDependentMods(IBeatmapInfo beatmapInfo, IEnumerable<Mod> mods)
        {
            foreach (KumoriModBPMAdjust bpm in mods.OfType<KumoriModBPMAdjust>())
                bpm.SetSourceBPM(KumoriBPMResolver.FromBeatmapInfo(beatmapInfo));
        }

    }
}
