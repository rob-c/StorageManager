using Avalonia;

namespace MountTool;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // When ssh (spawned by our sshfs child with SSH_ASKPASS pointing back
        // at this executable) invokes us for an authentication prompt, the
        // marker variable it inherited routes us into askpass mode.
        if (Environment.GetEnvironmentVariable(Askpass.ModeVariable) == "1")
        {
            Askpass.ChallengeHandler = MountTool.Gui.AskpassDialog.Prompt;
            Environment.ExitCode = Askpass.Run(args.FirstOrDefault() ?? "");
            return;
        }

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
    }
}
