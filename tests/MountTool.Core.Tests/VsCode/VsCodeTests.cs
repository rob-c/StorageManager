using System.Text.Json.Nodes;
using MountTool;
using MountTool.VsCode;

namespace MountTool.Core.Tests.VsCode;

public class VsCodeSettingsTests
{
    [Fact]
    public void Merge_preserves_existing_keys_and_sets_ours()
    {
        var existing = """{ "editor.fontSize": 14, "remote.SSH.showLoginTerminal": false }""";
        var merged = VsCodeSettings.Merge(existing);
        var root = JsonNode.Parse(merged)!.AsObject();

        Assert.Equal(14, (int)root["editor.fontSize"]!);          // untouched
        Assert.True((bool)root["remote.SSH.showLoginTerminal"]!); // corrected to true
        Assert.Equal(60, (int)root["remote.SSH.connectTimeout"]!);
    }

    [Fact]
    public void Merge_tolerates_comments_and_empty()
    {
        var withComment = "{\n  // my settings\n  \"editor.fontSize\": 12\n}";
        var merged = VsCodeSettings.Merge(withComment);
        Assert.True(VsCodeSettings.IsConfigured(merged));

        Assert.True(VsCodeSettings.IsConfigured(VsCodeSettings.Merge(null)));
    }

    [Fact]
    public void IsConfigured_false_when_key_missing_or_wrong()
    {
        Assert.False(VsCodeSettings.IsConfigured("""{ "remote.SSH.showLoginTerminal": true }"""));
        Assert.False(VsCodeSettings.IsConfigured("not json"));
    }
}

public class VsCodeSetupTests : IDisposable
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    private sealed class FakeCli(string? path, List<string> installed) : IVsCodeCli
    {
        public string? ExecutablePath => path;
        public List<string> Installed { get; } = installed;
        public IReadOnlyList<string> ListExtensions() => Installed;
        public bool InstallExtension(string id) { Installed.Add(id.ToLowerInvariant()); return true; }
    }

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "vscode-" + Guid.NewGuid().ToString("N"));

    public VsCodeSetupTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private readonly VsCodeTarget _target =
        new("lxplus", "lxplus.cern.ch", "jbloggs", ProxyJump: "bastion");

    [Fact]
    public void Setup_installs_extensions_writes_host_and_settings()
    {
        var cli = new FakeCli("/usr/bin/code", []);
        var setup = new VsCodeSetup(cli, new FixedClock(DateTime.UnixEpoch));
        var sshPath = Path.Combine(_dir, "ssh_config");
        var settingsPath = Path.Combine(_dir, "settings.json");

        var result = setup.Setup(_target, sshPath, settingsPath);

        Assert.Null(result.Error);
        Assert.Contains("ms-vscode-remote.remote-ssh", result.InstalledExtensions);
        Assert.True(result.HostConfigured);
        Assert.True(result.SettingsConfigured);

        var cfg = File.ReadAllText(sshPath);
        Assert.Contains("Host lxplus", cfg);
        Assert.Contains("HostName lxplus.cern.ch", cfg);
        Assert.Contains("ProxyJump bastion", cfg);
        Assert.True(VsCodeSettings.IsConfigured(File.ReadAllText(settingsPath)));
    }

    [Fact]
    public void Setup_without_code_cli_reports_error()
    {
        var setup = new VsCodeSetup(new FakeCli(null, []), new FixedClock(DateTime.UnixEpoch));
        var result = setup.Setup(_target,
            Path.Combine(_dir, "ssh_config"), Path.Combine(_dir, "settings.json"));
        Assert.NotNull(result.Error);
        Assert.False(result.HostConfigured);
    }

    [Fact]
    public void Verify_reports_all_ok_after_setup()
    {
        var cli = new FakeCli("/usr/bin/code", []);
        var setup = new VsCodeSetup(cli, new FixedClock(DateTime.UnixEpoch));
        var sshPath = Path.Combine(_dir, "ssh_config");
        var settingsPath = Path.Combine(_dir, "settings.json");

        setup.Setup(_target, sshPath, settingsPath);
        var status = setup.Verify(_target, sshPath, settingsPath);

        Assert.True(status.AllOk);
        Assert.Contains(status.Checks, c => c.Label.Contains("Remote-SSH extension") && c.Ok);
        Assert.Contains(status.Checks, c => c.Label.Contains("host 'lxplus'") && c.Ok);
    }

    [Fact]
    public void Verify_flags_missing_pieces_before_setup()
    {
        var setup = new VsCodeSetup(new FakeCli("/usr/bin/code", []));
        var status = setup.Verify(_target,
            Path.Combine(_dir, "absent_config"), Path.Combine(_dir, "absent_settings.json"));

        Assert.False(status.AllOk);
        Assert.Contains(status.Checks, c => c.Label.Contains("host 'lxplus'") && !c.Ok);
    }
}
