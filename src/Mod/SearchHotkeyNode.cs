using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace BlitzSolver;

/// <summary>
/// The ordinary-game trigger for this batch: F8 starts one capture + background search of the
/// current playable combat. It is deliberately not a UI and not an automatic per-turn search.
/// </summary>
internal sealed partial class SearchHotkeyNode : Node
{
    public override void _Ready() => SetProcessUnhandledInput(true);

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key
            || key.Keycode != Key.F8)
        {
            return;
        }
        NGame? host = NGame.Instance;
        if (host == null)
            return;
        ModLog.Info("[BlitzSolver] HOTKEY_SEARCH_TRIGGER key=F8");
        SearchSession.TryStartManual(host);
    }
}
