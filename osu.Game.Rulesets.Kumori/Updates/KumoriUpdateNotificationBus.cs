// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Kumori.Updates
{
    /// <summary>
    /// Bridges updater events from a background thread to whichever loaded Kumori icon currently
    /// owns access to osu!'s notification overlay. Messages are queued until that UI is ready.
    /// </summary>
    internal static class KumoriUpdateNotificationBus
    {
        private static readonly object sync = new object();
        private static readonly Queue<string> pending = new Queue<string>();
        private static Action<string>? sink;

        public static void Post(string message)
        {
            Action<string>? currentSink;

            lock (sync)
            {
                currentSink = sink;

                if (currentSink == null)
                {
                    pending.Enqueue(message);
                    return;
                }
            }

            currentSink(message);
        }

        public static IDisposable Attach(Action<string> notificationSink)
        {
            string[] queued;

            lock (sync)
            {
                sink = notificationSink;
                queued = pending.ToArray();
                pending.Clear();
            }

            foreach (string message in queued)
                notificationSink(message);

            return new Subscription(notificationSink);
        }

        internal static void ResetForTesting()
        {
            lock (sync)
            {
                sink = null;
                pending.Clear();
            }
        }

        private sealed class Subscription(Action<string> notificationSink) : IDisposable
        {
            public void Dispose()
            {
                lock (sync)
                {
                    if (ReferenceEquals(sink, notificationSink))
                        sink = null;
                }
            }
        }
    }
}
