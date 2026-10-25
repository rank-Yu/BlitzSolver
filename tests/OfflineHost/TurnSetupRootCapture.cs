// Captures the player CombatState at the CombatManager.SetupPlayerTurn entry point, before the
// native setup page runs. CombatSolver's TurnSetupRootCapture/PlayerTurnSetupPatches capture the same
// point; the offline host only needs the exact card-object set to mark cards created during setup.
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace OfflineHost;

internal static class TurnSetupRootCapture
{
    private static Harmony? _harmony;

    public static bool Enabled { get; set; }

    /// <summary>Card objects present in the player's combat piles when SetupPlayerTurn was entered.</summary>
    public static HashSet<CardModel>? StartCards { get; private set; }

    public static void Install()
    {
        if (_harmony != null)
            return;
        _harmony = new Harmony("BlitzSolver.OfflineHost.TurnSetupRootCapture");
        _harmony.Patch(
            AccessTools.Method(typeof(CombatManager), "SetupPlayerTurn"),
            prefix: new HarmonyMethod(typeof(TurnSetupRootCapture), nameof(Prefix)));
    }

    public static void BeginBuild()
    {
        StartCards = null;
        Enabled = true;
    }

    private static bool Prefix(CombatManager __instance)
    {
        if (!Enabled || StartCards != null)
            return true;
        CombatState? combat = __instance.DebugOnlyGetState();
        if (combat == null)
            return true;
        Player? player = LocalContext.GetMe(combat);
        if (player?.PlayerCombatState is not { Phase: PlayerTurnPhase.Start })
            return true;
        HashSet<CardModel> cards = new(ReferenceEqualityComparer.Instance);
        AddCards(cards, player.PlayerCombatState.Hand);
        AddCards(cards, player.PlayerCombatState.DrawPile);
        AddCards(cards, player.PlayerCombatState.DiscardPile);
        AddCards(cards, player.PlayerCombatState.ExhaustPile);
        AddCards(cards, player.PlayerCombatState.PlayPile);
        StartCards = cards;
        return true;
    }

    private static void AddCards(HashSet<CardModel> cards, CardPile pile)
    {
        foreach (CardModel card in pile.Cards)
            cards.Add(card);
    }
}
