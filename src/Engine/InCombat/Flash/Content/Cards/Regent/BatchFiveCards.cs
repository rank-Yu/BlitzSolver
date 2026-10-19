namespace CombatSolver.Engine.InCombat.Flash.Content.Cards.Regent;

// Regent batch five: historical counters, draw-time growth, card generation, the late cost hook and
// the extra-turn-end Power cards. Each definition names the existing-engine path it mirrors.

/// <summary>
/// <c>Supermassive</c>: a calculated attack (<c>CalculatedVarSpecRegistry.Supermassive</c>):
/// CalculationBase + ExtraDamage × cards generated this combat. The generated-card count is a
/// combat-long counter imported from the root history.
/// </summary>
[FlashContent("SUPERMASSIVE")]
public sealed class Supermassive : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["CalculationBase", "ExtraDamage"];

    public override void OnPlay(ref FlashExec x)
    {
        int generated = x.B.Counter(FlashCounter.CardsGeneratedThisCombat);
        decimal damage = FlashCardEffects.Var(x, 0)
            + FlashCardEffects.Var(x, 1) * generated;
        FlashCardEffects.Attack(ref x, damage);
    }
}

/// <summary>
/// <c>BeatIntoShape</c>: the inferred attack, then forge
/// <c>CalculatedForge - Results.Count × CalculationExtra</c> (CardOnPlaySupport). The calculated
/// multiplier is the player's powered attack hits on the target this turn; the attack's own hits
/// cancel out, so the forge is CalculationBase + CalculationExtra × hits before this attack.
/// </summary>
[FlashContent("BEAT_INTO_SHAPE")]
public sealed class BeatIntoShape : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "CalculationBase", "CalculationExtra"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [(FlashKernel.SovereignBladeId, 0)];

    public override void OnPlay(ref FlashExec x)
    {
        if (x.Target == FlashLimits.NoOwner)
            throw new FlashUnsupportedException("BeatIntoShape 缺少目标，无法读取按目标命中数。");
        int hits = FlashKernel.PoweredAttackHitsByPlayer(x.B, x.Target);
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        decimal forge = FlashCardEffects.Var(x, 1) + FlashCardEffects.Var(x, 2) * hits;
        if (forge < int.MinValue || forge > int.MaxValue || forge != decimal.Truncate(forge))
            throw new FlashUnsupportedException($"BeatIntoShape 的锻造量 {forge} 无法用整数表示。");
        FlashKernel.Forge(ref x, (int)forge);
    }
}

/// <summary>
/// <c>KinglyKick</c>: the inferred attack; drawing it lowers its own cost by 1 for the rest of combat
/// (<c>AfterCardDrawnMirrors.HandleKinglyKick</c> → <c>EnergyCost.AddThisCombat(-1)</c>).
/// </summary>
[FlashContent("KINGLY_KICK")]
public sealed class KinglyKick : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!FlashKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        // The drawn card is this card, at its post-draw hand position (AddThisCombat(-1)).
        FlashKernel.AddEnergyCostModifier(ref x.B.Cards[drawnPosition], -1, FlashCostType.Relative,
            FlashCostExpiration.EndOfCombat, reduceOnly: false);
    }
}

/// <summary>
/// <c>KinglyPunch</c>: the inferred attack; drawing it adds the Increase variable to its Damage for
/// the rest of combat (<c>AfterCardDrawnMirrors.HandleKinglyPunch</c>). The accumulated increase is
/// kept in <see cref="FlashCard.Private"/> so <c>AfterDowngraded</c> can re-add it after Dampen.
/// </summary>
[FlashContent("KINGLY_PUNCH")]
public sealed class KinglyPunch : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Increase"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardDrawn(ref FlashExec x, FlashListener self, int drawnPosition, bool fromHandDraw)
    {
        if (!FlashKernel.TakeIntrinsicDraw(ref x, self, drawnPosition))
            return;
        ref FlashCard drawn = ref x.B.Cards[drawnPosition];
        int increase = (int)x.R.Card(drawn.Id, drawn.Upgrade).Vars[1];
        if (increase > short.MaxValue - drawn.Var0 || increase > short.MaxValue - drawn.Private)
            throw new FlashUnsupportedException("KinglyPunch 的伤害累加超出卡牌私有计数范围。");
        drawn.Var0 = (short)(drawn.Var0 + increase);
        drawn.Private = (short)(drawn.Private + increase);
    }

    public override void ProjectPrivate(ref FlashCard card) => card.Private = card.Var0;

    public override void AfterDowngraded(ref FlashBattle battle, int position)
        => battle.Cards[position].Var0 = battle.Cards[position].Private;
}

/// <summary>
/// <c>MakeItSo</c>: the inferred attack; <c>AfterCardPlayedLate</c> returns this card to hand when the
/// number of finished Skill plays this turn is a multiple of the Cards variable
/// (<c>AfterCardPlayedMirrors.HandleMakeItSo</c>; vanilla records CardPlayFinished before the late
/// pass, so the current Skill is included).
/// </summary>
[FlashContent("MAKE_IT_SO")]
public sealed class MakeItSo : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "Cards"];

    public override void OnPlay(ref FlashExec x) => FlashCardEffects.GeneralAttack(ref x, 0);

    public override void AfterCardPlayedLate(ref FlashExec x, FlashListener self, in FlashCardPlayInfo play)
    {
        if (self.Kind != FlashListenerKind.Card)
            return;
        if (x.R.Card(play.Card.Id, play.Card.Upgrade).Type != FlashCardType.Skill)
            return;
        int position = self.Index;
        if (position >= x.B.CardCount)
            return;
        int handEnd = x.B.HandCount;
        if (position < handEnd)
            return;
        int cards = (int)x.R.Card(x.B.Cards[position].Id, x.B.Cards[position].Upgrade).Vars[1];
        if (cards <= 0)
            throw new FlashUnsupportedException("MakeItSo 的 Cards 变量不为正。");
        int finished = x.B.Counter(FlashCounter.SkillCardsPlayedThisTurn) + 1;
        if (finished % cards != 0)
            return;
        FlashPile pile = FlashPiles.PileOf(x.B, position, out int index);
        FlashKernel.MoveCard(ref x.B, pile, index, FlashPile.Hand);
    }
}

/// <summary>
/// <c>CollisionCourse</c>: the inferred attack, then CardEffectSpecRegistry adds one Debris to hand.
/// </summary>
[FlashContent("COLLISION_COURSE")]
public sealed class CollisionCourse : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage"];

    public override IReadOnlyList<(string CardId, int Upgrade)> GeneratedCards => [("DEBRIS", 0)];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        if (!FlashRegistry.TryCard("DEBRIS", out ushort debris))
            throw new FlashUnsupportedException("CollisionCourse 需要 DEBRIS 的 Flash 定义。");
        FlashKernel.AddGeneratedCard(ref x, FlashKernel.CreateCard(x.R, debris), FlashPile.Hand);
    }
}

/// <summary>
/// <c>MeteorShower</c>: attack every enemy, then CorePowerSupport.ApplyCardPowers applies Weak and
/// Vulnerable to every hittable enemy in roster order (resolving power amount changes between
/// targets).
/// </summary>
[FlashContent("METEOR_SHOWER")]
public sealed class MeteorShower : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["Damage", "VulnerablePower", "WeakPower"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashCardEffects.GeneralAttack(ref x, 0);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.WeakPower>(), 2);
        if (x.NeedsChoice)
            return;
        FlashCardEffects.ApplyToAllEnemies(ref x, FlashRegistry.PowerId<Powers.VulnerablePower>(), 1);
    }
}

/// <summary>
/// <c>TheSealedThrone</c>: applies TheSealedThronePower 1; the power gains its Amount stars before
/// each card play by its owner (<c>BeforeCardPlayedMirrors.HandleTheSealedThronePower</c>).
/// </summary>
[FlashContent("THE_SEALED_THRONE")]
public sealed class TheSealedThrone : FlashCardDef
{
    public override IReadOnlyList<string> AppliedPowers => ["THE_SEALED_THRONE_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner,
            FlashRegistry.PowerId<Powers.TheSealedThronePower>(), 1, FlashLimits.PlayerOwner);
    }
}





/// <summary>
/// <c>VoidForm</c>: CardOnPlaySupport.Batch043 applies VoidFormPower with the "VoidFormPower" variable
/// after <c>PrepareVoidFormApplication</c> (an already-active power's per-turn counter is maxed so the
/// new layer does not show free cards during the forced turn end), then ends the player turn.
/// </summary>
[FlashContent("VOID_FORM")]
public sealed class VoidForm : FlashCardDef
{
    public override IReadOnlyList<string> VarNames => ["VoidFormPower"];

    public override IReadOnlyList<string> AppliedPowers => ["VOID_FORM_POWER"];

    public override void OnPlay(ref FlashExec x)
    {
        ushort power = FlashRegistry.PowerId<Powers.VoidFormPower>();
        int existing = FlashKernel.FindPower(x.B, FlashLimits.PlayerOwner, power);
        if (existing >= 0 && x.B.Powers[existing].Amount > 0)
            x.B.Powers[existing].Aux0 = 999_999_999;
        FlashKernel.ApplyPower(ref x, FlashLimits.PlayerOwner, power, (int)FlashCardEffects.Var(x, 0),
            FlashLimits.PlayerOwner);
        if (x.NeedsChoice)
            return;
        FlashKernel.RequestPlayerTurnEnd(ref x.B);
    }
}
