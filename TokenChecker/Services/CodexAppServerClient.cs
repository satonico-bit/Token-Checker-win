using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TokenChecker.Models;

namespace TokenChecker.Services;

/// <summary>
/// `codex app-server` を spawn して JSON-RPC で通信する。
/// macOS 版の CodexAppServerClient と同じ設計をWindows向けに移植。
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string[] _candidates;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private Process?      _process;
    private StreamWriter? _stdin;
    private CancellationTokenSource? _readCts;
    private bool _started;

    private readonly object _pendingLock = new();
    private int _nextId = 1;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public CodexAppServerClient(string[]? candidates = null, TimeSpan? timeout = null)
    {
        _candidates = candidates ?? DefaultCandidates();
        _timeout    = timeout ?? TimeSpan.FromSeconds(8);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _startLock.WaitAsync(ct);
        try
        {
            if (_started && _process?.HasExited == false) return;

            // 前回のプロセスが残っていればクリーンアップ
            if (_started)
            {
                _readCts?.Cancel();
                _stdin?.Dispose();
                _stdin = null;
                try { _process?.Dispose(); } catch { }
                _process = null;
                _started = false;
            }

            var exe = ResolveExecutable() ?? throw DomainError.CodexNotFound();

            _process = CreateProcess(exe);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => FailAll(DomainError.CodexProcessExited());
            _process.Start();

            // UTF-8 NoBOM で書き込み（codex は UTF-8 を期待する）
            _stdin = new StreamWriter(
                _process.StandardInput.BaseStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            { AutoFlush = true };

            _started  = true;
            _readCts  = new CancellationTokenSource();
            _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _readCts.Token), _readCts.Token);

            // initialize ハンドシェイク
            _ = await SendRequestAsync("initialize", new
            {
                clientInfo   = new { name = "token-checker", version = "0.1.0" },
                capabilities = new { }
            }, ct);
            SendNotification("initialized", new { });
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<CodexRateLimitsDto> ReadRateLimitsAsync(CancellationToken ct = default)
    {
        var result = await SendRequestAsync("account/rateLimits/read", new { }, ct);
        if (result.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw DomainError.CodexRpcError("missing result");
        try
        {
            return result.Deserialize<CodexRateLimitsDto>(JsonOpts)
                   ?? throw DomainError.CodexRpcError("null result");
        }
        catch (JsonException e)
        {
            throw DomainError.Decoding($"codex rateLimits: {e.Message}");
        }
    }

    public void Stop()
    {
        _readCts?.Cancel();
        try { if (_process?.HasExited == false) _process.Kill(); } catch { }
        _process?.Dispose();
        _process  = null;
        _stdin?.Dispose();
        _stdin    = null;
        _started  = false;
        FailAll(DomainError.CodexProcessExited());
    }

    // ── Internal I/O ────────────────────────────────────────────────────

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { FailAll(DomainError.CodexProcessExited()); }
    }

    private void ProcessLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("id", out var idEl) ||
                idEl.ValueKind != JsonValueKind.Number) return;

            var id = idEl.GetInt32();

            // JSON-RPC エラーレスポンスを正しく処理する
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m)
                    ? m.GetString() ?? "RPC error" : "RPC error";
                lock (_pendingLock)
                {
                    if (_pending.TryGetValue(id, out var tcs))
                    {
                        _pending.Remove(id);
                        tcs.TrySetException(DomainError.CodexRpcError(msg));
                    }
                }
                return;
            }

            var result = doc.RootElement.TryGetProperty("result", out var r) ? r.Clone() : default;

            lock (_pendingLock)
            {
                if (_pending.TryGetValue(id, out var tcs))
                {
                    _pending.Remove(id);
                    tcs.TrySetResult(result);
                }
            }
        }
        catch { }
    }

    private async Task<JsonElement> SendRequestAsync<T>(string method, T @params, CancellationToken ct)
    {
        int id;
        lock (_pendingLock) { id = _nextId++; }

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) { _pending[id] = tcs; }

        var json = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id, method, @params }, JsonOpts);
        _stdin!.WriteLine(json);

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        await using var reg  = linked.Token.Register(() =>
        {
            lock (_pendingLock) { _pending.Remove(id); }
            tcs.TrySetException(timeoutCts.IsCancellationRequested
                ? (Exception)DomainError.Timeout()
                : new OperationCanceledException(ct));
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private void SendNotification<T>(string method, T @params)
    {
        var json = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", method, @params }, JsonOpts);
        _stdin!.WriteLine(json);
    }

    private void FailAll(DomainError error)
    {
        List<TaskCompletionSource<JsonElement>> snapshot;
        lock (_pendingLock)
        {
            snapshot = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (var tcs in snapshot) tcs.TrySetException(error);
    }

    // ── Process setup ───────────────────────────────────────────────────

    private static Process CreateProcess(string exe)
    {
        string fileName, args;

        // .cmd / .bat は cmd.exe 経由で実行する
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // Windows パスに '"' は使えないが、万一混入した場合にコマンドインジェクションを防ぐ
            if (exe.Contains('"'))
                throw new ArgumentException($"Executable path contains invalid character '\"': {exe}");
            fileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            args     = $"/c \"{exe}\" app-server";
        }
        else
        {
            fileName = exe;
            args     = "app-server";
        }

        var psi = new ProcessStartInfo(fileName, args)
        {
            UseShellExecute          = false,
            RedirectStandardInput    = true,
            RedirectStandardOutput   = true,
            RedirectStandardError    = true,
            CreateNoWindow           = true,
            StandardOutputEncoding   = Encoding.UTF8,
            StandardErrorEncoding    = Encoding.UTF8,
        };

        // 子プロセスに渡す最小限の環境変数
        psi.EnvironmentVariables.Clear();
        var keep = new[]
        {
            "PATH", "PATHEXT", "USERPROFILE", "HOME",
            "APPDATA", "LOCALAPPDATA", "TEMP", "TMP",
            "USERNAME", "COMSPEC", "SYSTEMROOT", "SYSTEMDRIVE",
            "CODEX_HOME",
        };
        foreach (var k in keep)
        {
            var v = Environment.GetEnvironmentVariable(k);
            if (v != null) psi.EnvironmentVariables[k] = v;
        }

        return new Process { StartInfo = psi };
    }

    private string? ResolveExecutable()
    {
        foreach (var c in _candidates)
            if (File.Exists(c)) return c;

        // where コマンドで PATH から探す（拡張子を検証して意図しない実行形式を弾く）
        try
        {
            using var p = Process.Start(new ProcessStartInfo("where", "codex")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            })!;
            var line = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(2000);
            if (!string.IsNullOrEmpty(line) && File.Exists(line) && IsAllowedExtension(line))
                return line;
        }
        catch { }

        return null;
    }

    private static bool IsAllowedExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] DefaultCandidates()
    {
        var appData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return
        [
            Path.Combine(appData, "npm", "codex.cmd"),
            Path.Combine(appData, "Roaming", "npm", "codex.cmd"),
            Path.Combine(programFiles, "nodejs", "codex.cmd"),
            Path.Combine(programFiles + " (x86)", "nodejs", "codex.cmd"),
        ];
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed class CodexRateLimitsDto
{
    [JsonPropertyName("rateLimits")]
    public RateLimitSnapshot? RateLimits { get; init; }

    [JsonPropertyName("rateLimitsByLimitId")]
    public Dictionary<string, RateLimitSnapshot>? RateLimitsByLimitId { get; init; }

    public RateLimit? FiveHourRateLimit() => Window(300);
    public RateLimit? WeeklyRateLimit()   => Window(10080);

    private RateLimit? Window(long minutes)
    {
        var all = new List<RateLimitWindow>();
        if (RateLimits?.Primary   != null) all.Add(RateLimits.Primary);
        if (RateLimits?.Secondary != null) all.Add(RateLimits.Secondary);
        foreach (var s in (RateLimitsByLimitId ?? []).Values)
        {
            if (s.Primary   != null) all.Add(s.Primary);
            if (s.Secondary != null) all.Add(s.Secondary);
        }
        var w = all.FirstOrDefault(x => x.WindowDurationMins == minutes);
        if (w == null) return null;
        return new RateLimit(
            Utilization: Math.Max(0, (w.UsedPercent ?? 0) / 100.0),
            ResetsAt:    DateTimeOffset.FromUnixTimeSeconds(w.ResetsAt ?? 0).LocalDateTime);
    }
}

public sealed class RateLimitSnapshot
{
    [JsonPropertyName("limitId")]   public string?          LimitId   { get; init; }
    [JsonPropertyName("primary")]   public RateLimitWindow? Primary   { get; init; }
    [JsonPropertyName("secondary")] public RateLimitWindow? Secondary { get; init; }
    [JsonPropertyName("planType")]  public string?          PlanType  { get; init; }
}

public sealed class RateLimitWindow
{
    [JsonPropertyName("usedPercent")]        public int?  UsedPercent        { get; init; }
    [JsonPropertyName("windowDurationMins")] public long? WindowDurationMins { get; init; }
    [JsonPropertyName("resetsAt")]           public long? ResetsAt           { get; init; }
}
