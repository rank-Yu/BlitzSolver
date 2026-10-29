// Adapted from tests/Differential/MainLoopContext.cs at 6a5f645 (Sts2Solver, MIT).
// Ported from CombatSolver/tools/search/OfflineSearchHarness/MainLoopContext.cs
// at commit a62f1850 (MIT, Copyright (c) 2026 Torch1230 and contributors).
using System.Collections.Concurrent;

namespace Sts2Solver.OfflineHost.Runtime;

/// <summary>
/// 单线程消息泵。游戏的回合循环全是 async/await：没有 SynchronizationContext 时
/// 每个 await 之后的续体会落到线程池，主线程断言就会失败。装上这个 context 以后所有
/// 续体都回到宿主主线程，等价于 Godot 自己的 GodotSynchronizationContext + _Process 泵。
/// </summary>
internal sealed class MainLoopContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

    public long PumpedCallbacks { get; private set; }

    public bool OnMainThread => Environment.CurrentManagedThreadId == _mainThreadId;

    public override void Post(SendOrPostCallback callback, object? state) => _queue.Add((callback, state));

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (!OnMainThread)
            throw new InvalidOperationException("离线宿主的同步上下文只支持主线程 Send。");
        callback(state);
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>把队列里的续体一条条在主线程上跑掉，直到任务完成或超时。</summary>
    public void RunUntilCompleted(Task task, TimeSpan timeout, string label, CancellationToken token = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!task.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"{label} 在 {timeout.TotalSeconds:0.#}s 内没有完成（已泵 {PumpedCallbacks} 个续体）。");
            PumpOnce(TimeSpan.FromMilliseconds(20));
        }
        token.ThrowIfCancellationRequested();
        Drain(token);
        task.GetAwaiter().GetResult();
    }

    /// <summary>纯粹把当前积压的续体跑完，用于「再推几步」的场合。</summary>
    public void Pump(TimeSpan window)
    {
        DateTime deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
            PumpOnce(TimeSpan.FromMilliseconds(5));
    }

    private bool PumpOnce(TimeSpan wait)
    {
        if (!_queue.TryTake(out var item, (int)wait.TotalMilliseconds))
            return false;
        PumpedCallbacks++;
        item.Callback(item.State);
        return true;
    }

    private void Drain(CancellationToken token)
    {
        while (_queue.TryTake(out var item, 0))
        {
            token.ThrowIfCancellationRequested();
            PumpedCallbacks++;
            item.Callback(item.State);
        }
    }
}
