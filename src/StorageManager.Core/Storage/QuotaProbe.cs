namespace StorageManager.Storage;

/// <summary>
/// Gathers usage/quota for a set of remote paths. Picks the right command per
/// backend (AFS → fs listquota, EOS → eos quota, else df + quota) and runs it on
/// the host via <see cref="IRemoteExec"/>. Also reads local mount usage directly
/// from the filesystem, which needs no remote call.
/// </summary>
public sealed class QuotaProbe(IRemoteExec exec)
{
    public async Task<IReadOnlyList<QuotaInfo>> GatherRemoteAsync(
        string host, string user, IReadOnlyList<string> remotePaths, CancellationToken ct = default)
    {
        var results = new List<QuotaInfo>();

        foreach (var path in remotePaths)
        {
            QuotaInfo? info = null;

            if (path.StartsWith("/afs/", StringComparison.OrdinalIgnoreCase))
            {
                var r = await exec.RunAsync(host, user, $"fs listquota {Quote(path)}", ct);
                if (r.Ok)
                    info = QuotaParsers.ParseAfsListQuota(r.Stdout, path);
            }
            else if (path.StartsWith("/eos/", StringComparison.OrdinalIgnoreCase))
            {
                var r = await exec.RunAsync(host, user, $"eos quota ls -m -p {Quote(path)}", ct);
                if (r.Ok)
                    info = QuotaParsers.ParseEosQuotaMonitoring(r.Stdout, path);
            }

            // Fall back to df (works everywhere) if the specific command didn't yield data.
            if (info is null)
            {
                var r = await exec.RunAsync(host, user, $"df -P {Quote(path)}", ct);
                if (r.Ok)
                    info = QuotaParsers.ParseDf(r.Stdout, path);
            }

            if (info is not null)
                results.Add(info);
        }

        return results;
    }

    /// <summary>Local mount usage from the filesystem (statvfs via DriveInfo); no remote call.</summary>
    public static QuotaInfo? LocalMountUsage(string mountPath)
    {
        try
        {
            var info = new DriveInfo(mountPath);
            if (!info.IsReady)
                return null;
            var used = info.TotalSize - info.AvailableFreeSpace;
            return new QuotaInfo("Mounted volume", mountPath, used, info.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string path) => "'" + path.Replace("'", "'\\''") + "'";
}
