using BlitzSolver.Engine;

namespace BlitzSolver.Content.Potions;

/// <summary>
/// Native <c>DistilledChaos</c>: <c>AutoPlayPotionMirrors.DistilledChaosOnUse</c> calls
/// <c>simulator.AutoPlayFromDrawPile(player, Repeat, CardPilePosition.Top)</c> with
/// <c>forceExhaust: false</c>, i.e. it moves up to Repeat cards from the draw-pile top to the play
/// pile and auto-plays them in order with their normal result piles.
/// </summary>
[BlitzContent("DISTILLED_CHAOS")]
public sealed class DistilledChaos : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Repeat"];

    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.AutoPlayFromDrawPile(ref x, (int)BlitzKernel.PotionVar(x, this, 0), forceExhaust: false);
}

[BlitzContent("OROBIC_ACID")]
public sealed class OrobicAcid : BlitzPotionDef
{
    // One distinct Attack, then Skill, then Power from the character pool; the Attack/Skill/Power
    // union is the whole character pool, so the full pool is the exact closure.
    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.CharacterAll];

    public override void OnUse(ref BlitzExec x)
    {
        Span<BlitzCard> generated = stackalloc BlitzCard[3];
        Span<BlitzCard> one = stackalloc BlitzCard[1];
        int count = 0;
        if (BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Attack, 1, one) == 1)
            generated[count++] = one[0];
        if (BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Skill, 1, one) == 1)
            generated[count++] = one[0];
        if (BlitzCardEffects.SampleCharacterCardsOfType(ref x.B, x.R, BlitzCardType.Power, 1, one) == 1)
            generated[count++] = one[0];
        // CardGenerationPotionMirrors.GenerateOrobicAcidCards: SetToFreeThisTurn on every generated
        // card, then AddGeneratedCardsToCombat(hand, Bottom) keeps that order.
        for (int index = 0; index < count; index++)
        {
            BlitzKernel.MakeFreeThisTurn(x.R, ref generated[index]);
            BlitzKernel.AddGeneratedCard(ref x, generated[index], BlitzPile.Hand);
        }
    }
}

[BlitzContent("BEETLE_JUICE")]
public sealed class BeetleJuice : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["DamageDecrease", "Repeat"];

    // BeetleJuice when target != null: combat.Apply<ShrinkPower>(target, Repeat.IntValue, owner).
    public override void OnUse(ref BlitzExec x)
    {
        if (x.Target == BlitzLimits.NoOwner)
            return;
        BlitzKernel.ApplyPower(ref x, x.Target, BlitzRegistry.PowerId<Powers.ShrinkPower>(),
            (int)BlitzKernel.PotionVar(x, this, 1), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("MAZALETHS_GIFT")]
public sealed class MazalethsGift : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["RitualPower"];

    // MazalethsGift: combat.Apply<RitualPower>(player, RitualPower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.RitualPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("SHACKLING_POTION")]
public sealed class ShacklingPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower"];

    // ShacklingPotion: combat.ApplyTemporaryStrengthLoss<ShacklingPotionPower> for each living
    // hittable enemy with the Strength amount.
    public override void OnUse(ref BlitzExec x)
    {
        int amount = (int)BlitzKernel.PotionVar(x, this, 0);
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = BlitzKernel.EnemyOwner(x.B.Roster[position]);
            if (!BlitzKernel.IsHittable(x.B, owner))
                continue;
            BlitzKernel.ApplyTemporaryStrength(ref x, owner, BlitzRegistry.PowerId<Powers.ShacklingPotionPower>(),
                amount, BlitzLimits.PlayerOwner, sign: -1);
        }
    }
}

[BlitzContent("GHOST_IN_A_JAR")]
public sealed class GhostInAJar : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["IntangiblePower"];

    // GhostInAJar: combat.Apply<IntangiblePower>(playerTarget, IntangiblePower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.IntangiblePower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("LUCKY_TONIC")]
public sealed class LuckyTonic : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["BufferPower"];

    // LuckyTonic: combat.Apply<BufferPower>(playerTarget, BufferPower, owner).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BufferPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

[BlitzContent("GIGANTIFICATION_POTION")]
public sealed class GigantificationPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["GigantificationPower"];

    public override IReadOnlyList<string> AppliedPowers => ["GIGANTIFICATION_POWER"];

    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner,
            BlitzRegistry.PowerId<Powers.GigantificationPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
}

/// <summary>
/// <c>EssenceOfDarkness</c>: channel one Dark orb per orb slot
/// (<c>OrbPotionMirrors.EssenceOfDarknessOnUse</c>). The count is the current queue capacity.
/// </summary>
[BlitzContent("ESSENCE_OF_DARKNESS")]
public sealed class EssenceOfDarkness : BlitzPotionDef
{
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.ChannelOrbs(ref x, BlitzKernel.OrbId("DARK_ORB"), x.B.OrbCapacity);
    }
}

/// <summary>
/// Native <c>FairyInABottle</c>: an automatic potion (<c>PotionOnUseSupport.CanSearch</c> is false),
/// so the search never offers an explicit use. Its <c>ShouldDie</c> prevents its owner's death and
/// <c>AfterPreventingDeath</c> consumes the slot and heals
/// <c>max(1, 30% of max HP)</c> (<c>FairyInABottleMirrors</c>).
/// </summary>
[BlitzContent("FAIRY_IN_A_BOTTLE")]
public sealed class FairyInABottle : BlitzPotionDef
{
    public override bool Searchable => false;

    // Never reached through the search; the effect runs from AfterPreventingDeath below.
    public override void OnUse(ref BlitzExec x)
        => throw new BlitzUnsupportedException("现有搜索不提供 FAIRY_IN_A_BOTTLE 的显式使用。");

    public override bool ShouldDie(ref BlitzExec x, BlitzListener self, byte creature)
        => creature != self.Owner;

    public override void AfterPreventingDeath(ref BlitzExec x, BlitzListener self, byte creature)
    {
        // PotionOnUseMirrors consume the slot, then the native OnUse heals max(1, 30% max HP).
        int freeMask = x.B.Counter(BlitzCounter.EntropicFreePotionMask);
        int slotBit = 1 << self.Index;
        bool freeFromEntropic = (freeMask & slotBit) != 0;
        if (freeFromEntropic)
            x.B.SetCounter(BlitzCounter.EntropicFreePotionMask, freeMask & ~slotBit);
        ushort storedPotion = x.B.Potions[self.Index];
        if (storedPotion != x.R.RootPotion(self.Index))
        {
            x.B.AddCounter(BlitzCounter.GeneratedPotionUseCount, 1);
            BlitzKernel.RecordGeneratedPotionStrategicCost(ref x.B,
                freeFromEntropic ? 0 : x.R.Potion((ushort)(storedPotion - 1)).StrategicCost, automatic: true);
        }
        x.B.Potions[self.Index] = 0;
        x.B.AddCounter(BlitzCounter.AutomaticPotionUseCount, 1);
        decimal amount = Math.Max(1m, x.B.Creature(creature).MaxHp * 0.3m);
        int hpBeforeRevive = x.B.Creature(creature).CurrentHp;
        BlitzKernel.Heal(ref x, creature, amount);
        int restored = x.B.Creature(creature).CurrentHp - hpBeforeRevive;
        if (restored > 0)
        {
            x.B.AddCounter(BlitzCounter.DeathSavePotionHpRestored, restored);
            x.B.AddCounter(BlitzCounter.DeathSaveUseCount, 1);
        }
        // FairyInABottleMirrors.AfterPreventingDeath calls ICombatPredictionEffectSink.AfterPotionUsed
        // while the revived player lives; the automatic use must reach ReptileTrinket/BeltBuckle too.
        if (x.B.Player.IsAlive)
            BlitzHooks.AfterPotionUsed(ref x, Id);
    }
}

/// <summary>
/// <c>CosmicConcoction</c>: <c>Cards</c> distinct colourless cards, each upgraded and added to hand
/// (<c>CardGenerationPotionMirrors.Generate</c> / <c>PotionOnUseSupport.Use</c> case CosmicConcoction).
/// </summary>
[BlitzContent("COSMIC_CONCOCTION")]
public sealed class CosmicConcoctionPotion : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<string> GeneratedCardPools => [BlitzCardPools.ColorlessAll];

    public override void OnUse(ref BlitzExec x)
    {
        int count = (int)BlitzKernel.PotionVar(x, this, 0);
        if (count <= 0)
            return;
        if (count > BlitzLimits.Cards)
            throw new BlitzCapacityException("GeneratedCards", BlitzLimits.Cards);
        Span<BlitzCard> selected = stackalloc BlitzCard[count];
        int sampled = BlitzKernel.SampleCardPool(ref x.B, x.R, BlitzCardPools.ColorlessAll, count,
            distinct: true, BlitzRngStream.CombatCardGeneration, selected);
        for (int index = 0; index < sampled; index++)
        {
            BlitzCard card = selected[index];
            BlitzKernel.UpgradeCard(x.R, ref card);
            if (!BlitzKernel.AddGeneratedCard(ref x, card, BlitzPile.Hand))
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
[BlitzContent("POT_OF_GHOULS")]
public sealed class PotOfGhouls : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnUse(ref BlitzExec x)
    {
        int count = (int)BlitzKernel.PotionVar(x, this, 0);
        for (int index = 0; index < count; index++)
        {
            BlitzCard soul = BlitzKernel.CreateCard(x.R, BlitzRegistry.CardId<Cards.Necrobinder.Soul>());
            if (!BlitzKernel.AddGeneratedCard(ref x, soul, BlitzPile.Hand))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

[BlitzContent("BOTTLED_POTENTIAL")]
public sealed class BottledPotential : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // BottledPotential: MoveHandToDrawPile(player), Shuffle(player), Draw(player, Cards).
    public override void OnUse(ref BlitzExec x)
    {
        while (x.B.HandCount > 0)
        {
            if (!BlitzKernel.MoveCard(ref x.B, BlitzPile.Hand, 0, BlitzPile.Draw, BlitzPilePosition.Bottom))
                break;
        }
        BlitzKernel.Shuffle(ref x);
        BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 0), fromHandDraw: false);
    }
}

[BlitzContent("DROPLET_OF_PRECOGNITION")]
public sealed class DropletOfPrecognition : BlitzPotionDef
{
    // DropletOfPrecognition is a no-op in PotionOnUseSupport.Use; its whole effect is
    // PotionChoiceSupport.GetSpec/Apply: ExactSpec(MoveToHand, Draw, the complete draw pile) and
    // AddToPile(selected, Hand, Bottom). The option order is the draw pile order (index 0 is the top,
    // which is also BlitzPile.Draw's order). A card played into a full hand goes to the discard pile
    // inside AddToPile, which BlitzKernel.MoveCard reproduces.
    public override void OnUse(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < x.B.DrawCount; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.MoveToHand, BlitzPile.Draw,
                options, 1, 1, selected, out int chosen))
        {
            return;
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Draw, selected[..chosen], BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

[BlitzContent("FRUIT_JUICE")]
public sealed class FruitJuice : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["MaxHp"];

    // FruitJuice: GainMaxHp(player, MaxHp).
    public override void OnUse(ref BlitzExec x)
        => BlitzKernel.GainMaxHp(ref x, BlitzLimits.PlayerOwner, BlitzKernel.PotionVar(x, this, 0));
}

[BlitzContent("LIQUID_MEMORIES")]
public sealed class LiquidMemories : BlitzPotionDef
{
    // PotionChoiceSupport.GetSpec: ExactSpec(MoveToHandFreeThisTurn, Discard, every discard-pile
    // card), in discard-pile order. Apply marks each selected card free for this turn before
    // AddToPile(selected, Hand), which appends at the bottom.
    public override void OnUse(ref BlitzExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!BlitzKernel.ChooseCardsInRange(ref x, BlitzChoiceEffect.MoveToHandFreeThisTurn,
                BlitzPile.Discard, options, 1, 1, selected, out int chosen))
        {
            return;
        }
        int discardStart = BlitzPiles.Start(x.B, BlitzPile.Discard);
        for (int index = 0; index < chosen; index++)
        {
            ref BlitzCard card = ref x.B.Cards[discardStart + selected[index]];
            BlitzKernel.MakeFreeThisTurn(x.R, ref card);
        }
        BlitzKernel.MoveSelected(ref x.B, BlitzPile.Discard, selected[..chosen], BlitzPile.Hand,
            BlitzPilePosition.Bottom);
    }
}

[BlitzContent("SHIP_IN_A_BOTTLE")]
public sealed class ShipInABottle : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    // ShipInABottle: GainBlock(player, Block, Unpowered), then Apply<BlockNextTurnPower>(player, Block, owner).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.GainBlock(ref x, BlitzLimits.PlayerOwner, BlitzKernel.PotionVar(x, this, 0),
            BlitzValueProps.Unpowered, fromCard: false);
        BlitzKernel.ApplyPower(ref x, BlitzLimits.PlayerOwner, BlitzRegistry.PowerId<Powers.BlockNextTurnPower>(),
            (int)BlitzKernel.PotionVar(x, this, 0), BlitzLimits.PlayerOwner);
    }
}

[BlitzContent("SOLDIERS_STEW")]
public sealed class SoldiersStew : BlitzPotionDef
{
    // SoldiersStew: BaseReplayCount++ for every card in containment whose canonical tags include Strike.
    public override void OnUse(ref BlitzExec x)
    {
        for (int index = 0; index < x.B.CardCount; index++)
        {
            ref BlitzCard card = ref x.B.Cards[index];
            if ((x.R.Card(card.Id, card.Upgrade).Tags & BlitzCardTag.Strike) == 0)
                continue;
            card.ReplayCount = checked((byte)(card.ReplayCount + 1));
        }
    }
}

[BlitzContent("SNECKO_OIL")]
public sealed class SneckoOil : BlitzPotionDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    // SneckoOil (DrawPotionMirrors.SneckoOilOnUse): draw Cards, then for each card in the resulting
    // hand that does not cost X and whose canonical cost is not negative, append
    // CardEnergyCost.SetThisTurnOrUntilPlayed(CombatEnergyCosts.NextInt(4)): an absolute local
    // modifier that expires at the turn end or when the card is played (both expiration bits).
    public override void OnUse(ref BlitzExec x)
    {
        BlitzKernel.Draw(ref x, (int)BlitzKernel.PotionVar(x, this, 0), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            ref BlitzCard card = ref x.B.Cards[index];
            if ((card.Flags & BlitzCardFlags.CostsX) != 0 || card.BaseCost < 0)
                continue;
            int cost = x.B.RngStream(BlitzRngStream.CombatEnergyCosts).NextInt(4);
            if (card.EnergyModifierCount >= BlitzLimits.CardEnergyCostModifiers)
                throw new BlitzCapacityException("Card.EnergyModifiers", BlitzLimits.CardEnergyCostModifiers);
            card.EnergyModifiers[card.EnergyModifierCount] = BlitzCostModifier.Create(cost,
                BlitzCostType.Absolute,
                BlitzCostExpiration.EndOfTurn | BlitzCostExpiration.WhenPlayed, reduceOnly: false);
            card.EnergyModifierCount++;
        }
    }
}

/// <summary>
/// Native <c>EntropicBrew</c>: while the player has an open potion slot, draw a random potion from
/// <c>PotionFactory.CreateRandomPotionOutOfCombat</c> (the unfiltered character + shared pool) and
/// procure it with <c>freeFromEntropic: true</c>; stop when procurement fails
/// (<c>PotionOnUseSupport.Use</c> EntropicBrew case). The free flag matters only for the later
/// use's strategic cost, so procurement marks the slot in <see cref="BlitzCounter.EntropicFreePotionMask"/>.
/// </summary>
[BlitzContent("ENTROPIC_BREW")]
public sealed class EntropicBrew : BlitzPotionDef
{
    public override bool GeneratesOutOfCombatPotion => true;

    public override void OnUse(ref BlitzExec x)
    {
        for (int guard = 0; guard < BlitzLimits.PotionSlots; guard++)
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
            ushort potion = BlitzKernel.GenerateRandomPotionOutOfCombat(ref x.B, x.R);
            if (!BlitzKernel.TryProcurePotion(ref x.B, potion, freeFromEntropic: true))
                return;
        }
    }
}
