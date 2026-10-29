// Read-only visibility follows sts2 v0.111.0 TreasureRoom/NTreasureRoom and relic synchronizer.
using Sts2Solver.Simulation.Combat;
using Sts2Solver.Simulation.Run;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Solver.Import;

public static partial class RunDecisionImporter
{
    /// <summary>0: closed chest (no relic payload); 1: revealed relics; 2: resolved, may leave.
    /// The host owns UI stage, but every model and synchronizer binding is revalidated here.</summary>
    public static NativeDecisionSnapshot<object> Treasure(TreasureRoom room, Player player,
        Func<int> stage, RunDecisionToken token)
    {
        CheckPlayer(player);
        if (!ReferenceEquals(player.RunState.CurrentRoom, room))
            throw new UnsupportedException("decision-treasure: room is not current");
        int currentStage = stage();
        var synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
        IReadOnlyList<RelicModel>? source = synchronizer.CurrentRelics;
        RelicModel[] relics = source?.ToArray() ?? [];
        RunDecisionRequest request = New(currentStage == 1 ? RunDecision.RelicSelect : RunDecision.Treasure, token);
        List<object> bindings = [];
        void Add(object binding, RunDecisionOptionKind kind, RelicModel? relic = null)
        {
            RunDecisionBuilder.AddOption(ref request, new()
            {
                OptionId = Next(request), Kind = kind,
                PayloadKind = relic == null ? RunDecisionPayloadKind.None : RunDecisionPayloadKind.Model,
                Model = relic == null ? default : Model(relic),
            });
            bindings.Add(binding);
        }
        switch (currentStage)
        {
            case 0: Add(room, RunDecisionOptionKind.Open); break;
            case 1:
                if (relics.Length == 0) throw new InvalidDataException("decision-treasure: empty reveal must resolve without a choice");
                foreach (RelicModel relic in relics) Add(relic, RunDecisionOptionKind.Relic, relic);
                Add(player, RunDecisionOptionKind.Skip);
                break;
            case 2: Add(room, RunDecisionOptionKind.Leave); break;
            default: throw new InvalidDataException("decision-treasure: invalid presentation stage");
        }
        return new(request, bindings.ToArray(), () => Treasure(room, player, stage, token),
            () => ReferenceEquals(room, player.RunState.CurrentRoom) && stage() == currentStage
                && ReferenceEquals(source, synchronizer.CurrentRelics)
                && SameInstances(relics, synchronizer.CurrentRelics ?? Array.Empty<RelicModel>()));
    }

    private static bool SameInstances(IReadOnlyList<RelicModel> a, IReadOnlyList<RelicModel> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
}
