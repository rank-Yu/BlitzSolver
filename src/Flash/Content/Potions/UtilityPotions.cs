using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

// Potions whose existing-engine case heals, draws, moves cards, upgrades, or applies a resource or
// power without dealing damage. Each OnUse is the potion's branch in PotionOnUseSupport.Use.

[FlashContent("ASHWATER")]
public sealed class Ashwater : FlashPotionDef
{
    // Ashwater is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // hand-selection exhaust.
    public override void OnUse(ref FlashExec x)
    {
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

[FlashContent("BLOOD_POTION")]
public sealed class BloodPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["HealPercent"];

    // BloodPotion: Heal(player, MaxHp * HealPercent / 100m).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.Heal(ref x, FlashLimits.PlayerOwner,
            x.B.Player.MaxHp * FlashKernel.PotionVar(x, this, 0) / 100m);
}

[FlashContent("BOTTLED_POTENTIAL")]
public sealed class BottledPotential : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // BottledPotential: MoveHandToDrawPile(player), Shuffle(player), Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
    {
        while (x.B.HandCount > 0)
        {
            if (!FlashKernel.MoveCard(ref x.B, FlashPile.Hand, 0, FlashPile.Draw, FlashPilePosition.Bottom))
                break;
        }
        FlashKernel.Shuffle(ref x);
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
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

[FlashContent("DROPLET_OF_PRECOGNITION")]
public sealed class DropletOfPrecognition : FlashPotionDef
{
    // DropletOfPrecognition is a no-op in PotionOnUseSupport.Use; its whole effect is
    // PotionChoiceSupport.GetSpec/Apply: ExactSpec(MoveToHand, Draw, the complete draw pile) and
    // AddToPile(selected, Hand, Bottom). The option order is the draw pile order (index 0 is the top,
    // which is also FlashPile.Draw's order). A card played into a full hand goes to the discard pile
    // inside AddToPile, which FlashKernel.MoveCard reproduces.
    public override void OnUse(ref FlashExec x)
    {
        if (x.B.DrawCount == 0)
            return;
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < x.B.DrawCount; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Draw,
                options, 1, selected, out int chosen))
        {
            return;
        }
        FlashKernel.MoveSelected(ref x.B, FlashPile.Draw, selected[..chosen], FlashPile.Hand,
            FlashPilePosition.Bottom);
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

[FlashContent("FRUIT_JUICE")]
public sealed class FruitJuice : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["MaxHp"];

    // FruitJuice: GainMaxHp(player, MaxHp).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.GainMaxHp(ref x, FlashLimits.PlayerOwner, FlashKernel.PotionVar(x, this, 0));
}

[FlashContent("GAMBLERS_BREW")]
public sealed class GamblersBrew : FlashPotionDef
{
    // GamblersBrew is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // discard-and-draw hand selection.
    public override void OnUse(ref FlashExec x)
    {
    }
}

[FlashContent("GLOWWATER_POTION")]
public sealed class GlowwaterPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // GlowwaterPotion: ExhaustHand(player), then Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
    {
        int count = x.B.HandCount;
        for (int index = 0; index < count && x.B.HandCount > 0; index++)
        {
            FlashKernel.Exhaust(ref x, FlashPile.Hand, 0);
            if (x.NeedsChoice)
                return;
        }
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
    }
}

[FlashContent("LIQUID_MEMORIES")]
public sealed class LiquidMemories : FlashPotionDef
{
    // LiquidMemories is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // discard-pile selection.
    public override void OnUse(ref FlashExec x)
    {
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

[FlashContent("SHIP_IN_A_BOTTLE")]
public sealed class ShipInABottle : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // ShipInABottle: GainBlock(player, Block, Unpowered), then Apply<BlockNextTurnPower>(player, Block, owner).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainBlock(ref x, FlashLimits.PlayerOwner, FlashKernel.PotionVar(x, this, 0),
            FlashValueProps.Unpowered, fromCard: false);
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BlockNextTurnPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
    }
}

[FlashContent("SOLDIERS_STEW")]
public sealed class SoldiersStew : FlashPotionDef
{
    // SoldiersStew: BaseReplayCount++ for every card in containment whose canonical tags include Strike.
    public override void OnUse(ref FlashExec x)
    {
        for (int index = 0; index < x.B.CardCount; index++)
        {
            ref FlashCard card = ref x.B.Cards[index];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & FlashCardTag.Strike) == 0)
                continue;
            card.ReplayCount = checked((byte)(card.ReplayCount + 1));
        }
    }
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

[FlashContent("SWIFT_POTION")]
public sealed class SwiftPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SwiftPotion: Draw(player, Cards).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
}

[FlashContent("TOUCH_OF_INSANITY")]
public sealed class TouchOfInsanity : FlashPotionDef
{
    // TouchOfInsanity is a no-op in PotionOnUseSupport.Use: the current search does not resolve its
    // hand selection that makes a card free for the combat.
    public override void OnUse(ref FlashExec x)
    {
    }
}

[FlashContent("SNECKO_OIL")]
public sealed class SneckoOil : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SneckoOil (DrawPotionMirrors.SneckoOilOnUse): draw Cards, then for each card in the resulting
    // hand that does not cost X and whose canonical cost is not negative, append
    // CardEnergyCost.SetThisTurnOrUntilPlayed(CombatEnergyCosts.NextInt(4)): an absolute local
    // modifier that expires at the turn end or when the card is played (both expiration bits).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.Draw(ref x, (int)FlashKernel.PotionVar(x, this, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref FlashCard card = ref x.B.Cards[index];
            if ((card.Flags & FlashCardFlags.CostsX) != 0 || card.BaseCost < 0)
                continue;
            int cost = x.B.RngStream(FlashRngStream.CombatEnergyCosts).NextInt(4);
            if (card.EnergyModifierCount >= FlashLimits.CardEnergyCostModifiers)
                throw new FlashCapacityException("Card.EnergyModifiers", FlashLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = FlashCostModifier.Create(cost,
                FlashCostType.Absolute,
                FlashCostExpiration.EndOfTurn | FlashCostExpiration.WhenPlayed, reduceOnly: false);
            card.EnergyModifierCount++;
        }
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

[FlashContent("STAR_POTION")]
public sealed class StarPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Stars"];

    // StarPotion: PlayerCmd.GainStars(Stars variable, target.Player) through
    // CombatPredictionSimulator.GainStars (PotionOnUseSupport.Use case StarPotion).
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.GainStars(ref x, (int)FlashKernel.PotionVar(x, this, 0));
    }
}

/// <summary>
/// Native <c>EntropicBrew</c>: while the player has an open potion slot, draw a random potion from
/// <c>PotionFactory.CreateRandomPotionOutOfCombat</c> (the unfiltered character + shared pool) and
/// procure it with <c>freeFromEntropic: true</c>; stop when procurement fails
/// (<c>PotionOnUseSupport.Use</c> EntropicBrew case). The free flag matters only for the later
/// use's strategic cost, so procurement marks the slot in <see cref="FlashCounter.EntropicFreePotionMask"/>.
/// </summary>
[FlashContent("ENTROPIC_BREW")]
public sealed class EntropicBrew : FlashPotionDef
{
    public override bool GeneratesOutOfCombatPotion => true;

    public override void OnUse(ref FlashExec x)
    {
        for (int guard = 0; guard < FlashLimits.PotionSlots; guard++)
        {
            bool open = false;
            for (int slot = 0; slot < x.B.PotionSlotCount; slot++)
            {
                if (x.B.Potions[slot] == 0)
                {
                    open = true;
                    break;
                }
            }
            if (!open)
                return;
            ushort potion = FlashKernel.GenerateRandomPotionOutOfCombat(ref x.B, x.R);
            if (!FlashKernel.TryProcurePotion(ref x.B, potion, freeFromEntropic: true))
                return;
        }
    }
}
