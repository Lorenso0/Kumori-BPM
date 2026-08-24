// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using osu.Framework.Platform;

namespace osu.Game.Rulesets.Kumori.BPM
{
    /// <summary>
    /// Single-DLL SQLite persistence using the SQLite library included with supported Windows versions.
    /// This deliberately avoids managed/native NuGet runtime dependencies that osu!'s external-ruleset
    /// loader would otherwise have to discover beside the ruleset DLL.
    /// </summary>
    internal static class KumoriStarRatingDatabase
    {
        public const string Filename = "kumori-star-ratings.db";

        private const int schema_version = 1;
        private static readonly object databaseLock = new object();
        private static readonly HashSet<string> initialisedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool TryLoadProfile(Storage storage, string profileKey, out KumoriPersistedStarRatingProfile profile)
        {
            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                using var profileStatement = connection.Prepare("SELECT complete FROM profiles WHERE profile_key = ?");
                profileStatement.BindText(1, profileKey);

                if (!profileStatement.StepRow())
                {
                    profile = default;
                    return false;
                }

                bool complete = profileStatement.ColumnInt64(0) != 0;
                var ratings = new Dictionary<string, double>(StringComparer.Ordinal);
                var unavailable = new HashSet<string>(StringComparer.Ordinal);

                using var ratingsStatement = connection.Prepare("SELECT beatmap_key, rating, unavailable FROM ratings WHERE profile_key = ?");
                ratingsStatement.BindText(1, profileKey);

                while (ratingsStatement.StepRow())
                {
                    string beatmapKey = ratingsStatement.ColumnText(0);

                    if (ratingsStatement.ColumnInt64(2) != 0)
                        unavailable.Add(beatmapKey);
                    else if (!ratingsStatement.ColumnIsNull(1))
                        ratings[beatmapKey] = ratingsStatement.ColumnDouble(1);
                }

                profile = new KumoriPersistedStarRatingProfile(ratings, unavailable, complete);
                return true;
            }
        }

        public static void EnsureProfiles(Storage storage, IEnumerable<string> profileKeys)
        {
            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                connection.Transaction(() =>
                {
                    using var statement = connection.Prepare("""
                                                             INSERT INTO profiles(profile_key, complete, updated_utc)
                                                             VALUES(?, 0, ?)
                                                             ON CONFLICT(profile_key) DO NOTHING
                                                             """);
                    string updated = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    foreach (string profileKey in profileKeys)
                    {
                        statement.BindText(1, profileKey);
                        statement.BindText(2, updated);
                        statement.Execute();
                        statement.Reset();
                    }
                });
            }
        }

        public static HashSet<string> GetFullyIndexedBeatmapKeys(Storage storage, IReadOnlyCollection<string> profileKeys)
        {
            var completed = new HashSet<string>(StringComparer.Ordinal);

            if (profileKeys.Count == 0)
                return completed;

            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                string placeholders = string.Join(",", Enumerable.Repeat("?", profileKeys.Count));
                using var statement = connection.Prepare($"""
                                                          SELECT beatmap_key
                                                          FROM ratings
                                                          WHERE profile_key IN ({placeholders})
                                                          GROUP BY beatmap_key
                                                          HAVING COUNT(*) = ?
                                                          """);
                int parameter = 1;

                foreach (string profileKey in profileKeys)
                    statement.BindText(parameter++, profileKey);

                statement.BindInt(parameter, profileKeys.Count);

                while (statement.StepRow())
                    completed.Add(statement.ColumnText(0));
            }

            return completed;
        }

        public static void WriteBatch(Storage storage, IReadOnlyCollection<KumoriStarRatingDatabaseWrite> writes)
        {
            if (writes.Count == 0)
                return;

            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                connection.Transaction(() =>
                {
                    using var statement = connection.Prepare("""
                                                             INSERT INTO ratings(profile_key, beatmap_key, rating, unavailable)
                                                             VALUES(?, ?, ?, ?)
                                                             ON CONFLICT(profile_key, beatmap_key) DO UPDATE SET
                                                                 rating = excluded.rating,
                                                                 unavailable = excluded.unavailable
                                                             """);

                    foreach (KumoriStarRatingDatabaseWrite write in writes)
                    {
                        statement.BindText(1, write.ProfileKey);
                        statement.BindText(2, write.BeatmapKey);

                        if (write.Rating.HasValue)
                            statement.BindDouble(3, write.Rating.Value);
                        else
                            statement.BindNull(3);

                        statement.BindInt(4, write.Unavailable ? 1 : 0);
                        statement.Execute();
                        statement.Reset();
                    }

                    using var updateProfiles = connection.Prepare("UPDATE profiles SET updated_utc = ? WHERE profile_key = ?");
                    string updated = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    foreach (string profileKey in writes.Select(write => write.ProfileKey).Distinct(StringComparer.Ordinal))
                    {
                        updateProfiles.BindText(1, updated);
                        updateProfiles.BindText(2, profileKey);
                        updateProfiles.Execute();
                        updateProfiles.Reset();
                    }
                });
            }
        }

        public static void SetProfilesComplete(Storage storage, IEnumerable<string> profileKeys, bool complete)
        {
            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                connection.Transaction(() =>
                {
                    using var statement = connection.Prepare("UPDATE profiles SET complete = ?, updated_utc = ? WHERE profile_key = ?");
                    string updated = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    foreach (string profileKey in profileKeys)
                    {
                        statement.BindInt(1, complete ? 1 : 0);
                        statement.BindText(2, updated);
                        statement.BindText(3, profileKey);
                        statement.Execute();
                        statement.Reset();
                    }
                });

                if (complete)
                    connection.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
            }
        }

        public static bool DeleteProfiles(Storage storage, IReadOnlyCollection<string> profileKeys, bool compact)
        {
            if (profileKeys.Count == 0)
                return false;

            lock (databaseLock)
            {
                using DatabaseConnection connection = open(storage);
                bool deleted = false;

                connection.Transaction(() =>
                {
                    using var statement = connection.Prepare("DELETE FROM profiles WHERE profile_key = ?");

                    foreach (string profileKey in profileKeys)
                    {
                        statement.BindText(1, profileKey);
                        statement.Execute();
                        deleted |= connection.Changes > 0;
                        statement.Reset();
                    }
                });

                if (deleted && compact)
                {
                    connection.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
                    connection.Execute("VACUUM");
                }

                return deleted;
            }
        }

        private static DatabaseConnection open(Storage storage)
        {
            string path = storage.GetFullPath(Filename, true);
            var connection = new DatabaseConnection(path);
            connection.Execute("PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 10000; PRAGMA foreign_keys = ON");

            if (initialisedPaths.Contains(path))
                return connection;

            connection.Execute("""
                               PRAGMA journal_mode = WAL;

                               CREATE TABLE IF NOT EXISTS profiles(
                                   profile_key TEXT PRIMARY KEY,
                                   complete INTEGER NOT NULL DEFAULT 0,
                                   updated_utc TEXT NOT NULL
                               ) WITHOUT ROWID;

                               CREATE TABLE IF NOT EXISTS ratings(
                                   profile_key TEXT NOT NULL,
                                   beatmap_key TEXT NOT NULL,
                                   rating REAL NULL,
                                   unavailable INTEGER NOT NULL DEFAULT 0,
                                   PRIMARY KEY(profile_key, beatmap_key),
                                   FOREIGN KEY(profile_key) REFERENCES profiles(profile_key) ON DELETE CASCADE
                               ) WITHOUT ROWID;
                               """);

            using var version = connection.Prepare("PRAGMA user_version");

            if (!version.StepRow())
                throw new InvalidDataException("Could not read the Kumori star database schema version.");

            long existingVersion = version.ColumnInt64(0);

            if (existingVersion > schema_version)
                throw new InvalidDataException($"Kumori star database schema {existingVersion} is newer than supported schema {schema_version}.");

            version.Dispose();

            if (existingVersion < schema_version)
                connection.Execute($"PRAGMA user_version = {schema_version}");

            initialisedPaths.Add(path);
            return connection;
        }

        private sealed class DatabaseConnection : IDisposable
        {
            private IntPtr handle;

            public DatabaseConnection(string path)
            {
                int result = Native.sqlite3_open_v2(path, out handle, Native.SQLITE_OPEN_READWRITE | Native.SQLITE_OPEN_CREATE | Native.SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);

                if (result != Native.SQLITE_OK)
                {
                    string message = getErrorMessage();
                    Dispose();
                    throw new InvalidDataException($"Could not open Kumori star database: {message} ({result}).");
                }
            }

            public Statement Prepare(string sql)
            {
                int result = Native.sqlite3_prepare_v2(handle, sql, -1, out IntPtr statement, IntPtr.Zero);
                Check(result);
                return new Statement(this, statement);
            }

            public void Execute(string sql)
            {
                int result = Native.sqlite3_exec(handle, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error);

                if (result == Native.SQLITE_OK)
                    return;

                string message = error == IntPtr.Zero ? getErrorMessage() : Marshal.PtrToStringUTF8(error) ?? "Unknown SQLite error";

                if (error != IntPtr.Zero)
                    Native.sqlite3_free(error);

                throw new InvalidDataException($"Kumori star database command failed: {message} ({result}).");
            }

            public void Transaction(Action action)
            {
                Execute("BEGIN IMMEDIATE");

                try
                {
                    action();
                    Execute("COMMIT");
                }
                catch
                {
                    try
                    {
                        Execute("ROLLBACK");
                    }
                    catch
                    {
                    }

                    throw;
                }
            }

            public void Check(int result)
            {
                if (result != Native.SQLITE_OK && result != Native.SQLITE_ROW && result != Native.SQLITE_DONE)
                    throw new InvalidDataException($"Kumori star database operation failed: {getErrorMessage()} ({result}).");
            }

            public int Changes => Native.sqlite3_changes(handle);

            private string getErrorMessage() => handle == IntPtr.Zero ? "Unknown SQLite error" : Marshal.PtrToStringUTF8(Native.sqlite3_errmsg(handle)) ?? "Unknown SQLite error";

            public void Dispose()
            {
                if (handle == IntPtr.Zero)
                    return;

                Native.sqlite3_close_v2(handle);
                handle = IntPtr.Zero;
            }
        }

        private sealed class Statement : IDisposable
        {
            private readonly DatabaseConnection connection;
            private IntPtr handle;

            public Statement(DatabaseConnection connection, IntPtr handle)
            {
                this.connection = connection;
                this.handle = handle;
            }

            public void BindText(int parameter, string value) => connection.Check(Native.sqlite3_bind_text(handle, parameter, value, -1, new IntPtr(-1)));

            public void BindInt(int parameter, int value) => connection.Check(Native.sqlite3_bind_int(handle, parameter, value));

            public void BindDouble(int parameter, double value) => connection.Check(Native.sqlite3_bind_double(handle, parameter, value));

            public void BindNull(int parameter) => connection.Check(Native.sqlite3_bind_null(handle, parameter));

            public bool StepRow()
            {
                int result = Native.sqlite3_step(handle);

                if (result == Native.SQLITE_ROW)
                    return true;

                connection.Check(result);
                return false;
            }

            public void Execute()
            {
                int result = Native.sqlite3_step(handle);

                if (result != Native.SQLITE_DONE)
                    connection.Check(result);
            }

            public void Reset()
            {
                connection.Check(Native.sqlite3_reset(handle));
                connection.Check(Native.sqlite3_clear_bindings(handle));
            }

            public bool ColumnIsNull(int column) => Native.sqlite3_column_type(handle, column) == Native.SQLITE_NULL;

            public long ColumnInt64(int column) => Native.sqlite3_column_int64(handle, column);

            public double ColumnDouble(int column) => Native.sqlite3_column_double(handle, column);

            public string ColumnText(int column)
            {
                IntPtr text = Native.sqlite3_column_text(handle, column);
                int length = Native.sqlite3_column_bytes(handle, column);
                return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(text, length) ?? string.Empty;
            }

            public void Dispose()
            {
                if (handle == IntPtr.Zero)
                    return;

                Native.sqlite3_finalize(handle);
                handle = IntPtr.Zero;
            }
        }

        private static class Native
        {
            public const int SQLITE_OK = 0;
            public const int SQLITE_ROW = 100;
            public const int SQLITE_DONE = 101;
            public const int SQLITE_NULL = 5;
            public const int SQLITE_OPEN_READWRITE = 0x00000002;
            public const int SQLITE_OPEN_CREATE = 0x00000004;
            public const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags, IntPtr vfs);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_close_v2(IntPtr database);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr sqlite3_errmsg(IntPtr database);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_changes(IntPtr database);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern void sqlite3_free(IntPtr memory);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_finalize(IntPtr statement);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_step(IntPtr statement);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_reset(IntPtr statement);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_clear_bindings(IntPtr statement);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_bind_text(IntPtr statement, int parameter, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_bind_int(IntPtr statement, int parameter, int value);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_bind_double(IntPtr statement, int parameter, double value);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_bind_null(IntPtr statement, int parameter);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_column_type(IntPtr statement, int column);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern long sqlite3_column_int64(IntPtr statement, int column);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern double sqlite3_column_double(IntPtr statement, int column);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr sqlite3_column_text(IntPtr statement, int column);

            [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_column_bytes(IntPtr statement, int column);
        }
    }

    internal readonly record struct KumoriStarRatingDatabaseWrite(string ProfileKey, string BeatmapKey, double? Rating, bool Unavailable);

    internal readonly record struct KumoriPersistedStarRatingProfile(Dictionary<string, double> Ratings, HashSet<string> UnavailableBeatmaps, bool Complete);
}
