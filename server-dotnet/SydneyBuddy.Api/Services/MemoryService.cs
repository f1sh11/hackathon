using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SydneyBuddy.Api;

/// <summary>授权记忆操作契约；the application boundary for the official H-MEM engine.</summary>
public interface IMemoryService
{
    Task<JsonObject> RunAsync(string action, JsonObject arguments, CancellationToken ct = default);
}

/// <summary>串行调用官方 H-MEM 引擎并管理子进程；serializes persistent-memory operations without requiring Python.</summary>
public sealed class MemoryService(ProjectPaths paths) : IMemoryService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const string Failure = "The H-MEM operation failed. Memory has not been confirmed saved or removed.";

    public async Task<JsonObject> RunAsync(string action, JsonObject arguments, CancellationToken ct = default)
    {
        if (action is not ("add" or "query" or "delete" or "list" or "stage" or "discard_session")) throw new ServiceException(Failure);
        await Gate.WaitAsync(ct);
        try
        {
            var directory = paths.HmemDirectory;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var payload = (JsonObject)arguments.DeepClone();
            payload["action"] = action;
            payload["directory"] = directory;
            var node = Environment.GetEnvironmentVariable("NODE_BINARY");
            if (string.IsNullOrWhiteSpace(node)) node = "node";
            var start = new ProcessStartInfo(node)
            {
                WorkingDirectory = paths.ServerDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(Path.Combine(paths.ServerDirectory, "hmem_bridge.ts"));
            using var process = new Process { StartInfo = start };
            try
            {
                if (!process.Start()) throw new ServiceException(Failure);
            }
            catch (Win32Exception)
            {
                throw new ServiceException("H-MEM requires Node.js 24 or later. Install it and try again.");
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            // 同时读取两个管道以防死锁；drain stderr alongside stdout so neither pipe blocks the child.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.StandardInput.WriteAsync(payload.ToJsonString().AsMemory(), linked.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(linked.Token);
                var output = await stdout;
                _ = await stderr; // 日志可能包含私人偏好；consume process messages without surfacing them.
                if (process.ExitCode != 0) throw new ServiceException(Failure);
                var result = JsonNode.Parse(output);
                return result switch
                {
                    JsonObject obj => obj,
                    JsonArray list when action == "list" => new JsonObject { ["memories"] = list },
                    _ => throw new ServiceException(Failure)
                };
            }
            catch (OperationCanceledException)
            {
                // 先终止整棵进程树，再等管道结束；kill the process tree before awaiting pipe completion.
                Stop(process);
                await Task.WhenAll(stdout, stderr);
                if (ct.IsCancellationRequested) throw;
                throw new ServiceException(Failure);
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
            {
                Stop(process);
                await Task.WhenAll(stdout, stderr);
                throw new ServiceException(Failure);
            }
        }
        finally { Gate.Release(); }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
