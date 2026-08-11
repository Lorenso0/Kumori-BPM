// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online.API
{
    /// <summary>
    /// Marks requests which create or submit an official score.
    /// Custom builds block these at the API boundary as a second line of defence.
    /// </summary>
    public interface IScoreSubmissionRequest
    {
    }
}
