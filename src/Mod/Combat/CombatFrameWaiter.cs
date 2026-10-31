using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Solver;

internal static class CombatFrameWaiter
{
    /// <summary>
    /// 交付捕获前让回合开始的可视/计时协程（例如 FakeHappyFlower 的 1 秒激活）结束。
    /// 这些协程会临时改变原生公开状态；离线宿主在消息泵里会立即走完，实机需要等计时器。
    /// </summary>
    internal static async Task SettleAsync(NGame host)
    {
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        SceneTree tree = host.GetTree();
        await host.ToSignal(tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
    }

    internal static async Task NextFrameAsync(NGame host)
        => await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
}
