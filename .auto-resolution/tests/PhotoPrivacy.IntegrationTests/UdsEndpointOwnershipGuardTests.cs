using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// Ticket 13 (B14) source-lint guard for the ticket-11 UnixDomainSocketIpcTransport
/// endpoint-ownership fix: the socket file may only be deleted by the endpoint owner
/// (the server-side instance that called ListenAsync, detected via
/// <c>ownsEndpoint = _listener is not null</c>). Client transports are per-call
/// (ADR 0057) and share the same socket path — an unconditional File.Delete there
/// unlinks the live server endpoint after the first call and every later connect
/// fails (AddressNotAvailable on Linux/macOS).
/// The ClassifyDisposeAsync classifier is a pure function so the negative self-proof can feed
/// injected violating samples (in-memory, no files touched), per the ticket-06/B07
/// whitelist-guard pattern in UiLauncherSourceTests.
/// </summary>
public sealed class UdsEndpointOwnershipGuardTests
{
    private static readonly string TransportRelativePath =
        Path.Combine("src", "PhotoPrivacy.Ipc", "UnixDomainSocketIpcTransport.cs");

    /// <summary>
    /// Pure classifier over the DisposeAsync body of
    /// UnixDomainSocketIpcTransport.cs (comments stripped, comment-only lines removed).
    /// Returns violation reasons; empty means the ownership shape holds.
    /// Locked shape: (1) the ownership flag must be derived as
    /// <c>ownsEndpoint = _listener is not null</c>; (2) every File.Delete touching the
    /// socket path inside DisposeAsync must sit inside an <c>if (ownsEndpoint)</c> block.
    /// File.Delete in ListenAsync (ADR 0026 stale-socket cleanup, pre-bind) is
    /// out of scope here — it runs before the endpoint exists and is guarded separately.
    /// </summary>
    internal static List<string> ClassifyDisposeAsync(string transportSource)
    {
        var violations = new List<string>();

        var disposeStart = transportSource.IndexOf("ValueTask DisposeAsync()", StringComparison.Ordinal);
        Assert.True(disposeStart >= 0, "DisposeAsync entry point not found in UnixDomainSocketIpcTransport.cs");
        var bodyStart = transportSource.IndexOf('{', disposeStart);
        var body = ExtractBalancedBlock(transportSource, bodyStart);

        var hasOwnershipFlag = Regex.IsMatch(
            body,
            @"ownsEndpoint\s*=\s*_listener\s+is\s+not\s+null",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(5));
        if (!hasOwnershipFlag)
        {
            violations.Add("DisposeAsync must derive the ownership flag as 'ownsEndpoint = _listener is not null'");
        }

        // Every File.Delete call inside DisposeAsync must be lexically inside an
        // 'if (ownsEndpoint)' block: scan the balanced block that follows each guard.
        var deleteIdx = body.IndexOf("File.Delete(", StringComparison.Ordinal);
        if (deleteIdx < 0)
        {
            violations.Add("DisposeAsync no longer deletes the socket file — endpoint cleanup shape changed, re-review this guard");
            return violations;
        }

        var guarded = false;
        var scanIdx = 0;
        while ((scanIdx = body.IndexOf("if (ownsEndpoint)", scanIdx, StringComparison.Ordinal)) >= 0)
        {
            var braceIdx = body.IndexOf('{', scanIdx);
            if (braceIdx < 0) break;
            var block = ExtractBalancedBlock(body, braceIdx);
            if (deleteIdx >= scanIdx && deleteIdx < braceIdx + block.Length)
            {
                guarded = true;
                break;
            }
            scanIdx += "if (ownsEndpoint)".Length;
        }

        if (!guarded)
        {
            violations.Add("File.Delete in DisposeAsync must be wrapped in an 'if (ownsEndpoint)' owner condition");
        }

        return violations;
    }

    /// <summary>Extracts the balanced {…} block starting at the given open brace index.</summary>
    private static string ExtractBalancedBlock(string text, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[openBraceIndex..(i + 1)];
            }
        }

        throw new InvalidOperationException("unbalanced braces in transport source");
    }

    private static string ReadTransportSourceStripped()
    {
        var lines = File.ReadAllLines(Path.Combine(SourceLint.RepoRoot, TransportRelativePath));
        var kept = lines.Select(SourceLint.StripLineComment)
            .Where(l => !string.IsNullOrWhiteSpace(l));
        return string.Join(Environment.NewLine, kept);
    }

    [Fact]
    public void DisposeAsync_Should_Delete_Socket_Only_When_Owning_Endpoint()
    {
        var violations = ClassifyDisposeAsync(ReadTransportSourceStripped());
        Assert.True(violations.Count == 0,
            "UDS endpoint-ownership shape regressed (ticket 11 fix / ticket 13 guard):" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Uds_Ownership_Guard_Negative_Self_Proof()
    {
        // Injected violating/green samples (in-memory) — the classifier must flag
        // every regression form and pass the current fixed shape.
        var currentSource = ReadTransportSourceStripped();
        var failing = new (string Fragment, string Reason)[]
        {
            // unconditional delete: ownership flag removed
            (currentSource
                .Replace("bool ownsEndpoint = _listener is not null;", "bool ownsEndpoint = true;")
                .Replace("if (ownsEndpoint)", "if (true)"),
             "unconditional File.Delete (owner check short-circuited to true)"),
            // delete moved outside the owner condition
            (currentSource
                .Replace("if (ownsEndpoint)", "if (_socketPath.Length > 0)"),
             "File.Delete guarded by a non-ownership condition"),
            // ownership flag no longer derived from _listener
            (currentSource
                .Replace("ownsEndpoint = _listener is not null", "ownsEndpoint = _socketPath is not null"),
             "ownership flag decoupled from the live listener"),
        };

        var failures = new List<string>();
        foreach (var (fragment, reason) in failing)
        {
            if (fragment == currentSource)
            {
                failures.Add($"MUTATION DID NOT APPLY (sample equals green source): {reason}");
                continue;
            }
            if (ClassifyDisposeAsync(fragment).Count == 0)
            {
                failures.Add($"MISSED (should be red): {reason}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Negative self-proof failed:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void DisposeAsync_Should_Document_The_Ownership_Rationale()
    {
        // The in-code rationale (why client transports must not delete) is part of
        // the fix contract — silently dropping it should trip this guard too.
        var source = SourceLint.Read("src", "PhotoPrivacy.Ipc", "UnixDomainSocketIpcTransport.cs");
        Assert.Contains("only by the endpoint owner", source, StringComparison.Ordinal);
        Assert.Contains("B11 finding", source, StringComparison.Ordinal);
    }
}
