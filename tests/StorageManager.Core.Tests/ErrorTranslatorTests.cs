using StorageManager.Errors;

namespace StorageManager.Core.Tests;

public class ErrorTranslatorTests
{
    [Theory]
    [InlineData("Permission denied (publickey,password).", "username")]
    [InlineData("reading remote directory: No such file or directory", "does not exist")]
    [InlineData("read: Connection reset by peer", "dropped the connection")]
    [InlineData("Host key verification failed.", "known_hosts")]
    [InlineData("ssh: connect to host x port 22: Connection timed out", "VPN")]
    [InlineData("/bin/sh: No such file or directory\nbanner exchange: Connection to UNKNOWN port 65535: Broken pipe\nread: Connection reset by peer", "jump/gateway")]
    [InlineData("mount_macfuse: the file system is not available (1)\nfuse: unknown option `PubkeyAuthentication=no'", "System Settings")]
    public void Translate_maps_known_stderr(string stderr, string expectGuidanceFragment)
    {
        var e = ErrorTranslator.Translate(stderr, 1, twoFactor: false);
        Assert.Contains(expectGuidanceFragment, e.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(stderr, e.Raw);
        Assert.NotEqual("The storage could not be mounted.", e.Headline);
    }

    [Fact]
    public void Translate_unknown_falls_through_with_raw_preserved()
    {
        var e = ErrorTranslator.Translate("some novel failure", 1, twoFactor: false);
        Assert.Equal("The storage could not be mounted.", e.Headline);
        Assert.Equal("some novel failure", e.Raw);
    }

    [Fact]
    public void Translate_unknown_two_factor_adds_code_hint()
    {
        var e = ErrorTranslator.Translate("weird", 1, twoFactor: true);
        Assert.Contains("two-factor", e.Guidance, StringComparison.OrdinalIgnoreCase);
    }
}
