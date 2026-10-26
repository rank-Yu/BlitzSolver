using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using STS2RitsuLib;
using STS2RitsuLib.Patching.Core;

namespace BlitzSolver;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "BlitzSolver";
    public const string DisplayName = "极速求解器";
    public const string EnglishName = "Blitz Solver";
    public const string Version = "0.1.0";

    public static bool Enabled { get; private set; }

    public static bool TestMode { get; private set; }

    public static void Initialize()
    {
        // 与 CombatSolver 共存时完全不启用：不注册补丁、不订阅事件，只保留这一行日志。
        if (CombatSolverPresent())
        {
            MegaCrit.Sts2.Core.Logging.Log.Info(
                "[BlitzSolver] DISABLED reason=CombatSolver present (coexistence policy: no patches, no events)");
            return;
        }

        string? testDirectory = Environment.GetEnvironmentVariable(TestSession.DirectoryVariable);
        TestMode = !string.IsNullOrWhiteSpace(testDirectory);
        Enabled = true;
        ModLog.Initialize(TestMode ? Path.Combine(testDirectory!, "mod.log") : null);
        ModLog.Info($"[BlitzSolver] INIT id={ModId} version={Version} name={DisplayName} english={EnglishName} test_mode={TestMode}");

        ModPatcher patcher = RitsuLibFramework.CreatePatcher(ModId, "blitzsolver", DisplayName);
        patcher.RegisterPatch<UnattendedTestIsolationPatch>();
        patcher.RegisterPatch<UnattendedNoDiscoveryOrderPatch>();
        patcher.RegisterPatch<UnattendedHeadlessFtuePatch>();
        patcher.RegisterPatch<TurnSetupRootCapturePatch>();
        RitsuLibFramework.ApplyRequiredPatcher(patcher, DisableMod);
        if (!Enabled)
        {
            ModLog.Error("[BlitzSolver] DISABLED reason=required patch registration failed");
            return;
        }

        // 本批只需要战斗生命周期与测试模式；普通游戏时除搜索取消外没有副作用。
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(
            static _ => SearchSession.OnCombatStarting());
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(static _ =>
        {
            SearchSession.OnCombatEnded();
            AutoCombatSession.OnCombatEnded();
            TestSession.OnCombatEnded();
        });
        if (TestMode)
            RitsuLibFramework.SubscribeLifecycle<GameReadyEvent>(static _ => TestSession.Start());

        NGame? host = NGame.Instance;
        if (host == null)
        {
            ModLog.Warn("[BlitzSolver] HOTKEY_DISABLED reason=NGame_instance_missing");
        }
        else
        {
            host.AddChild(new SearchHotkeyNode());
            host.TreeExiting += static () =>
            {
                AutoCombatSession.Cancel("tree_exiting");
                SearchSession.Cancel("tree_exiting");
            };
            ModLog.Info("[BlitzSolver] HOTKEY_READY key=F8 action=toggle_full_auto");
        }
    }

    private static bool CombatSolverPresent()
    {
        foreach (Mod mod in ModManager.Mods)
        {
            if (!string.Equals(mod.manifest?.id, "CombatSolver", StringComparison.Ordinal))
                continue;
            // None 表示已发现、尚未轮到加载；它仍会与本 Mod 同时出现在进程里。
            if (mod.state is ModLoadState.None or ModLoadState.Loaded)
                return true;
        }
        return false;
    }

    private static void DisableMod()
    {
        Enabled = false;
    }
}
