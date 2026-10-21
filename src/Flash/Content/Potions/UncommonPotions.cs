using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

[FlashContent("FYSH_OIL")]
public sealed class FyshOil : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "DexterityPower"];

    // FyshOil: Apply<StrengthPower>(player, Strength, owner), then Apply<DexterityPower>(player, Dexterity, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DexterityPower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("HEART_OF_IRON")]
public sealed class HeartOfIron : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["PlatingPower"];

    // HeartOfIron: combat.Apply<PlatingPower>(player, PlatingPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.PlatingPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("LIQUID_BRONZE")]
public sealed class LiquidBronze : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ThornsPower"];

    // LiquidBronze: combat.Apply<ThornsPower>(player, ThornsPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ThornsPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("POTION_OF_BINDING")]
public sealed class PotionOfBinding : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["VulnerablePower", "WeakPower"];

    // PotionOfBinding: for each hittable enemy, apply Weak with the VulnerablePower amount and
    // Vulnerable with the WeakPower amount (the existing engine's branch swaps the two variables).
    public override void OnUse(ref FlashExec x)
    {
        int weak = (int)FlashKernel.PotionVar(x, this, 0);
        int vulnerable = (int)FlashKernel.PotionVar(x, this, 1);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.WeakPower>(), weak,
                FlashLimits.PlayerOwner);
            FlashKernel.ApplyPower(ref x, owner, FlashRegistry.PowerId<Powers.VulnerablePower>(), vulnerable,
                FlashLimits.PlayerOwner);
        }
    }
}

[FlashContent("POWDERED_DEMISE")]
public sealed class PowderedDemise : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Demise"];

    // PowderedDemise when target != null: combat.Apply<DemisePower>(target, Demise, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.DemisePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("DUPLICATOR")]
public sealed class Duplicator : FlashPotionDef
{
    // PotionOnUseSupport.Use, Duplicator case: Apply<DuplicationPower>(player, 1, player).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.DuplicationPower>(), 1, FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>BoneBrew</c>: <c>combat.SummonOsty(simulator, PlayerTarget(), Summon)</c>
/// (<c>PotionOnUseSupport.Use</c> case BoneBrew).
/// </summary>
[FlashContent("BONE_BREW")]
public sealed class BoneBrewPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}

/// <summary>
/// <c>CunningPotion</c>: <c>CardPileOnPlaySupport.GenerateShivs(..., Cards, upgraded: true)</c>
/// (<c>PotionOnUseSupport.Use</c> case CunningPotion).
/// </summary>
[FlashContent("CUNNING_POTION")]
public sealed class CunningPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SHIV", 1)];

    public override void OnUse(ref FlashExec x)
    {
        Cards.Silent.SilentContent.GenerateShivs(ref x, (int)FlashKernel.PotionVar(x, this, 0), upgraded: true);
    }
}

/// <summary><c>PotionOfCapacity</c>: <c>simulator.AddOrbSlots(PlayerTarget(), Repeat)</c>.</summary>
[FlashContent("POTION_OF_CAPACITY")]
public sealed class PotionOfCapacity : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.AddOrbSlots(ref x.B, (int)FlashKernel.PotionVar(x, this, 0));
    }
}

[FlashContent("ASHWATER")]
public sealed class Ashwater : FlashPotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(Exhaust, Hand, 0..Hand.Count, every hand card), in
    // hand order. Apply exhausts the selected cards in selection order through the normal Exhaust
    // hooks, so a hook that asks another choice stops the potion exactly like the existing engine.
    public override void OnUse(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.Exhaust, FlashPile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        FlashKernel.ExhaustSelected(ref x, FlashPile.Hand, selected[..chosen]);
    }
}

[FlashContent("BLESSING_OF_THE_FORGE")]
public sealed class BlessingOfTheForge : FlashPotionDef
{
    // BlessingOfTheForge: upgrade every upgradable card in the player's hand.
    public override void OnUse(ref FlashExec x)
    {
        for (int index = 0; index < x.B.HandCount; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[index]);
    }
}

[FlashContent("CLARITY")]
public sealed class Clarity : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["ClarityPower", "Cards"];

    // Clarity: Draw(player, Cards), then Apply<ClarityPower>(player, ClarityPower, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 1), fromHandDraw: false);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ClarityPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("CURE_ALL")]
public sealed class CureAll : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    // CureAll: GainEnergy(player, Energy), then Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashKernel.PotionVar(x, this, 0));
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 1), fromHandDraw: false);
    }
}

[FlashContent("FORTIFIER")]
public sealed class Fortifier : FlashPotionDef
{
    // Fortifier: GainBlock(player, player.Block * 2, Unpowered).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, x.B.Player.Block * 2,
            FlashValueProps.Unpowered, fromCard: false);
}

[FlashContent("GAMBLERS_BREW")]
public sealed class GamblersBrew : FlashPotionDef
{
    // PotionChoiceSupport.GetSpec: RangeSpec(DiscardAndDraw, Hand, 0..Hand.Count, every hand card),
    // in hand order. Apply calls simulator.DiscardAndDraw(selected, selected.Count): discard each
    // selected card through the normal hook/Sly pipeline, then draw as many, then auto-play the
    // cards that were Sly at discard time.
    public override void OnUse(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[x.B.HandCount];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.DiscardAndDraw, FlashPile.Hand,
                options, 0, options.Length, selected, out int chosen))
        {
            return;
        }
        FlashKernel.DiscardAndDraw(ref x, FlashPile.Hand, selected[..chosen], chosen);
    }
}

[FlashContent("RADIANT_TINCTURE")]
public sealed class RadiantTincture : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "RadiancePower"];

    // RadiantTincture: GainEnergy(player, Energy), then Apply<RadiancePower>(player, RadiancePower, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, FlashKernel.PotionVar(x, this, 0));
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RadiancePower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("REGEN_POTION")]
public sealed class RegenPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RegenPower"];

    // RegenPotion: combat.Apply<RegenPower>(player, RegenPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RegenPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("STABLE_SERUM")]
public sealed class StableSerum : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    // StableSerum: combat.Apply<RetainHandPower>(player, Repeat, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RetainHandPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("TOUCH_OF_INSANITY")]
public sealed class TouchOfInsanity : FlashPotionDef
{
    // PotionChoiceSupport.GetSpec: ExactSpec(SetFreeThisCombat, Hand, hand cards that cost energy
    // or stars). CostsEnergyOrStars is checked first with local modifiers and then with global
    // modifiers; the union is the option list because either predicate includes the card. Apply
    // calls SetToFreeThisCombat on the selected card and does not move it.
    public override void OnUse(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        int optionCount = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (CostsEnergyOrStars(ref x, x.B.Cards[index]))
                options[optionCount++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCardsInRange(ref x, FlashChoiceEffect.SetFreeThisCombat, FlashPile.Hand,
                options[..optionCount], 1, 1, selected, out int chosen))
        {
            return;
        }
        int handStart = FlashPiles.Start(x.B, FlashPile.Hand);
        for (int index = 0; index < chosen; index++)
        {
            ref FlashCard card = ref x.B.Cards[handStart + selected[index]];
            FlashKernel.MakeFreeThisCombat(x.R, ref card);
        }
    }

    /// <summary>
    /// <c>CardModel.CostsEnergyOrStars</c>: the first check uses local modifiers only; the second
    /// includes the combat cost hooks. A negative (Unplayable) or X base cost is not costly in the
    /// local check, but an X cost can become costly through <c>EnergyCostWithModifiers</c>.
    /// </summary>
    private static bool CostsEnergyOrStars(ref FlashExec x, in FlashCard card)
    {
        int localEnergy = FlashKernel.EnergyCostWithLocalModifiers(x.B, card);
        int localStar = card.HasTemporaryStarCost ? card.TemporaryStarCost : card.BaseStarCost;
        if (localEnergy > 0 || localStar > 0)
            return true;
        return FlashKernel.EnergyCostWithModifiers(x.B, x.R, card, FlashPile.Hand) > 0
            || FlashKernel.StarCostWithModifiers(x.B, x.R, card, FlashPile.Hand) > 0;
    }
}

[FlashContent("KINGS_COURAGE")]
public sealed class KingsCourage : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Forge"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards =>
        [(FlashKernel.SovereignBladeId, 0)];

    // KingsCourage: ForgeCmd.Forge(Forge variable, target.Player, this); the single-player target is
    // the player (PotionOnUseSupport.Use case KingsCourage).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.Forge(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}
