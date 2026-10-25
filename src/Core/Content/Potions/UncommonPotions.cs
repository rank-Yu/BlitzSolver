using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

[BlitzContent("FYSH_OIL")]
public sealed class FyshOil : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    // FyshOil: Apply<StrengthPower>(player, Strength, owner), then Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.StrengthPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.DexterityPower>(),
            (int)BlitzKernel.PotionVar(x, this, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("HEART_OF_IRON")]
public sealed class HeartOfIron : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    // HeartOfIron: combat.Apply<PlatingPower>(player, PlatingPower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.PlatingPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("LIQUID_BRONZE")]
public sealed class LiquidBronze : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // LiquidBronze: combat.Apply<ThornsPower>(player, ThornsPower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ThornsPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("POTION_OF_BINDING")]
public sealed class PotionOfBinding : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower", "WeakPower"];

    // PotionOfBinding: for each hittable enemy, apply Weak with the VulnerablePower amount and
    // Vulnerable with the WeakPower amount (the existing engine's branch swaps the two variables).
    public override void OnUse(ref BlitzExec x)
    {
        int weak = (int)BlitzKernel.PotionVar(x, this, 0);
        int vulnerable = (int)BlitzKernel.PotionVar(x, this, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.WeakPower>(), weak,
                BlitzLimits.PlayerOwner);
            BlitzKernel.ApplyPower(ref x, owner, BlitzRegistry.PowerId<Powers.VulnerablePower>(), vulnerable,
                BlitzLimits.PlayerOwner);
        }
    }
}

[BlitzContent("POWDERED_DEMISE")]
public sealed class PowderedDemise : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Demise"];

    // PowderedDemise when target != null: combat.Apply<DemisePower>(target, Demise, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.DemisePower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("DUPLICATOR")]
public sealed class Duplicator : BlitzPotionDef
{
    // PotionOnUseSupport.Use, Duplicator case: Apply<DuplicationPower>(player, 1, player).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.DuplicationPower>(), 1, BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>BoneBrew</c>: <c>combat.SummonOsty(simulator, PlayerTarget(), Summon)</c>
/// (<c>PotionOnUseSupport.Use</c> case BoneBrew).
/// </summary>
[BlitzContent("BONE_BREW")]
public sealed class BoneBrewPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.SummonOsty(ref x, (int)BlitzKernel.PotionVar(x, this, 0));
    }
}

/// <summary>
/// <c>CunningPotion</c>: <c>CardPileOnPlaySupport.GenerateShivs(..., Cards, upgraded: true)</c>
/// (<c>PotionOnUseSupport.Use</c> case CunningPotion).
/// </summary>
[BlitzContent("CUNNING_POTION")]
public sealed class CunningPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 1)];

    public override void OnUse(ref BlitzExec x)
    {
        Cards.Silent.SilentContent.GenerateShivs(ref x, (int)BlitzKernel.PotionVar(x, this, 0), upgraded: true);
    }
}

/// <summary><c>PotionOfCapacity</c>: <c>simulator.AddOrbSlots(PlayerTarget(), Repeat)</c>.</summary>
[BlitzContent("POTION_OF_CAPACITY")]
public sealed class PotionOfCapacity : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.AddOrbSlots(ref x.B, (int)BlitzKernel.PotionVar(x, this, 0));
    }
}

[BlitzContent("ASHWATER")]
public sealed class Ashwater : BlitzPotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(Exhaust, Hand, 0..Hand.Count, every hand card), in
    // hand order. Apply exhausts the selected cards in selection order through the normal Exhaust
    // hooks, so a hook that asks another choice stops the potion exactly like the existing engine.
    public override void OnUse(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.Exhaust, BlitzPile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.ExhaustSelected(ref x, BlitzPile.Hand, selected[..chosen]);
    }
}

[BlitzContent("BLESSING_OF_THE_FORGE")]
public sealed class BlessingOfTheForge : BlitzPotionDef
{
    // BlessingOfTheForge: upgrade every upgradable card in the player's hand.
    public override void OnUse(ref BlitzExec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
            BlitzKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }
}

[BlitzContent("CLARITY")]
public sealed class Clarity : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ClarityPower", "Cards"];

    // Clarity: Draw(player, Cards), then Apply<ClarityPower>(player, ClarityPower, owner).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 1), fromHandDraw: false);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.ClarityPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("CURE_ALL")]
public sealed class CureAll : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CureAll: GainEnergy(player, Energy), then Draw(player, Cards).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, BlitzKernel.PotionVar(x, this, 0));
        BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 1), fromHandDraw: false);
    }
}

[BlitzContent("FORTIFIER")]
public sealed class Fortifier : BlitzPotionDef
{
    // Fortifier: GainBlock(player, player.Block * 2, Unpowered).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, x.B.Player.Block * 2,
            BlitzValueProps.Unpowered, fromCard: false);
}

[BlitzContent("GAMBLERS_BREW")]
public sealed class GamblersBrew : BlitzPotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(DiscardAndDraw, Hand, 0..Hand.Count, every hand card),
    // in hand order. Apply calls simulator.DiscardAndDraw(selected, selected.Count): discard each
    // selected card through the normal hook/Sly pipeline, then draw as many, then auto-play the
    // cards that were Sly at discard time.
    public override void OnUse(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.DiscardAndDraw, BlitzPile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.DiscardAndDraw(ref x, BlitzPile.Hand, selected[..chosen], chosen);
    }
}

[BlitzContent("RADIANT_TINCTURE")]
public sealed class RadiantTincture : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "RadiancePower"];

    // RadiantTincture: GainEnergy(player, Energy), then Apply<RadiancePower>(player, RadiancePower, owner).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.GainEnergy(ref x, BlitzKernel.PotionVar(x, this, 0));
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RadiancePower>(),
            (int)BlitzKernel.PotionVar(x, this, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("REGEN_POTION")]
public sealed class RegenPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RegenPower"];

    // RegenPotion: combat.Apply<RegenPower>(player, RegenPower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RegenPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("STABLE_SERUM")]
public sealed class StableSerum : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // StableSerum: combat.Apply<RetainHandPower>(player, Repeat, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RetainHandPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("TOUCH_OF_INSANITY")]
public sealed class TouchOfInsanity : BlitzPotionDef
{
    // PotionChoiceSupport.GetSpec: ExactSpec(SetFreeThisCombat, Hand, hand cards that cost energy
    // or stars). CostsEnergyOrStars is checked first with local modifiers and then with global
    // modifiers; the union is the option list because either predicate includes the card. Apply
    // calls SetToFreeThisCombat on the selected card and does not move it.
    public override void OnUse(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int optionCount = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (CostsEnergyOrStars(ref x, x.B.Cards[index]))
                options[optionCount++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.SetFreeThisCombat, BlitzPile.Hand,
                options[..optionCount], 1, 1, selected, out int chosen))
        {
            return;
        }
        int handStart = BlitzPiles.Start(x.B, BlitzPile.Hand);
        for (int index = 0; index < chosen; index++)
        {
            ref BlitzCard card = ref x.B.Cards[handStart + selected[index]];
            BlitzKernel.MakeFreeThisCombat(x.R, ref card);
        }
    }

    /// <summary>
    /// <c>CardModel.CostsEnergyOrStars</c> (CardModel.cs:1579-1595): the local branch asks
    /// <c>EnergyCost.GetWithModifiers(CostModifiers.Local) &gt; 0</c> or <c>CurrentStarCost &gt; 0</c>;
    /// the global branch adds <c>!EnergyCost.CostsX &amp;&amp; GetWithModifiers(All) &gt; 0</c> and
    /// <c>!HasStarCostX &amp;&amp; GetStarCostWithModifiers() &gt; 0</c>. X-energy costs stay at the
    /// canonical placeholder 0 in both branches: <c>CardEnergyCost.GetWithModifiers</c> returns it
    /// before applying modifiers (CardEnergyCost.cs:94-120), and the global branch skips CostsX.
    /// X-star costs are likewise skipped in the global branch, while the local branch still uses
    /// CurrentStarCost (a positive temporary star cost on an X-star card is costly natively too).
    /// The spend-time mirrors <c>EnergyCostWithModifiers</c>/<c>StarCostWithModifiers</c> return all
    /// current resources for X (GetAmountToSpend/GetStarCostWithModifiers), so they must not feed
    /// these predicate branches. An Unplayable card keeps its negative base and is not costly; a
    /// card whose local modifier sets the cost to 0 can still be costly through the global hook
    /// (the union is intentional). ReplayCount does not participate in either branch.
    /// </summary>
    private static bool CostsEnergyOrStars(ref BlitzExec x, in BlitzCard card)
    {
        // Local branch: GetWithModifiers(Local) short-circuits CostsX to the placeholder 0, so do
        // not let a local modifier make an X-cost card selectable here.
        int localEnergy = (card.Flags & BlitzCardFlags.CostsX) != 0
            ? 0
            : BlitzKernel.EnergyCostWithLocalModifiers(x.B, card);
        int localStar = card.HasTemporaryStarCost ? card.TemporaryStarCost : card.BaseStarCost;
        if (localEnergy > 0 || localStar > 0)
            return true;
        // Global branch: the native method skips X-cost cards before consulting the hook-modified
        // spend value. Without these gates the X card would inherit the player's current
        // energy/stars (the diff-NECROBINDER-022 failure) and gain a spurious option.
        if ((card.Flags & BlitzCardFlags.CostsX) == 0
            && BlitzKernel.EnergyCostWithModifiers(x.B, x.R, card, BlitzPile.Hand) > 0)
        {
            return true;
        }
        if ((card.Flags & BlitzCardFlags.StarCostsX) == 0
            && BlitzKernel.StarCostWithModifiers(x.B, x.R, card, BlitzPile.Hand) > 0)
        {
            return true;
        }
        return false;
    }
}

[BlitzContent("KINGS_COURAGE")]
public sealed class KingsCourage : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards =>
        [(BlitzKernel.SovereignBladeId, 0)];

    // KingsCourage: ForgeCmd.Forge(Forge variable, target.Player, this); the single-player target is
    // the player (PotionOnUseSupport.Use case KingsCourage).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.Forge(ref x, (int)BlitzKernel.PotionVar(x, this, 0));
    }
}
