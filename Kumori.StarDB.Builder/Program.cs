// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Kumori.StarDB.Builder;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
            return runHeadless(args);

        string input = getArgument(args, "--input", required: false);
        string output = getArgument(args, "--output", required: false);
        int? workers = int.TryParse(getArgument(args, "--workers", required: false), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWorkers)
            ? parsedWorkers
            : null;
        bool autoStart = args.Contains("--autostart", StringComparer.OrdinalIgnoreCase);
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(input, output, workers, autoStart));
        return 0;
    }

    private static int runHeadless(string[] args)
    {
        try
        {
            string input = getArgument(args, "--input");
            string output = getArgument(args, "--output");
            int workers = int.TryParse(getArgument(args, "--workers", required: false), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWorkers)
                ? parsedWorkers
                : Math.Max(1, Environment.ProcessorCount - 1);
            var request = new BuilderRequest(input, output, Math.Clamp(workers, 1, Environment.ProcessorCount));
            BuilderResult result = new StarDatabaseBuilder().Run(request, new ConsoleProgress(), default);
            Console.WriteLine($"COMPLETE database={result.DatabasePath} maps={result.Total:N0} calculated={result.Calculated:N0} reused={result.Reused:N0} failed={result.Failed:N0}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string getArgument(string[] args, string name, bool required = true)
    {
        int index = Array.FindIndex(args, argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            return args[index + 1];

        if (required)
            throw new ArgumentException($"Missing required argument {name}.");

        return string.Empty;
    }

    private sealed class ConsoleProgress : IProgress<BuilderProgress>
    {
        public void Report(BuilderProgress value)
        {
            string progress = value.Total > 0 ? $" {value.Completed:N0}/{value.Total:N0}" : string.Empty;
            Console.WriteLine($"{value.Stage}{progress}: {value.Message}");
        }
    }
}
