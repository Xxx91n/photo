using System.Net.Sockets;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// Unix 对端凭据策略（ADR 0067 纵深第二层）。
/// 内核只做「识别」不做「拦截」——man7 unix(7) 明言 socket 权限位不保证可移植、
/// 可移植程序不应依赖其做安全；因此对端 uid 读回后必须由本层比对，不匹配即拒绝。
/// 凭据不可读一律 fail-closed（返回 null / 空集），绝不静默放行（不变量①）。
/// 方法均为 virtual：测试可注入伪造凭据以模拟跨用户连接（issue 04「可模拟场景内」）。
/// </summary>
public class UnixPeerCredentialPolicy
{
    /// <summary>本进程有效 uid（geteuid）；非 Unix 或读取失败返回 null。</summary>
    public virtual uint? ReadOwnUid() => UnixNativeInterop.TryGetOwnUid(out var uid) ? uid : null;

    /// <summary>已连接 socket 的对端 uid（SO_PEERCRED / getpeereid）；读取失败返回 null。</summary>
    public virtual uint? ReadPeerUid(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return UnixNativeInterop.TryGetPeerUid(socket, out var uid) ? uid : null;
    }

    /// <summary>组名 → 成员 uid 集合；组不存在或解析失败返回空集（调用方据此 fail-closed）。</summary>
    public virtual IReadOnlySet<uint> ReadGroupMemberUids(string groupName)
    {
        var uids = new HashSet<uint>();
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return uids;
        }

        foreach (var member in UnixNativeInterop.ReadGroupMemberNames(groupName))
        {
            if (UnixNativeInterop.TryGetUidByName(member, out var uid))
            {
                uids.Add(uid);
            }
        }

        return uids;
    }
}
