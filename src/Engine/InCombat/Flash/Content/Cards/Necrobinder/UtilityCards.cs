namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Necrobinder;

// Summon, Soul and utility cards. Existing engine paths are named on each definition; the mirror
// phase (attacks, block, draw) always runs before the compensation phase, and these definitions keep
// that order.

/// <summary><c>Afterlife</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[FlashContent("AFTERLIFE")]
public sealed class Afterlife : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary><c>Reanimate</c>: summon Osty for the Summon variable (<c>CardOnPlaySupport.Batch043</c>).</summary>
[FlashContent("REANIMATE")]
public sealed class Reanimate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>Spur</c>: summon Osty for Summon, then heal it for Heal
/// (<c>CardOnPlaySupport.Batch043</c>'s Spur case).
/// </summary>
[FlashContent("SPUR")]
public sealed class Spur : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Heal"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.HealOsty(ref x, (int)NecrobinderContent.Var(ref x, 1));
    }
}

/// <summary>
/// <c>NecroMastery</c>: summon Osty, then apply one NecroMasteryPower
/// (<c>CardOnPlaySupport.Batch043</c>'s NecroMastery case).
/// </summary>
[FlashContent("NECRO_MASTERY")]
public sealed class NecroMastery : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NecroMasteryPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>PullAggro</c>: the general block recipe runs in the mirror phase, then summon Osty
/// (<c>CardEffectSpecRegistry</c>'s ResourceEffects completion).
/// </summary>
[FlashContent("PULL_AGGRO")]
public sealed class PullAggro : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Block"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 1);
        if (x.NeedsChoice)
            return;
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }
}

/// <summary>
/// <c>Cleanse</c>: summon Osty, then exhaust one draw-pile card
/// (<c>CardOnPlaySupport.Batch043</c> plus <c>CardChoiceSupport</c>'s Cleanse Exhaust spec).
/// </summary>
[FlashContent("CLEANSE")]
public sealed class Cleanse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.SummonOsty(ref x, (int)NecrobinderContent.Var(ref x, 0));
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DrawCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Exhaust, FlashPile.Draw, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.Exhaust(ref x, FlashPile.Draw, selected[0]);
    }
}

/// <summary>
/// <c>Dirge</c>: X summons plus X Soul cards added to the draw pile at random positions
/// (<c>CardOnPlaySupport.Batch043</c>'s ApplyDirge).
/// </summary>
[FlashContent("DIRGE")]
public sealed class Dirge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        int xValue = FlashKernel.ResolveEnergyXValue(ref x);
        int summon = (int)NecrobinderContent.Var(ref x, 0);
        for (int index = 0; index < xValue; index++)
        {
            FlashKernel.SummonOsty(ref x, summon);
            if (x.NeedsChoice)
                return;
        }
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < xValue; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random))
                return;
            if (x.NeedsChoice)
                return;
        }
    }
}

/// <summary><c>Wisp</c>: gain its Energy variable (<c>CardOnPlayCompensationCatalog</c>).</summary>
[FlashContent("WISP")]
public sealed class Wisp : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
}

/// <summary>
/// <c>BorrowedTime</c>: gain Energy, then apply BorrowedTimePower for ExtraCost
/// (<c>CardOnPlaySupport</c>: energy first, then the power).
/// </summary>
[FlashContent("BORROWED_TIME")]
public sealed class BorrowedTime : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "ExtraCost"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 0));
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.BorrowedTimePower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>EnfeeblingTouch</c>: apply a temporary Strength loss to the target
/// (<c>CardOnPlaySupport</c>'s ApplyTemporaryStrengthLoss).
/// </summary>
[FlashContent("ENFEEBLING_TOUCH")]
public sealed class EnfeeblingTouch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyTemporaryStrength(ref x, x.Target,
            FlashRegistry.PowerId<Powers.EnfeeblingTouchPower>(), (int)NecrobinderContent.Var(ref x, 0),
            FlashLimits.PlayerOwner, -1);
    }
}

/// <summary><c>Defy</c>: player block, then Weak on the target (<c>CardEffectSpecRegistry</c>).</summary>
[FlashContent("DEFY")]
public sealed class Defy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 1);
    }
}

/// <summary><c>Fear</c>: player attack, then Vulnerable on the target (<c>CorePowerSupport</c>).</summary>
[FlashContent("FEAR")]
public sealed class Fear : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

/// <summary>
/// <c>Hang</c>: attack, then apply HangPower with max(2, current amount) and the 999,999,999 cap
/// (<c>CorePowerSupport</c>'s Hang case).
/// </summary>
[FlashContent("HANG")]
public sealed class Hang : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        ushort hang = FlashRegistry.PowerId<Powers.HangPower>();
        int current = FlashKernel.GetAmount(x.B, x.Target, hang);
        int amount = Math.Max(2, current);
        if (current + amount > 999_999_999)
            amount = Math.Max(0, 999_999_999 - current);
        FlashKernel.ApplyPower(ref x, x.Target, hang, amount, FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>SharedFate</c>: lose PlayerStrengthLoss Strength, then apply EnemyStrengthLoss to the target
/// (<c>CorePowerSupport</c>'s SharedFate case).
/// </summary>
[FlashContent("SHARED_FATE")]
public sealed class SharedFate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["EnemyStrengthLoss", "PlayerStrengthLoss"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort strength = FlashRegistry.PowerId<Powers.StrengthPower>();
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, strength,
            -(int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
        if (x.NeedsChoice || x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, strength, -(int)NecrobinderContent.Var(ref x, 0),
            FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Friendship</c>: lose Strength, then apply FriendshipPower for its Energy
/// (<c>CorePowerSupport</c>'s Friendship case).
/// </summary>
[FlashContent("FRIENDSHIP")]
public sealed class Friendship : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["StrengthPower", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.StrengthPower>(),
            -(int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.FriendshipPower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Debilitate</c>: attack, then apply DebilitatePower to the target
/// (<c>CardEffectSpecRegistry</c>'s target DebilitatePower).
/// </summary>
[FlashContent("DEBILITATE")]
public sealed class Debilitate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "DebilitatePower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToTarget(ref x, FlashRegistry.PowerId<Powers.DebilitatePower>(), 1);
    }
}

/// <summary>
/// <c>Veilpiercer</c>: attack, then one VeilpiercerPower on the player
/// (<c>CardEffectSpecRegistry</c>'s owner VeilpiercerPower).
/// </summary>
[FlashContent("VEILPIERCER")]
public sealed class Veilpiercer : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.VeilpiercerPower>(), 1,
            FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Countdown</c>: apply CountdownPower for its amount (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("COUNTDOWN")]
public sealed class Countdown : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CountdownPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CountdownPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>Neurosurge</c>: gain energy, draw cards, then apply NeurosurgePower
/// (<c>CardDrawCardMirrors.NeurosurgeOnPlay</c>).
/// </summary>
[FlashContent("NEUROSURGE")]
public sealed class Neurosurge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["NeurosurgePower", "Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.GainEnergy(ref x, NecrobinderContent.Var(ref x, 1));
        if (x.NeedsChoice)
            return;
        FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 2), fromHandDraw: false);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.NeurosurgePower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary><c>Shroud</c>: apply ShroudPower for its Block variable (<c>CardPowerOnPlaySupport.SToZ</c>).</summary>
[FlashContent("SHROUD")]
public sealed class Shroud : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.ShroudPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Calcify</c>: apply CalcifyPower (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("CALCIFY")]
public sealed class Calcify : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalcifyPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.CalcifyPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Lethality</c>: apply LethalityPower (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("LETHALITY")]
public sealed class Lethality : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["LethalityPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.LethalityPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Haunt</c>: apply HauntPower for its HpLoss variable (<c>CardPowerOnPlaySupport</c>).</summary>
[FlashContent("HAUNT")]
public sealed class Haunt : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["HpLoss"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.HauntPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

/// <summary><c>Oblivion</c>: apply OblivionPower to the target for its Doom variable (<c>CardOnPlaySupport</c>).</summary>
[FlashContent("OBLIVION")]
public sealed class Oblivion : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DoomPower"];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        FlashKernel.ApplyPower(ref x, x.Target, FlashRegistry.PowerId<Powers.OblivionPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Eradicate</c>: an X-cost attack whose hit count is the resolved X value
/// (<c>GeneralCardMirrors</c> uses <c>ResolveEnergyXValue</c> for <c>AttackCommand.WithHitCount</c>).
/// </summary>
[FlashContent("ERADICATE")]
public sealed class Eradicate : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x)
        => FlashCardEffects.Attack(ref x, NecrobinderContent.Var(ref x, 0), FlashKernel.ResolveEnergyXValue(ref x));
}

[FlashContent("SOW")]
public sealed class Sow : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("BURY")]
public sealed class Bury : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("REAP")]
public sealed class Reap : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

[FlashContent("DEFILE")]
public sealed class Defile : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);
}

/// <summary>
/// <c>Graveblast</c>: attack, then move one discard-pile card to hand
/// (<c>CardChoiceSupport</c>'s MoveToHand spec over the discard pile).
/// </summary>
[FlashContent("GRAVEBLAST")]
public sealed class Graveblast : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Dredge</c>: move min(Cards, hand room) cards from the discard pile to hand
/// (<c>CardChoiceSupport</c>'s Dredge spec).
/// </summary>
[FlashContent("DREDGE")]
public sealed class Dredge : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        int room = Math.Max(0, x.B.MaxHandSize - x.B.HandCount);
        int count = Math.Min((int)NecrobinderContent.Var(ref x, 0), room);
        if (count <= 0 || x.B.DiscardCount == 0)
            return;
        Span<int> options = stackalloc int[x.B.DiscardCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[count];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.MoveToHand, FlashPile.Discard, options, count,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashKernel.MoveSelected(ref x.B, FlashPile.Discard, selected[..chosen], FlashPile.Hand,
                FlashPilePosition.Bottom);
    }
}

/// <summary>
/// <c>Severance</c>: attack, then three unupgraded Souls — one to the draw pile at a random position,
/// one to the discard pile, one to hand (<c>CardEffectSpecRegistry</c>'s Severance completion).
/// </summary>
[FlashContent("SEVERANCE")]
public sealed class Severance : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
        if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
            || x.NeedsChoice)
            return;
        if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Discard)
            || x.NeedsChoice)
            return;
        FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Hand);
    }
}

/// <summary>
/// <c>Reave</c>: attack, then the Cards variable's Souls (upgraded with the card) at random draw-pile
/// positions (<c>CardEffectSpecRegistry</c>'s Reave completion).
/// </summary>
[FlashContent("REAVE")]
public sealed class Reave : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0), ("SOUL", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        bool upgraded = x.Card.Upgrade > 0;
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>GraveWarden</c>: player block, then one Soul at a random draw-pile position
/// (<c>CardEffectSpecRegistry</c>'s GraveWarden completion).
/// </summary>
[FlashContent("GRAVE_WARDEN")]
public sealed class GraveWarden : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Undeath</c>: player block, then a clone of this card into the discard pile
/// (<c>CardEffectSpecRegistry</c>'s Undeath completion + <c>CardModel.CreateClone</c>).
/// </summary>
[FlashContent("UNDEATH")]
public sealed class Undeath : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("UNDEATH", 0), ("UNDEATH", 1)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralBlock(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCard clone = FlashKernel.CloneCard(x.Card);
        FlashKernel.AddGeneratedCard(ref x, clone, FlashPile.Discard);
    }
}

/// <summary>
/// <c>SculptingStrike</c>: attack, then add the local Ethereal keyword to a selected hand card
/// (<c>CardChoiceSupport</c>'s ApplyEthereal spec + <c>CardCmd.ApplyKeyword</c>).
/// </summary>
[FlashContent("SCULPTING_STRIKE")]
public sealed class SculptingStrike : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[FlashLimits.Cards];
        int count = 0;
        for (int index = 0; index < x.B.HandCount; index++)
        {
            if (!x.B.Cards[index].HasKeyword(FlashKeywords.Ethereal))
                options[count++] = index;
        }
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.ApplyEthereal, FlashPile.Hand, options[..count], 1,
                selected, out int chosen))
        {
            return;
        }
        if (chosen > 0)
            FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]).Keywords |= FlashKeywords.Ethereal;
    }
}

/// <summary>
/// <c>Transfigure</c>: add one this-combat cost to a selected hand card and increase its base replay
/// count (<c>CardChoiceResolution.ModifySelectedCard</c>).
/// </summary>
[FlashContent("TRANSFIGURE")]
public sealed class Transfigure : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
    }

    public override void OwnChoice(ref FlashExec x)
    {
        Span<int> options = stackalloc int[x.B.HandCount];
        for (int index = 0; index < options.Length; index++)
            options[index] = index;
        Span<int> selected = stackalloc int[1];
        if (!FlashKernel.ChooseCards(ref x, FlashChoiceEffect.Modify, FlashPile.Hand, options, 1, selected,
                out int chosen))
        {
            return;
        }
        if (chosen == 0)
            return;
        ref FlashCard card = ref FlashPiles.At(ref x.B, FlashPile.Hand, selected[0]);
        int cost = FlashKernel.EnergyCostWithLocalModifiers(x.B, card);
        if ((card.Flags & FlashCardFlags.CostsX) == 0 && cost >= 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, 1, FlashCostType.Relative, FlashCostExpiration.EndOfCombat,
                reduceOnly: false);
        }
        card.ReplayCount = checked((byte)(card.ReplayCount + 1));
    }
}

/// <summary>
/// <c>CaptureSpirit</c>: unblockable unpowered attack damage with the card as source, then Souls at
/// random draw-pile positions (<c>CardOnPlaySupport.Batch042</c>'s CaptureSpirit case).
/// </summary>
[FlashContent("CAPTURE_SPIRIT")]
public sealed class CaptureSpirit : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("SOUL", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            return;
        Span<byte> target = stackalloc byte[1];
        target[0] = x.Target;
        FlashKernel.Damage(ref x, target, NecrobinderContent.Var(ref x, 0),
            FlashValueProps.Unblockable | FlashValueProps.Unpowered | FlashValueProps.Move,
            FlashLimits.PlayerOwner, hasCardSource: true, x.Card);
        if (x.NeedsChoice)
            return;
        int count = (int)NecrobinderContent.Var(ref x, 1);
        for (int index = 0; index < count; index++)
        {
            FlashCard soul = NecrobinderContent.CreateSoul(x.B, x.R, upgraded: false);
            if (!FlashKernel.AddGeneratedCard(ref x, soul, FlashPile.Draw, FlashPilePosition.Random)
                || x.NeedsChoice)
                return;
        }
    }
}

/// <summary>
/// <c>Melancholy</c>: player block; after any death that was not prevented, each Melancholy copy in a
/// combat pile loses its Energy variable from this combat's cost (<c>AfterDeathMirrors</c>).
/// </summary>
[FlashContent("MELANCHOLY")]
public sealed class Melancholy : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Block", "Energy"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralBlock(ref x, 0);

    public override void AfterDeath(ref FlashExec x, FlashListener self, byte creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || (uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id)
            return;
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        if (energy > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -energy, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }
}

/// <summary>
/// <c>BansheesCry</c>: attack all enemies; its cost drops by Energy for every Ethereal card played and
/// for every Ethereal play already in this combat when it enters combat
/// (<c>AfterCardPlayedMirrors.HandleBansheesCry</c>, <c>SimulatedCombatState.CardEventHistory</c>).
/// </summary>
[FlashContent("BANSHEES_CRY")]
public sealed class BansheesCry : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Energy"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardPlayed(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if ((uint)self.Index >= (uint)x.B.CardCount)
            return;
        ref FlashCard card = ref x.B.Cards[self.Index];
        if (card.Id != Id || !FlashKernel.HasKeyword(x.B, play.Card, FlashKeywords.Ethereal))
            return;
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        if (energy > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -energy, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }

    public override void AfterEnteredCombat(ref FlashExec x, FlashPile pile, int index)
    {
        ref FlashCard card = ref FlashPiles.At(ref x.B, pile, index);
        if (card.Id != Id || card.IsClone)
            return;
        int plays = x.B.Counter(FlashCounter.EtherealPlays);
        int energy = (int)x.R.Card(card.Id, card.Upgrade).Vars[1];
        int reduction = plays * energy;
        if (reduction > 0)
        {
            FlashKernel.AddEnergyCostModifier(ref card, -reduction, FlashCostType.Relative,
                FlashCostExpiration.EndOfCombat, reduceOnly: false);
        }
    }
}

/// <summary>
/// <c>DrainPower</c>: single-target attack, then upgrade up to Cards random upgradable discard cards
/// (<c>CardSelectionCardMirrors.DrainPowerOnPlay</c>: filtered list, full Fisher-Yates with the
/// CombatCardSelection stream, take the first Cards).
/// </summary>
[FlashContent("DRAIN_POWER")]
public sealed class DrainPower : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int discardStart = FlashPiles.Start(x.B, FlashPile.Discard);
        int discardCount = x.B.DiscardCount;
        Span<int> order = stackalloc int[discardCount];
        int candidates = 0;
        for (int index = 0; index < discardCount; index++)
        {
            ref readonly FlashCard card = ref x.B.Cards[discardStart + index];
            FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
            if (card.Upgrade < stats.MaxUpgradeLevel)
                order[candidates++] = index;
        }
        if (candidates == 0)
            return;
        ref FlashRngState rng = ref x.B.RngStream(FlashRngStream.CombatCardSelection);
        for (int n = candidates; n > 1;)
        {
            n--;
            int k = rng.NextInt(n + 1);
            (order[k], order[n]) = (order[n], order[k]);
        }
        int take = Math.Min((int)NecrobinderContent.Var(ref x, 1), candidates);
        for (int index = 0; index < take; index++)
            FlashKernel.UpgradeCard(x.R, ref x.B.Cards[discardStart + order[index]]);
    }
}

/// <summary><c>Parse</c>: draw its Cards variable (the inferred owner-draw recipe).</summary>
[FlashContent("PARSE")]
public sealed class Parse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Cards"];

    public override void OnPlay(ref FlashExec x) => FlashKernel.Draw(ref x, (int)NecrobinderContent.Var(ref x, 0), fromHandDraw: false);
}

/// <summary>
/// <c>Invoke</c>: apply SummonNextTurn for the Summon variable, then EnergyNextTurn for Energy
/// (<c>CardPowerOnPlaySupport</c>'s Invoke case).
/// </summary>
[FlashContent("INVOKE")]
public sealed class Invoke : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Summon", "Energy"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SummonNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.EnergyNextTurnPower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
    }
}

/// <summary>
/// <c>Eidolon</c>: auto-play every Ethereal, playable card in the exhaust pile in pile order
/// (<c>CardOnPlaySupport.Batch043</c>'s ApplyEidolon). The snapshot follows the existing engine's
/// <c>ExhaustPile.Cards.Where(...).ToArray()</c>: positions are taken before the loop and shifted only by
/// the removals of earlier plays, so a card that exhausts back into the pile is not played again.
/// </summary>
[FlashContent("EIDOLON")]
public sealed class Eidolon : FlashCardDef
{
    public override void OnPlay(ref FlashExec x)
    {
        Span<int> positions = stackalloc int[x.B.ExhaustCount];
        int count = 0;
        for (int offset = 0; offset < x.B.ExhaustCount; offset++)
        {
            FlashCard card = FlashPiles.At(ref x.B, FlashPile.Exhaust, offset);
            if (card.HasKeyword(FlashKeywords.Ethereal) && !card.HasKeyword(FlashKeywords.Unplayable))
                positions[count++] = offset;
        }
        for (int index = 0; index < count; index++)
        {
            int position = positions[index];
            if (!FlashKernel.AutoPlay(ref x, FlashPile.Exhaust, position))
                return;
            if (x.NeedsChoice)
                return;
            for (int later = index + 1; later < count; later++)
            {
                if (positions[later] > position)
                    positions[later]--;
            }
        }
    }
}

[FlashContent("SLEIGHT_OF_FLESH")]
public sealed class SleightOfFlesh : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["SleightOfFleshPower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SleightOfFleshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("SPIRIT_OF_ASH")]
public sealed class SpiritOfAsh : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["BlockOnExhaust"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.SpiritOfAshPower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEVOUR_LIFE")]
public sealed class DevourLife : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["DevourLifePower"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DevourLifePower>(),
            (int)NecrobinderContent.Var(ref x, 0), FlashLimits.PlayerOwner);
}

[FlashContent("DEMESNE")]
public sealed class Demesne : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Energy", "Cards"];

    public override void OnPlay(ref FlashExec x)
        => FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, FlashRegistry.PowerId<Powers.DemesnePower>(),
            (int)NecrobinderContent.Var(ref x, 1), FlashLimits.PlayerOwner);
}

/// <summary>
/// <c>TheScythe</c>: attack with its accumulated Damage variable, then add the Increase variable to
/// the played card's Damage (and record the deck-version long-term growth:
/// <c>BespokeCardMirrors.TheScytheOnPlay</c>). The accumulated value is the imported Damage-variable
/// delta (Var0), so no private counter is needed.
/// </summary>
[FlashContent("THE_SCYTHE")]
public sealed class TheScythe : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        int increase = (int)NecrobinderContent.Var(ref x, 1);
        FlashCardEffects.AddCurrentCardVar(ref x, 0, increase);
        if ((x.Card.Flags & FlashCardFlags.DeckCard) != 0)
            x.B.AddCounter(FlashCounter.LongTermResourceValue, increase);
    }
}
