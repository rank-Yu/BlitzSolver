using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Relics;

// Silent relics and the generic power-amount-given relic. Existing engine locations are named on
// each definition. Numeric values come from the per-root relic rule table.

/// <summary>
/// <c>Tingsha</c>: after a card is discarded, while its owner is the current side, deal 3 unpowered
/// damage to a random hittable enemy (<c>AfterCardDiscardedMirrors.HandleTingsha</c>).
/// </summary>
[FlashContent("TINGSHA")]
public sealed class Tingsha : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void AfterCardDiscarded(ref FlashExec x, FlashListener self, in FlashCard card)
    {
        if (x.B.Side != FlashSide.Player)
            return;
        FlashCardEffects.DamageRandomEnemy(ref x, x.R.RelicVar(Id, 0), FlashValueProps.Unpowered,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>HelicalDart</c>: after a card with the Shiv tag is played, apply 1 temporary Dexterity
/// (<c>AfterCardPlayedMirrors.HandleHelicalDart</c> → <c>ApplyTemporaryDexterity</c>).
/// </summary>
[FlashContent("HELICAL_DART")]
public sealed class HelicalDart : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((x.R.Card(play.Card.Id, play.Card.Upgrade).Tags & FlashCardTag.Shiv) == 0)
            return;
        int amount = (int)x.R.RelicVar(Id, 0);
        ushort temporary = FlashRegistry.PowerId<Powers.HelicalDartPower>();
        ushort dexterity = FlashRegistry.PowerId<Powers.DexterityPower>();
        bool alreadyApplied = FlashKernel.GetAmount(x.B, FlashLimits.PlayerOwner, temporary) != 0;
        if (!alreadyApplied)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, amount, FlashLimits.PlayerOwner);
        int applied = FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, temporary, amount,
            FlashLimits.PlayerOwner);
        if (alreadyApplied && applied != 0)
            FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, dexterity, applied, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>BeltBuckle</c>: applies 2 Dexterity when the owner has no potions. The pre-combat application
/// is already in the captured root; <c>AfterPotionUsed</c> reapplies it once when the last potion is
/// used (<c>SimulatedCombatState.TriggerRelicsAfterPotionUsed</c>, gated by the relic's
/// StatefulRelicState.Current flag).
/// </summary>
[FlashContent("BELT_BUCKLE")]
public sealed class BeltBuckle : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["DexterityPower"];

    public override void AfterPotionUsed(ref FlashExec x, FlashListener self, ushort potionId)
    {
        if (x.B.Relics[self.Index].Counter != 0 || HasAvailablePotion(x.B))
            return;
        x.B.Relics[self.Index].Counter = 1;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DexterityPower>(), (int)x.R.RelicVar(Id, 0), FlashLimits.PlayerOwner);
    }

    private static bool HasAvailablePotion(in FlashBattle battle)
    {
        for (int slot = 0; slot < battle.PotionSlotCount; slot++)
        {
            if (battle.Potions[slot] != 0)
                return true;
        }
        return false;
    }
}

/// <summary>
/// <c>PaperKrane</c>: <c>ModifyWeakMultiplier</c> subtracts 0.15 for powered attacks against the
/// owner. The existing engine routes it through <c>WeakPower</c>'s multiplier; the Flash kernel reads
/// the relic there, so this definition only declares the rule-table variable.
/// </summary>
[FlashContent("PAPER_KRANE")]
public sealed class PaperKrane : FlashRelicDef
{
}

/// <summary>
/// <c>SneckoSkull</c>: <c>ModifyPowerAmountGivenAdditive</c> adds 1 to every Poison amount the owner
/// gives (<c>PersistentRelicSupport.ModifyPowerAmountGiven</c>). The after-hook is cosmetic.
/// </summary>
[FlashContent("SNECKO_SKULL")]
public sealed class SneckoSkull : FlashRelicDef
{
    public override IReadOnlyList<string> VarNames => ["PoisonPower"];

    public override decimal ModifyPowerAmountGiven(in FlashBattle battle, FlashRules rules, FlashListener self,
        ushort powerId, byte giver, byte target, decimal amount)
    {
        if (giver != FlashLimits.PlayerOwner)
            return 0m;
        if (!FlashRegistry.TryPower("POISON_POWER", out ushort poison) || powerId != poison)
            return 0m;
        return rules.RelicVar(Id, 0);
    }
}
