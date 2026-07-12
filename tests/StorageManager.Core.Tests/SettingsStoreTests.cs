using StorageManager.Settings;

namespace StorageManager.Core.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "mounttool-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Round_trips_saved_settings()
    {
        var store = new SettingsStore(_dir);
        var settings = new UserSettings(
            Username: "jbloggs",
            HostName: "lxplus.cern.ch",
            RemotePathTemplate: "/eos/user/$USER1/$USER",
            MountTarget: "/home/jbloggs/S",
            CustomHosts: new[] { "my.host" },
            CustomPaths: new[] { "/eos/project/foo" });

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("jbloggs", loaded.Username);
        Assert.Equal("lxplus.cern.ch", loaded.HostName);
        Assert.Equal("/eos/user/$USER1/$USER", loaded.RemotePathTemplate);
        Assert.Equal("/home/jbloggs/S", loaded.MountTarget);
        Assert.Equal(new[] { "my.host" }, loaded.Hosts);
        Assert.Equal(new[] { "/eos/project/foo" }, loaded.Paths);
    }

    [Fact]
    public void Missing_file_returns_empty()
    {
        var store = new SettingsStore(_dir);
        Assert.Same(UserSettings.Empty, store.Load());
    }

    [Fact]
    public void Corrupt_file_returns_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), "{ not valid json ");
        var store = new SettingsStore(_dir);
        Assert.Same(UserSettings.Empty, store.Load());
    }
}
