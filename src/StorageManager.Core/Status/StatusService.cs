using StorageManager.Auth;
using StorageManager.Storage;

namespace StorageManager.Status;

/// <summary>Describes the host/paths a status snapshot is gathered for.</summary>
public sealed record StatusRequest(
    string Host,
    string User,
    IReadOnlyList<string> RemotePaths,
    string? LocalMountPath = null);

/// <summary>A consolidated Kerberos + storage snapshot for one host.</summary>
public sealed record StatusReport(
    KerberosStatus Kerberos,
    IReadOnlyList<QuotaInfo> Quotas);

/// <summary>
/// Single entry point for the "Storage &amp; Auth status" view: composes the
/// Kerberos ticket state and per-path usage/quota into one report. The whole
/// Kerberos half honors the app-wide switch (the single UI tickbox): when off,
/// no klist/kinit ever runs and remote quota SSH does not attempt GSSAPI.
/// </summary>
public sealed class StatusService(KerberosHelper kerberos, QuotaProbe quota, bool useKerberos)
{
    public static StatusService CreateDefault(bool useKerberos) =>
        new(new KerberosHelper(new KerberosCli()),
            new QuotaProbe(new SshRemoteExec(useGssapi: useKerberos)),
            useKerberos);

    public bool KerberosEnabled => useKerberos;

    public async Task<StatusReport> GatherAsync(StatusRequest request, CancellationToken ct = default)
    {
        var kerberosStatus = useKerberos ? kerberos.Status() : KerberosStatus.Off;

        var quotas = new List<QuotaInfo>();
        if (request.LocalMountPath is { Length: > 0 } local
            && QuotaProbe.LocalMountUsage(local) is { } localUsage)
            quotas.Add(localUsage);

        if (request.RemotePaths.Count > 0)
            quotas.AddRange(await quota.GatherRemoteAsync(request.Host, request.User, request.RemotePaths, ct));

        return new StatusReport(kerberosStatus, quotas);
    }

    public KerberosStatus Authenticate(string principal, string password) =>
        useKerberos ? kerberos.Authenticate(principal, password) : KerberosStatus.Off;
}
