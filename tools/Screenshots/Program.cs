using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StorageManager;
using StorageManager.Gui;

// Renders the real Avalonia windows off-screen (headless Skia) to PNG files, so
// the project can auto-generate on-brand screenshots without a display. Usage:
//   dotnet run --project tools/Screenshots -- <output-dir>

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Dark theme to match the website.
        Environment.SetEnvironmentVariable("STORAGEMANAGER_THEME", "dark");

        var outDir = args.FirstOrDefault() ?? "screenshots";
        Directory.CreateDirectory(outDir);

        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        Capture(new MainWindow(), 460, 720, "main.png", outDir);

        // Doctor reads ~/.ssh/config locally — click Run so the panel shows findings.
        Capture(new DoctorWindow(), 560, 560, "doctor.png", outDir, clickLabel: "Run", settleMs: 2500);

        // VS Code verifies the local setup — click Verify to show the checklist.
        Capture(new VsCodeWindow(), 520, 520, "vscode.png", outDir, clickLabel: "Verify", settleMs: 2000);

        // Status auto-refreshes on open; clear the remote paths first so it reads
        // the local Kerberos state without waiting on network quota calls.
        var status = new StatusWindow("lxplus.cern.ch", "rcurrie4");
        ClearField(status, "_paths");
        Capture(status, 560, 600, "status.png", outDir, settleMs: 2000);
    }

    private static void Capture(
        Window window, int w, int h, string name, string dir,
        string? clickLabel = null, int settleMs = 0)
    {
        window.Width = w;
        window.Height = h;
        window.Show();

        Pump(6);

        if (clickLabel is not null)
        {
            var button = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (b.Content as string) == clickLabel);
            button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        var deadline = settleMs / 100;
        for (var i = 0; i < deadline; i++)
        {
            Thread.Sleep(100);
            Pump(2);
        }
        Pump(4);

        var frame = window.CaptureRenderedFrame();
        var path = Path.Combine(dir, name);
        if (frame is not null)
        {
            frame.Save(path);
            Console.WriteLine($"wrote {path}");
        }
        else
        {
            Console.Error.WriteLine($"FAILED to capture {name}");
        }
        window.Close();
    }

    private static void Pump(int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void ClearField(object window, string fieldName)
    {
        var field = window.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(window) is TextBox tb)
            tb.Text = "";
    }
}
