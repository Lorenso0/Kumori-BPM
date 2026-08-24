// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kumori.StarDB.Builder;

internal sealed class MainForm : Form
{
    private readonly TextBox inputPath = new TextBox();
    private readonly TextBox outputPath = new TextBox();
    private readonly NumericUpDown workers = new NumericUpDown();
    private readonly Button inputBrowse = new Button();
    private readonly Button outputBrowse = new Button();
    private readonly Button startButton = new Button();
    private readonly Button pauseButton = new Button();
    private readonly Button openFolderButton = new Button();
    private readonly ProgressBar progressBar = new ProgressBar();
    private readonly Label status = new Label();
    private readonly Label detail = new Label();
    private readonly TextBox log = new TextBox();
    private CancellationTokenSource? cancellation;
    private bool closeWhenStopped;
    private readonly string? initialInput;
    private readonly string? initialOutput;
    private readonly int? initialWorkers;
    private readonly bool autoStart;

    public MainForm(string? initialInput = null, string? initialOutput = null, int? initialWorkers = null, bool autoStart = false)
    {
        this.initialInput = initialInput;
        this.initialOutput = initialOutput;
        this.initialWorkers = initialWorkers;
        this.autoStart = autoStart;
        Text = "Kumori Star Database Builder";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 600);
        ClientSize = new Size(820, 620);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 9,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            AutoSize = true,
            Text = "Complete star database builder",
            Font = new Font("Segoe UI Semibold", 20F),
            ForeColor = Color.FromArgb(28, 35, 48),
            Margin = new Padding(0, 0, 0, 4),
        };
        var subtitle = new Label
        {
            AutoSize = true,
            Text = "220–270 BPM  •  Stat scaling off  •  DA AR10 / HP0  •  51 exact profiles",
            ForeColor = Color.FromArgb(80, 89, 105),
            Margin = new Padding(0, 0, 0, 20),
        };

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(createPathRow("Lazer data folder", inputPath, inputBrowse, chooseInput), 0, 2);
        root.Controls.Add(createPathRow("Database output folder", outputPath, outputBrowse, chooseOutput), 0, 3);
        root.Controls.Add(createOptionsRow(), 0, 4);
        root.Controls.Add(createProgressPanel(), 0, 5);

        log.Dock = DockStyle.Fill;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.BackColor = Color.White;
        log.ForeColor = Color.FromArgb(67, 74, 88);
        log.BorderStyle = BorderStyle.FixedSingle;
        log.Margin = new Padding(0, 12, 0, 12);
        root.Controls.Add(log, 0, 7);
        root.Controls.Add(createButtons(), 0, 8);
        Controls.Add(root);

        Shown += async (_, _) =>
        {
            detectDefaults();

            if (!string.IsNullOrWhiteSpace(this.initialInput))
                inputPath.Text = this.initialInput;

            if (!string.IsNullOrWhiteSpace(this.initialOutput))
                outputPath.Text = this.initialOutput;

            if (this.initialWorkers.HasValue)
                workers.Value = Math.Clamp(this.initialWorkers.Value, (int)workers.Minimum, (int)workers.Maximum);

            if (this.autoStart)
                await start();
        };
        FormClosing += onFormClosing;
    }

    private Control createPathRow(string labelText, TextBox textBox, Button browseButton, EventHandler browseAction)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        var label = new Label
        {
            AutoSize = true,
            Text = labelText,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = Color.FromArgb(51, 59, 73),
            Margin = new Padding(0, 0, 0, 5),
        };
        panel.Controls.Add(label, 0, 0);
        panel.SetColumnSpan(label, 2);

        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(0, 0, 8, 0);
        textBox.MinimumSize = new Size(0, 30);
        panel.Controls.Add(textBox, 0, 1);

        browseButton.Text = "Browse…";
        browseButton.Dock = DockStyle.Fill;
        browseButton.Margin = Padding.Empty;
        browseButton.Click += browseAction;
        panel.Controls.Add(browseButton, 1, 1);
        return panel;
    }

    private Control createOptionsRow()
    {
        int recommendedWorkers = Math.Max(1, Environment.ProcessorCount);
        int workerStep = Math.Max(1, recommendedWorkers / 4);
        int maximumWorkers = Math.Max(recommendedWorkers, recommendedWorkers + workerStep * 2);
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 18),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "CPU workers",
            Font = new Font("Segoe UI Semibold", 9F),
            Margin = new Padding(0, 7, 8, 0),
        });
        workers.Minimum = 1;
        workers.Maximum = maximumWorkers;
        workers.Increment = workerStep;
        workers.Value = recommendedWorkers;
        workers.Width = 72;
        panel.Controls.Add(workers);
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"{recommendedWorkers} recommended; arrows test {recommendedWorkers + workerStep} / {maximumWorkers}. Higher can be slower and use more RAM.",
            ForeColor = Color.FromArgb(92, 100, 114),
            Margin = new Padding(12, 7, 0, 0),
        });
        return panel;
    }

    private Control createProgressPanel()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Padding = new Padding(16),
            Margin = Padding.Empty,
        };
        status.AutoSize = true;
        status.Text = "Detecting your lazer library…";
        status.Font = new Font("Segoe UI Semibold", 11F);
        status.ForeColor = Color.FromArgb(36, 44, 58);
        status.Margin = new Padding(0, 0, 0, 8);
        panel.Controls.Add(status);

        progressBar.Dock = DockStyle.Top;
        progressBar.Height = 20;
        progressBar.Maximum = 10000;
        progressBar.Margin = new Padding(0, 0, 0, 8);
        panel.Controls.Add(progressBar);

        detail.AutoSize = true;
        detail.Text = "Progress is saved in batches and resumes automatically.";
        detail.ForeColor = Color.FromArgb(92, 100, 114);
        panel.Controls.Add(detail);
        return panel;
    }

    private Control createButtons()
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
        };

        startButton.Text = "Start / Resume";
        startButton.Width = 130;
        startButton.Height = 38;
        startButton.BackColor = Color.FromArgb(233, 87, 137);
        startButton.ForeColor = Color.White;
        startButton.FlatStyle = FlatStyle.Flat;
        startButton.FlatAppearance.BorderSize = 0;
        startButton.Click += async (_, _) => await start();

        pauseButton.Text = "Pause safely";
        pauseButton.Width = 115;
        pauseButton.Height = 38;
        pauseButton.Enabled = false;
        pauseButton.Click += (_, _) => pause();

        openFolderButton.Text = "Open output";
        openFolderButton.Width = 105;
        openFolderButton.Height = 38;
        openFolderButton.Click += (_, _) => openOutputFolder();

        panel.Controls.Add(startButton);
        panel.Controls.Add(pauseButton);
        panel.Controls.Add(openFolderButton);
        return panel;
    }

    private void detectDefaults()
    {
        try
        {
            string detected = StarDatabaseBuilder.ResolveInput(null);
            inputPath.Text = detected;
            outputPath.Text = detected;
            status.Text = "Ready to build or resume";
            detail.Text = "Realm is opened read-only. The database will be written directly into your lazer data folder.";
            appendLog($"Detected lazer data: {detected}");
        }
        catch (Exception exception)
        {
            status.Text = "Choose your lazer data folder";
            detail.Text = exception.Message;
            appendLog(exception.Message);
        }
    }

    private async Task start()
    {
        if (cancellation != null)
            return;

        try
        {
            string resolvedInput = StarDatabaseBuilder.ResolveInput(inputPath.Text);
            string output = Path.GetFullPath(outputPath.Text);
            inputPath.Text = resolvedInput;
            outputPath.Text = output;
            cancellation = new CancellationTokenSource();
            setRunning(true);
            progressBar.Value = 0;
            appendLog($"Started with {(int)workers.Value} workers.");

            var request = new BuilderRequest(resolvedInput, output, (int)workers.Value);
            var progress = new Progress<BuilderProgress>(applyProgress);
            BuilderResult result = await Task.Run(() => new StarDatabaseBuilder().Run(request, progress, cancellation.Token));

            status.Text = "Database complete";
            detail.Text = $"{result.Total:N0} maps • {result.Reused:N0} resumed • {result.Failed:N0} unavailable • {formatDuration(result.Elapsed)}";
            progressBar.Value = progressBar.Maximum;
            appendLog($"Complete: {result.DatabasePath}");
            MessageBox.Show(this, $"The shared star database is ready.\n\n{result.DatabasePath}", "Kumori database complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            status.Text = "Paused safely";
            detail.Text = "Completed batches were saved. Press Start / Resume whenever you are ready.";
            appendLog("Paused safely; saved work will be reused.");
        }
        catch (Exception exception)
        {
            status.Text = "Builder stopped with an error";
            detail.Text = exception.Message;
            appendLog(exception.ToString());
            MessageBox.Show(this, exception.Message, "Kumori builder error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            setRunning(false);

            if (closeWhenStopped)
                BeginInvoke(new Action(Close));
        }
    }

    private void applyProgress(BuilderProgress update)
    {
        if (update.Stage == BuilderStage.Warning)
        {
            appendLog(update.Message);
            return;
        }

        status.Text = update.Message;

        if (update.Total > 0)
        {
            progressBar.Value = Math.Clamp((int)Math.Round((double)update.Completed / update.Total * progressBar.Maximum), 0, progressBar.Maximum);
            string eta = update.Remaining.HasValue ? formatDuration(update.Remaining.Value) : "estimating…";
            detail.Text = $"{update.Completed:N0} / {update.Total:N0} maps   •   {update.Ratings:N0} ratings   •   "
                          + $"{update.MapsPerSecond:0.00} maps/s   •   ETA {eta}   •   {update.Failed:N0} unavailable";
        }

        if (update.Stage is BuilderStage.Scanning or BuilderStage.Preparing or BuilderStage.Finalising)
            appendLog(update.Message);
    }

    private void pause()
    {
        if (cancellation == null)
            return;

        pauseButton.Enabled = false;
        status.Text = "Pausing safely…";
        detail.Text = "Finishing and saving in-flight database batches.";
        cancellation.Cancel();
    }

    private void setRunning(bool running)
    {
        inputPath.Enabled = !running;
        outputPath.Enabled = !running;
        inputBrowse.Enabled = !running;
        outputBrowse.Enabled = !running;
        workers.Enabled = !running;
        startButton.Enabled = !running;
        pauseButton.Enabled = running;
    }

    private void chooseInput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the lazer data folder containing client.realm and files",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(inputPath.Text) ? inputPath.Text : string.Empty,
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            inputPath.Text = dialog.SelectedPath;
    }

    private void chooseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where kumori-star-ratings.db will be written",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(outputPath.Text) ? outputPath.Text : string.Empty,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            outputPath.Text = dialog.SelectedPath;
    }

    private void openOutputFolder()
    {
        try
        {
            string path = Path.GetFullPath(outputPath.Text);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not open output folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void appendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        log.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
    }

    private static string formatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";

        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes:00}m";

        return $"{Math.Max(0, (int)duration.TotalMinutes)}m {duration.Seconds:00}s";
    }

    private void onFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (cancellation == null || closeWhenStopped)
            return;

        DialogResult choice = MessageBox.Show(this, "Pause safely and close after current database batches are saved?", "Builder is running",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        e.Cancel = true;

        if (choice != DialogResult.Yes)
            return;

        closeWhenStopped = true;
        pause();
    }
}
