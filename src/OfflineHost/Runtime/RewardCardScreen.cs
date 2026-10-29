// Managed UI-only bridge for sts2 v0.111.0 CardReward.OnSelect's existing awaited screen path.
// Alternative hotkey IL bypass adapted from tests/Differential/Run/RunRewardShopCompare.cs,
// baseline 16128953 (Sts2Solver, MIT). No reward logic is replaced.
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Sts2Solver.OfflineHost.Run;

internal static class RewardCardScreen
{
    internal sealed class Lease(OfflineRunHost host, MegaCrit.Sts2.Core.Rewards.CardReward reward)
    {
        internal readonly OfflineRunHost Host = host;
        internal readonly MegaCrit.Sts2.Core.Rewards.CardReward Reward = reward;
        internal NCardRewardSelectionScreen? Screen;
        internal IReadOnlyList<CardCreationResult> Cards = [];
        // OnSelect interprets indices using this initial native alternatives collection after reroll too.
        internal IReadOnlyList<CardRewardAlternative> Alternatives = [];
    }
    private static readonly AsyncLocal<Lease?> Current = new();
    private static readonly ConditionalWeakTable<NCardRewardSelectionScreen, Lease> Known = new();
    internal static IDisposable Use(Lease lease)
    {
        if (Current.Value != null) throw new InvalidDataException("host-reward-screen-nested");
        Current.Value = lease;
        return new Scope(lease);
    }
    private sealed class Scope(Lease lease) : IDisposable
    {
        public void Dispose()
        {
            lease.Screen = null; lease.Cards = []; lease.Alternatives = [];
            if (ReferenceEquals(Current.Value, lease)) Current.Value = null;
        }
    }
    internal static void Install(Harmony harmony)
    {
        foreach ((string target, string prefix) in new[] {
            ("ShowScreen", nameof(Show)), ("OptionSelected", nameof(Select)),
            ("RefreshOptions", nameof(Refresh)), ("GetCardHolder", nameof(Holder)) })
            harmony.Patch(AccessTools.Method(typeof(NCardRewardSelectionScreen), target),
                prefix: new HarmonyMethod(typeof(RewardCardScreen), prefix));
        harmony.Patch(AccessTools.Constructor(typeof(CardRewardAlternative),
            [typeof(string), typeof(Func<Task>), typeof(PostAlternateCardRewardAction)]),
            transpiler: new HarmonyMethod(typeof(RewardCardScreen), nameof(AlternativeHotkeys)));
    }
    private static bool Show(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions,
        ref NCardRewardSelectionScreen? __result)
    {
        Lease? lease = Current.Value;
        if (lease == null) return true;
        if (lease.Screen != null) throw new InvalidDataException("host-reward-screen-duplicate");
        // No constructor, native handle, scene tree or Godot dispose. Suppression occurs before publication.
        NCardRewardSelectionScreen screen = (NCardRewardSelectionScreen)RuntimeHelpers.GetUninitializedObject(typeof(NCardRewardSelectionScreen));
        GC.SuppressFinalize(screen);
        Known.Add(screen, lease);
        lease.Screen = screen; lease.Cards = options; lease.Alternatives = extraOptions;
        __result = screen; return false;
    }
    private static Lease Owner(NCardRewardSelectionScreen screen)
    {
        Lease lease = Current.Value ?? throw new InvalidDataException("host-unrelated-reward-ui");
        if (!ReferenceEquals(lease.Screen, screen)) throw new InvalidDataException("host-unrelated-reward-ui");
        return lease;
    }
    private static bool Select(NCardRewardSelectionScreen __instance, ref Task<int?> __result)
    {
        if (!Known.TryGetValue(__instance, out _))
        { if (Current.Value != null) throw new InvalidDataException("host-unrelated-reward-ui"); return true; }
        Lease lease = Owner(__instance);
        __result = lease.Host.SelectRewardCardAsync(lease.Reward, lease.Cards, lease.Alternatives);
        return false;
    }
    private static bool Refresh(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options)
    {
        if (!Known.TryGetValue(__instance, out _))
        { if (Current.Value != null) throw new InvalidDataException("host-unrelated-reward-ui"); return true; }
        Owner(__instance).Cards = options;
        return false;
    }
    private static bool Holder(NCardRewardSelectionScreen __instance, ref NCardHolder? __result)
    {
        if (!Known.TryGetValue(__instance, out _))
        { if (Current.Value != null) throw new InvalidDataException("host-unrelated-reward-ui"); return true; }
        Owner(__instance); __result = null; return false;
    }
    private static IEnumerable<CodeInstruction> AlternativeHotkeys(IEnumerable<CodeInstruction> instructions)
    {
        int fields = 0, conversions = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field
                && field.DeclaringType == typeof(MegaCrit.Sts2.Core.ControllerInput.MegaInput))
            { fields++; instruction.opcode = OpCodes.Ldstr; instruction.operand = ""; }
            else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Godot.StringName)
                && method.Name == "op_Implicit" && method.ReturnType == typeof(string))
            { conversions++; instruction.opcode = OpCodes.Nop; instruction.operand = null; }
            yield return instruction;
        }
        if (fields != 3 || conversions != 3) throw new InvalidDataException("native alternative hotkey IL changed");
    }
}
