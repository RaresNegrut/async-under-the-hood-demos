using System.Diagnostics;

namespace _06_ThreadPoolDeadlockDemo;

public class MainForm : Form
{
    private readonly Button _btnDeadlock;
    private readonly Button _btnCorrect;
    private readonly Label _lblStatus;
    private readonly Label _lblInfo;

    public MainForm()
    {
        Text = "ThreadPool & SynchronizationContext Demo";
        Size = new Size(520, 300);
        StartPosition = FormStartPosition.CenterScreen;

        _lblInfo = new Label
        {
            Text = $"SynchronizationContext: {SynchronizationContext.Current?.GetType().Name ?? "null"}\n"
                 + $"ThreadPool min threads: {GetPoolInfo()}\n"
                 + $"UI Thread ID: {Environment.CurrentManagedThreadId}",
            Location = new Point(20, 15),
            AutoSize = true
        };

        _btnDeadlock = new Button
        {
            Text = "DEADLOCK with result",
            Location = new Point(20, 80),
            Size = new Size(220, 45),
            BackColor = Color.MistyRose
        };
        _btnDeadlock.Click += OnDeadlockClick;

        _btnCorrect = new Button
        {
            Text = "CORRECT using await",
            Location = new Point(260, 80),
            Size = new Size(220, 45),
            BackColor = Color.Honeydew
        };
        _btnCorrect.Click += OnCorrectClick;

        _lblStatus = new Label
        {
            Text = "Click a button...",
            Location = new Point(20, 145),
            Size = new Size(460, 100),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.AddRange(new Control[] { _lblInfo, _btnDeadlock, _btnCorrect, _lblStatus });
    }

    private void OnDeadlockClick(object? sender, EventArgs e)
    {
        _lblStatus.Text =
            "Calling SomeAsync().Result — UI thread is now BLOCKED.\n"
          + "The continuation needs this thread → DEADLOCK.\n"
          + "App will freeze — you'll have to kill it.";
        _lblStatus.Refresh();           // force repaint before we freeze

        // 1. This blocks the UI thread, waiting for the Task to finish
        var result = SomeAsync().Result;

        // 4. We never reach here - DEADLOCK
        _lblStatus.Text = result;
    }

    private async void OnCorrectClick(object? sender, EventArgs e)
    {
        _lblStatus.Text = "Awaiting SomeAsync()...";

        try
        {
            var result = await SomeAsync().ConfigureAwait(false);
            _lblStatus.Text = result; // UI thread is FREE while waiting
        }
        catch (Exception ioex)
        {
            Invoke(() =>
            {
                _lblStatus.Text = $"{ioex.GetType().Name}: {ioex.Message}";
            });
        }
    }

    // Shared Async method
    private async Task<string> SomeAsync()
    {
        var ctx = SynchronizationContext.Current?.GetType().Name ?? "null";
        Debug.WriteLine($"SomeAsync started on thread {Environment.CurrentManagedThreadId}, SyncCtx={ctx}");

        // 2. Captures the current SynchronizationContext (WinForms UI context)
        await Task.Delay(1000);

        // 3. Continuation tries to post back to the UI thread
        Debug.WriteLine($"SomeAsync resumed on thread {Environment.CurrentManagedThreadId}");

        return $" Done on thread {Environment.CurrentManagedThreadId} at {DateTime.Now:T}\n"
             + $"SynchronizationContext: {SynchronizationContext.Current?.GetType().Name ?? "null"}";
    }

    private static string GetPoolInfo()
    {
        ThreadPool.GetMinThreads(out int workers, out int io);
        return $"{workers} workers, {io} I/O";
    }
}