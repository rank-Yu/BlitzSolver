// All-or-nothing root-private-state claim ledger. The captured set mirrors
// PowerPredictionStateSupport.CaptureRootState / RelicPredictionStateSupport.IsTracked and
// SimulatedCombatState.IsStatefulRelic at a62f1850; the projections must claim every entry.
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BlitzSolver.Import;

internal sealed class PrivateStateLedger
{
    private readonly HashSet<object> _present = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _claimed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, string> _names = new(ReferenceEqualityComparer.Instance);

    public static PrivateStateLedger Capture(CombatState state, Player player)
    {
        PrivateStateLedger ledger = new();
        foreach (MegaCrit.Sts2.Core.Entities.Creatures.Creature creature in state.Creatures)
        {
            foreach (PowerModel power in creature.Powers)
            {
                if (power.Amount == 0 || !IsCapturedPower(power))
                    continue;
                ledger.Add(power, power.Id.Entry);
            }
        }
        foreach (RelicModel relic in player.Relics)
        {
            if (!IsTrackedRelic(relic) && !IsStatefulRelic(relic))
                continue;
            ledger.Add(relic, relic.Id.Entry);
        }
        return ledger;
    }

    public void Claim(object model)
    {
        if (_present.Contains(model))
            _claimed.Add(model);
    }

    public void RequireAllClaimed()
    {
        foreach ((object model, string name) in _names)
        {
            if (!_claimed.Contains(model))
                throw new UnsupportedImportException(ImportReason.UnclaimedPrivateState, $"StateStore: {name}");
        }
    }

    /// <summary>Negative-test entry point for the same all-or-nothing rule.</summary>
    public static void RequireAllClaimedForTest(IReadOnlyList<string> present, IReadOnlyList<string> claimed)
    {
        HashSet<string> claimedSet = new(claimed, StringComparer.Ordinal);
        foreach (string name in present)
        {
            if (!claimedSet.Contains(name))
                throw new UnsupportedImportException(ImportReason.UnclaimedPrivateState, $"StateStore: {name}");
        }
    }

    private void Add(object model, string name)
    {
        _present.Add(model);
        _names[model] = name;
    }

    private static bool IsCapturedPower(PowerModel power) => power is
        DarkEmbracePower or SkittishPower or PanachePower or SoulboundPower or HellraiserPower
        or CacophonyPower or AutomationPower or JugglingPower or ChainsOfBindingPower
        or SurroundedPower or VoidFormPower or FeralPower or HardenedShellPower;

    private static bool IsTrackedRelic(RelicModel relic) => relic is
        BeatingRemnant or BrilliantScarf or BurningSticks or CentennialPuzzle or DemonTongue
        or IronClub or Kunai or JossPaper or Kusarigama or LetterOpener or LizardTail
        or Metronome or MusicBox or Nunchaku or OrnamentalFan or PaelsLegion or PenNib
        or Permafrost or RainbowRing or Regalite or Shuriken or ThrowingAxe or TuningFork
        or Vambrace or VelvetChoker;

    private static bool IsStatefulRelic(RelicModel relic) => relic is
        ArtOfWar or BeltBuckle or BoneTea or EmotionChip or FakeHappyFlower or FakeVenerableTeaSet
        or HappyFlower or MiniRegent or GalacticDust or PaelsEye or PaelsTears or RainbowRing
        or RedSkull or Pendulum or Pocketwatch or PollinousCore or RuinedHelmet or UnsettlingLamp
        or VenerableTeaSet;
}
