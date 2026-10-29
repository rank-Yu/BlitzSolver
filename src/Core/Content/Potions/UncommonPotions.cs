using Sts2Solver.Simulation.Combat;

namespace Sts2Solver.Content.Potions;

[ContentId("FYSH_OIL")]
public sealed class FyshOil : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    // FyshOil: Apply<StrengthPower>(player, Strength, owner), then Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref Exec x)
    {
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.StrengthPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.DexterityPower>(),
            (int)Kernel.PotionVar(x, this, 1), Limits.PlayerOwner);
    }
}

[ContentId("HEART_OF_IRON")]
public sealed class HeartOfIron : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    // HeartOfIron: combat.Apply<PlatingPower>(player, PlatingPower, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.PlatingPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("LIQUID_BRONZE")]
public sealed class LiquidBronze : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // LiquidBronze: combat.Apply<ThornsPower>(player, ThornsPower, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ThornsPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("POTION_OF_BINDING")]
public sealed class PotionOfBinding : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower", "WeakPower"];

    // PotionOfBinding: for each hittable enemy, apply Weak with the VulnerablePower amount and
    // Vulnerable with the WeakPower amount (the existing engine's branch swaps the two variables).
    public override void OnUse(ref Exec x)
    {
        int weak = (int)Kernel.PotionVar(x, this, 0);
        int vulnerable = (int)Kernel.PotionVar(x, this, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = Kernel.EnemyOwner(x.B.Roster[position]);
            if (!Kernel.IsHittable(x.B, owner))
                continue;
            Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.WeakPower>(), weak,
                Limits.PlayerOwner);
            Kernel.ApplyPower(ref x, owner, Registry.PowerId<Powers.VulnerablePower>(), vulnerable,
                Limits.PlayerOwner);
        }
    }
}

[ContentId("POWDERED_DEMISE")]
public sealed class PowderedDemise : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Demise"];

    // PowderedDemise when target != null: combat.Apply<DemisePower>(target, Demise, owner).
    public override void OnUse(ref Exec x)
    {
        if (x.Target == Limits.NoOwner)
            return;
        Kernel.ApplyPower(ref x, x.Target, Registry.PowerId<Powers.DemisePower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

[ContentId("DUPLICATOR")]
public sealed class Duplicator : PotionDef
{
    // PotionOnUseSupport.Use, Duplicator case: Apply<DuplicationPower>(player, 1, player).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner,
            Registry.PowerId<Powers.DuplicationPower>(), 1, Limits.PlayerOwner);
}

/// <summary>
/// <c>BoneBrew</c>: <c>combat.SummonOsty(simulator, PlayerTarget(), Summon)</c>
/// (<c>PotionOnUseSupport.Use</c> case BoneBrew).
/// </summary>
[ContentId("BONE_BREW")]
public sealed class BoneBrewPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnUse(ref Exec x)
    {
        Kernel.SummonOsty(ref x, (int)Kernel.PotionVar(x, this, 0));
    }
}

/// <summary>
/// <c>CunningPotion</c>: <c>CardPileOnPlaySupport.GenerateShivs(..., Cards, upgraded: true)</c>
/// (<c>PotionOnUseSupport.Use</c> case CunningPotion).
/// </summary>
[ContentId("CUNNING_POTION")]
public sealed class CunningPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 1)];

    public override void OnUse(ref Exec x)
    {
        Cards.Silent.SilentContent.GenerateShivs(ref x, (int)Kernel.PotionVar(x, this, 0), upgraded: true);
    }
}

/// <summary><c>PotionOfCapacity</c>: <c>simulator.AddOrbSlots(PlayerTarget(), Repeat)</c>.</summary>
[ContentId("POTION_OF_CAPACITY")]
public sealed class PotionOfCapacity : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref Exec x)
    {
        Kernel.AddOrbSlots(ref x.B, (int)Kernel.PotionVar(x, this, 0));
    }
}

[ContentId("ASHWATER")]
public sealed class Ashwater : PotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(Exhaust, Hand, 0..Hand.Count, every hand card), in
    // hand order. Apply exhausts the selected cards in selection order through the normal Exhaust
    // hooks, so a hook that asks another choice stops the potion exactly like the existing engine.
    public override void OnUse(ref Exec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!Kernel.ChooseCardsInRange(ref x, ChoiceEffect.Exhaust, Pile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        Kernel.ExhaustSelected(ref x, Pile.Hand, selected[..chosen]);
    }
}

[ContentId("BLESSING_OF_THE_FORGE")]
public sealed class BlessingOfTheForge : PotionDef
{
    // BlessingOfTheForge: upgrade every upgradable card in the player's hand.
    public override void OnUse(ref Exec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
            Kernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }
}

[ContentId("CLARITY")]
public sealed class Clarity : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["ClarityPower", "Cards"];

    // Clarity: Draw(player, Cards), then Apply<ClarityPower>(player, ClarityPower, owner).
    public override void OnUse(ref Exec x)
    {
        Kernel.Draw(ref x, (int)Kernel.PotionVar(x, this, 1), fromHandDraw: false);
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.ClarityPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
    }
}

[ContentId("CURE_ALL")]
public sealed class CureAll : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CureAll: GainEnergy(player, Energy), then Draw(player, Cards).
    public override void OnUse(ref Exec x)
    {
        Kernel.GainEnergy(ref x, Kernel.PotionVar(x, this, 0));
        Kernel.Draw(ref x, (int)Kernel.PotionVar(x, this, 1), fromHandDraw: false);
    }
}

[ContentId("FORTIFIER")]
public sealed class Fortifier : PotionDef
{
    // Fortifier: GainBlock(player, player.Block * 2, Unpowered).
    public override void OnUse(ref Exec x)
        => Kernel.GainBlock(ref x, Limits.PlayerOwner, x.B.Player.Block * 2,
            ValueProps.Unpowered, fromCard: false);
}

[ContentId("GAMBLERS_BREW")]
public sealed class GamblersBrew : PotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(DiscardAndDraw, Hand, 0..Hand.Count, every hand card),
    // in hand order. Apply calls simulator.DiscardAndDraw(selected, selected.Count): discard each
    // selected card through the normal hook/Sly pipeline, then draw as many, then auto-play the
    // cards that were Sly at discard time.
    public override void OnUse(ref Exec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!Kernel.ChooseCardsInRange(ref x, ChoiceEffect.DiscardAndDraw, Pile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        Kernel.DiscardAndDraw(ref x, Pile.Hand, selected[..chosen], chosen);
    }
}

[ContentId("RADIANT_TINCTURE")]
public sealed class RadiantTincture : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "RadiancePower"];

    // RadiantTincture: GainEnergy(player, Energy), then Apply<RadiancePower>(player, RadiancePower, owner).
    public override void OnUse(ref Exec x)
    {
        Kernel.GainEnergy(ref x, Kernel.PotionVar(x, this, 0));
        Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.RadiancePower>(),
            (int)Kernel.PotionVar(x, this, 1), Limits.PlayerOwner);
    }
}

[ContentId("REGEN_POTION")]
public sealed class RegenPotion : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["RegenPower"];

    // RegenPotion: combat.Apply<RegenPower>(player, RegenPower, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.RegenPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("STABLE_SERUM")]
public sealed class StableSerum : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // StableSerum: combat.Apply<RetainHandPower>(player, Repeat, owner).
    public override void OnUse(ref Exec x)
        => Kernel.ApplyPower(ref x, Limits.PlayerOwner, Registry.PowerId<Powers.RetainHandPower>(),
            (int)Kernel.PotionVar(x, this, 0), Limits.PlayerOwner);
}

[ContentId("TOUCH_OF_INSANITY")]
public sealed class TouchOfInsanity : PotionDef
{
    // PotionChoiceSupport.GetSpec: ExactSpec(SetFreeThisCombat, Hand, hand cards that cost energy
    // or stars). CostsEnergyOrStars is checked first with local modifiers and then with global
    // modifiers; the union is the option list because either predicate includes the card. Apply
    // calls SetToFreeThisCombat on the selected card and does not move it.
    public override void OnUse(ref Exec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int optionCount = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (CostsEnergyOrStars(ref x, x.B.Cards[index]))
                options[optionCount++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!Kernel.ChooseCardsInRange(ref x, ChoiceEffect.SetFreeThisCombat, Pile.Hand,
                options[..optionCount], 1, 1, selected, out int chosen))
        {
            return;
        }
        int handStart = Piles.Start(x.B, Pile.Hand);
        for (int index = 0; index < chosen; index++)
        {
            ref Card card = ref x.B.Cards[handStart + selected[index]];
            Kernel.MakeFreeThisCombat(x.R, ref card);
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
    private static bool CostsEnergyOrStars(ref Exec x, in Card card)
    {
        // Local branch: GetWithModifiers(Local) short-circuits CostsX to the placeholder 0, so do
        // not let a local modifier make an X-cost card selectable here.
        int localEnergy = (card.Flags & CardFlags.CostsX) != 0
            ? 0
            : Kernel.EnergyCostWithLocalModifiers(x.B, card);
        int localStar = card.HasTemporaryStarCost ? card.TemporaryStarCost : card.BaseStarCost;
        if (localEnergy > 0 || localStar > 0)
            return true;
        // Global branch: the native method skips X-cost cards before consulting the hook-modified
        // spend value. Without these gates the X card would inherit the player's current
        // energy/stars (the diff-NECROBINDER-022 failure) and gain a spurious option.
        if ((card.Flags & CardFlags.CostsX) == 0
            && Kernel.EnergyCostWithModifiers(x.B, x.R, card, Pile.Hand) > 0)
        {
            return true;
        }
        if ((card.Flags & CardFlags.StarCostsX) == 0
            && Kernel.StarCostWithModifiers(x.B, x.R, card, Pile.Hand) > 0)
        {
            return true;
        }
        return false;
    }
}

[ContentId("KINGS_COURAGE")]
public sealed class KingsCourage : PotionDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards =>
        [(Kernel.SovereignBladeId, 0)];

    // KingsCourage: ForgeCmd.Forge(Forge variable, target.Player, this); the single-player target is
    // the player (PotionOnUseSupport.Use case KingsCourage).
    public override void OnUse(ref Exec x)
    {
        Kernel.Forge(ref x, (int)Kernel.PotionVar(x, this, 0));
    }
}
