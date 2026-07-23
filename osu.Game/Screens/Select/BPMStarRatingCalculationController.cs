// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Screens.Select
{
    public sealed class BPMStarRatingCalculationController
    {
        public BPMStarRatingCalculationState State { get; private set; }
        public int CompletedMaps { get; private set; }
        public int TotalMaps { get; private set; }

        public event Action? CalculateRequested;
        public event Action? CancelRequested;
        public event Action? ProgressChanged;

        public void RequestCalculation() => CalculateRequested?.Invoke();

        public void RequestCancellation() => CancelRequested?.Invoke();

        internal void BeginLoading()
        {
            State = BPMStarRatingCalculationState.Loading;
            CompletedMaps = 0;
            TotalMaps = 0;
            ProgressChanged?.Invoke();
        }

        internal void Begin(int totalMaps)
        {
            State = BPMStarRatingCalculationState.Calculating;
            CompletedMaps = 0;
            TotalMaps = totalMaps;
            ProgressChanged?.Invoke();
        }

        internal void ReportProgress(int completedMaps)
        {
            CompletedMaps = Math.Clamp(completedMaps, 0, TotalMaps);
            ProgressChanged?.Invoke();
        }

        internal void Complete(int? totalMaps = null)
        {
            State = BPMStarRatingCalculationState.Completed;

            if (totalMaps.HasValue)
                TotalMaps = totalMaps.Value;

            CompletedMaps = TotalMaps;
            ProgressChanged?.Invoke();
        }

        internal void Reset()
        {
            State = BPMStarRatingCalculationState.Idle;
            CompletedMaps = 0;
            TotalMaps = 0;
            ProgressChanged?.Invoke();
        }
    }

    public enum BPMStarRatingCalculationState
    {
        Idle,
        Loading,
        Calculating,
        Completed,
    }
}
