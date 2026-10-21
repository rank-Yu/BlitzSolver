using CombatSolver.Flash.Kernel;

namespace CombatSolver.Flash.Content.Potions;

/// <summary>
/// Native <c>DistilledChaos</c>: <c>AutoPlayPotionMirrors.DistilledChaosOnUse</c> calls
/// <c>simulator.AutoPlayFromDrawPile(player, Repeat, CardPilePosition.Top)</c> with
/// <c>forceExhaust: false</c>, i.e. it moves up to Repeat cards from the draw-pile top to the play
/// pile and auto-plays them in order with their normal result piles.
/// </summary>
[FlashContent("DISTILLED_CHAOS")]
public sealed class DistilledChaos : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref FlashExec x)
        => FlashKernel.AutoPlayFromDrawPile(ref x, (int)FlashKernel.PotionVar(x, this, 0), forceExhaust: false);
}

[FlashContent("OROBIC_ACID")]
public sealed class OrobicAcid : FlashPotionDef
{
    // One distinct Attack, then Skill, then Power from the character pool; the Attack/Skill/Power
    // union is the whole character pool, so the full pool is the exact closure.
    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.CharacterAll];

    public override void OnUse(ref FlashExec x)
    {
        Span<FlashCard> generated = stackalloc FlashCard[3];
        Span<FlashCard> one = stackalloc FlashCard[1];
        int count = 0;
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Attack, 1, one) == 1)
            generated[count++] = one[0];
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Skill, 1, one) == 1)
            generated[count++] = one[0];
        if (FlashCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, FlashCardType.Power, 1, one) == 1)
            generated[count++] = one[0];
        // CardGenerationPotionMirrors.GenerateOrobicAcidCards: SetToFreeThisTurn on every generated
        // card, then AddGeneratedCardsToCombat(hand, Bottom) keeps that order.
        for (int index = 0; index < count; index++)
        {
            FlashKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            FlashKernel.AddGeneratedCard(ref x, generated[index], FlashPile.Hand);
        }
    }
}

[FlashContent("BEETLE_JUICE")]
public sealed class BeetleJuice : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease", "Repeat"];

    // BeetleJuice when target != null: combat.Apply<ShrinkPower>(target, Repeat.IntValue, owner).
    public override void OnUse(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.ShrinkPower>(),
            (int)FlashKernel.PotionVar(x, this, 1), FlashLimits.PlayerOwner);
    }
}

[FlashContent("MAZALETHS_GIFT")]
public sealed class MazalethsGift : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RitualPower"];

    // MazalethsGift: combat.Apply<RitualPower>(player, RitualPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.RitualPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("SHACKLING_POTION")]
public sealed class ShacklingPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // ShacklingPotion: combat.ApplyTemporaryStrengthLoss<ShacklingPotionPower> for each living
    // hittable enemy with the Strength amount.
    public override void OnUse(ref FlashExec x)
    {
        int amount = (int)FlashKernel.PotionVar(x, this, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = FlashKernel.EnemyOwner(x.B.Roster[position]);
            if (!FlashKernel.IsHittable(x.B, owner))
                continue;
            FlashKernel.ApplyTemporaryStrength(ref x, owner, FlashRegistry.PowerId<Powers.ShacklingPotionPower>(),
                amount, FlashLimits.PlayerOwner, sign: -1);
        }
    }
}

[FlashContent("GHOST_IN_A_JAR")]
public sealed class GhostInAJar : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower"];

    // GhostInAJar: combat.Apply<IntangiblePower>(playerTarget, IntangiblePower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.IntangiblePower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("LUCKY_TONIC")]
public sealed class LuckyTonic : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    // LuckyTonic: combat.Apply<BufferPower>(playerTarget, BufferPower, owner).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BufferPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

[FlashContent("GIGANTIFICATION_POTION")]
public sealed class GigantificationPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["GigantificationPower"];

    public override IReadOnlyList<string> AppliedPowers => ["GIGANTIFICATION_POWER"];

    public override void OnUse(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.GigantificationPower>(),
            (int)FlashKernel.PotionVar(x, this, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>EssenceOfDarkness</c>: channel one Dark orb per orb slot
/// (<c>OrbPotionMirrors.EssenceOfDarknessOnUse</c>). The count is the current queue capacity.
/// </summary>
[FlashContent("ESSENCE_OF_DARKNESS")]
public sealed class EssenceOfDarkness : FlashPotionDef
{
    public override void OnUse(ref FlashExec x)
    {
        FlashKernel.ChannelOrbs(ref x, FlashKernel.OrbId("DARK_ORB"), x.B.OrbCapacity);
    }
}

/// <summary>
/// Native <c>FairyInABottle</c>: an automatic potion (<c>PotionOnUseSupport.CanSearch</c> is false),
/// so the search never offers an explicit use. Its <c>ShouldDie</c> prevents its owner's death and
/// <c>AfterPreventingDeath</c> consumes the slot and heals
/// <c>max(1, 30% of max HP)</c> (<c>FairyInABottleMirrors</c>).
/// </summary>
[FlashContent("FAIRY_IN_A_BOTTLE")]
public sealed class FairyInABottle : FlashPotionDef
{
    public override bool Searchable => false;

    // Never reached through the search; the effect runs from AfterPreventingDeath below.
    public override void OnUse(ref FlashExec x)
        => throw new FlashUnsupportedException("现有搜索不提供 FAIRY_IN_A_BOTTLE 的显式使用。");

    public override bool ShouldDie(ref FlashExec x, FlashListener self, byte creature)
        => creature != self.Owner;

    public override void AfterPreventingDeath(ref FlashExec x, FlashListener self, byte creature)
    {
        // PotionOnUseMirrors consume the slot, then the native OnUse heals max(1, 30% max HP).
        int freeMask = x.B.Counter(FlashCounter.EntropicFreePotionMask);
        int slotBit = 1 << self.Index;
        bool freeFromEntropic = (freeMask & slotBit) != 0;
        if (freeFromEntropic)
            x.B.SetCounter(FlashCounter.EntropicFreePotionMask, freeMask & ~slotBit);
        ushort storedPotion = x.B.Potions[self.Index];
        if (storedPotion != x.R.RootPotion(self.Index))
        {
            x.B.AddCounter(FlashCounter.GeneratedPotionUseCount, 1);
            FlashKernel.RecordGeneratedPotionStrategicCost(ref x.B,
                freeFromEntropic ? 0 : x.R.Potion((ushort)(storedPotion - 1)).StrategicCost, automatic: true);
        }
        x.B.Potions[self.Index] = 0;
        x.B.AddCounter(FlashCounter.AutomaticPotionUseCount, 1);
        decimal amount = Math.Max(1m, x.B.Creature(creature).MaxHp * 0.3m);
        int hpBeforeRevive = x.B.Creature(creature).CurrentHp;
        FlashKernel.Heal(ref x, creature, amount);
        int restored = x.B.Creature(creature).CurrentHp - hpBeforeRevive;
        if (restored > 0)
        {
            x.B.AddCounter(FlashCounter.DeathSavePotionHpRestored, restored);
            x.B.AddCounter(FlashCounter.DeathSaveUseCount, 1);
        }
        // FairyInABottleMirrors.AfterPreventingDeath calls ICombatPredictionEffectSink.AfterPotionUsed
        // while the revived player lives; the automatic use must reach ReptileTrinket/BeltBuckle too.
        if (x.B.Player.IsAlive)
            FlashHooks.AfterPotionUsed(ref x, Id);
    }
}

/// <summary>
/// <c>CosmicConcoction</c>: <c>Cards</c> distinct colourless cards, each upgraded and added to hand
/// (<c>CardGenerationPotionMirrors.Generate</c> / <c>PotionOnUseSupport.Use</c> case CosmicConcoction).
/// </summary>
[FlashContent("COSMIC_CONCOCTION")]
public sealed class CosmicConcoctionPotion : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [FlashCardPools.ColorlessAll];

    public override void OnUse(ref FlashExec x)
    {
        int count = (int)FlashKernel.PotionVar(x, this, 0);
        if (count <= 0)
            return;
        if (count > FlashLimits.Cards)
            throw new FlashCapacityException("GeneratedCards", FlashLimits.Cards);
        Span<FlashCard> selected = stackalloc FlashCard[count];
        int sampled = FlashKernel.SampleCardPool(ref x.B, x.R, FlashCardPools.ColorlessAll, count,
            distinct: true, FlashRngStream.CombatCardGeneration, selected);
        for (int index = 0; index < sampled; index++)
        {
            FlashCard card = selected[index];
            FlashKernel.UpgradeCard(x.R, ref card);
            if (!FlashKernel.AddGeneratedCard(ref x, card, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>PotOfGhouls</c>: creates <c>Cards</c> Soul cards and adds them to the hand bottom
/// (<c>PotionOnUseSupport.Use</c> case PotOfGhouls, CardGenerationResultKind.Fixed).
/// </summary>
[FlashContent("POT_OF_GHOULS")]
public sealed class PotOfGhouls : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnUse(ref FlashExec x)
    {
        int count = (int)FlashKernel.PotionVar(x, this, 0);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = FlashKernel.CreateCard(x.R, FlashRegistry.CardId<Cards.Necrobinder.Soul>());
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
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

[FlashContent("FRUIT_JUICE")]
public sealed class FruitJuice : FlashPotionDef
{
    public override IReadOnlyList<string> VarNames => ["MaxHp"];

    // FruitJuice: GainMaxHp(player, MaxHp).
    public override void OnUse(ref FlashExec x)
        => FlashKernel.GainMaxHp(ref x, FlashLimits.PlayerOwner, FlashKernel.PotionVar(x, this, 0));
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
