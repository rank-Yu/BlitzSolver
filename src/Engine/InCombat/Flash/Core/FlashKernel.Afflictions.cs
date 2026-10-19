namespace CombatSolver.Engine.InCombat.Flash;

/// <summary>
/// Card afflictions and enchantments. Afflictions are markers on a card; the powers that apply them
/// (Ringing, Tangled, Smoggy, Vital Spark, Hex) drive the behavior. The existing engine applies and
/// clears them in <c>SimulatedCombatState.NormalizeCardAfflictions</c>, which runs at card-entry and
/// action boundaries; this file keeps the same normalization points and the same card-entry check
/// (<c>PredictedCard.HasCheckedPowerAfflictionEntry</c>).
/// </summary>
public static partial class FlashKernel
{
    /// <summary>Internal affliction id of the card, or <see cref="ushort.MaxValue"/> when it has none.</summary>
    public static ushort AfflictionId(in FlashCard card)
        => card.AfflictionId == 0 ? ushort.MaxValue : (ushort)(card.AfflictionId - 1);

    /// <summary>Internal enchantment id of the card, or <see cref="ushort.MaxValue"/> when it has none.</summary>
    public static ushort EnchantmentId(in FlashCard card)
        => card.EnchantmentId == 0 ? ushort.MaxValue : (ushort)(card.EnchantmentId - 1);

    /// <summary>
    /// Mirrors <c>CombatPredictionSimulator.Afflict</c> / <c>CardCmd.Afflict</c>: the
    /// <c>ShouldAfflict</c>/<c>CanAfflict</c> checks, the first application, and the same-type stack.
    /// </summary>
    public static void ApplyAffliction(ref FlashExec x, int position, ushort afflictionId, int amount)
    {
        if (amount == 0 || IsOverOrEnding(x.B))
            return;
        if ((uint)position >= (uint)x.B.CardCount)
            throw new ArgumentOutOfRangeException(nameof(position), position, "卡牌位置越界。");
        ref FlashCard card = ref x.B.Cards[position];
        FlashAfflictionDef def = FlashRegistry.Affliction(afflictionId);
        FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
        if (!def.CanAfflictCardType(stats.Type))
            return;
        if (HasKeyword(x.B, card, FlashKeywords.Unplayable) && !def.CanAfflictUnplayableCards)
            return;
        if (card.AfflictionId != 0)
        {
            // AfflictionModel.CanAfflict: a different type needs stacking; the same type is accepted
            // only when the affliction stacks, and then its amount is added.
            if (!def.IsStackable || card.AfflictionId != afflictionId + 1)
                return;
            long stacked = (long)card.AfflictionAmount + amount;
            if (stacked is < short.MinValue or > short.MaxValue)
                throw new FlashUnsupportedException($"苦难 {def.NativeId} 层数 {stacked} 超出 Flash 的短整型表示。");
            card.AfflictionAmount = (short)stacked;
            return;
        }
        if (amount is < short.MinValue or > short.MaxValue)
            throw new FlashUnsupportedException($"苦难 {def.NativeId} 层数 {amount} 超出 Flash 的短整型表示。");
        card.AfflictionId = checked((byte)(afflictionId + 1));
        card.AfflictionAmount = (short)amount;
    }

    /// <summary>Mirrors <c>CombatPredictedCardExtensions.ClearAffliction</c>.</summary>
    public static void ClearAffliction(ref FlashBattle battle, int position)
    {
        ref FlashCard card = ref battle.Cards[position];
        card.AfflictionId = 0;
        card.AfflictionAmount = 0;
    }

    /// <summary>Clears every card carrying <paramref name="afflictionId"/>, keeping pile order.</summary>
    public static void ClearAfflictions(ref FlashBattle battle, ushort afflictionId)
    {
        for (int position = 0; position < battle.CardCount; position++)
        {
            if (battle.Cards[position].AfflictionId == afflictionId + 1)
                ClearAffliction(ref battle, position);
        }
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.NormalizeCardAfflictions</c>: the Hex/Tangled/Ringing power
    /// pass over every player card, then the Vital Spark/Smoggy one-time card-entry pass.
    /// </summary>
    public static void NormalizeCardAfflictions(ref FlashExec x)
    {
        ushort hex = PowerIdOrMax("HEX_POWER");
        ushort tangled = PowerIdOrMax("TANGLED_POWER");
        ushort ringing = PowerIdOrMax("RINGING_POWER");
        int hexAmount = hex == ushort.MaxValue ? 0 : GetAmount(x.B, FlashLimits.PlayerOwner, hex);
        int tangledAmount = tangled == ushort.MaxValue ? 0 : GetAmount(x.B, FlashLimits.PlayerOwner, tangled);
        int ringingAmount = ringing == ushort.MaxValue ? 0 : GetAmount(x.B, FlashLimits.PlayerOwner, ringing);
        ushort hexed = AfflictionIdOrMax("HEXED");
        ushort entangled = AfflictionIdOrMax("ENTANGLED");
        ushort ringingAffliction = AfflictionIdOrMax("RINGING");

        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.AfflictionId == 0)
            {
                if (hexAmount > 0 && hexed != ushort.MaxValue)
                    ApplyAffliction(ref x, position, hexed, hexAmount);
                else if (tangledAmount > 0 && entangled != ushort.MaxValue
                         && x.R.Card(card.Id, card.Upgrade).Type == FlashCardType.Attack)
                    ApplyAffliction(ref x, position, entangled, 1);
                else if (ringingAmount > 0 && ringingAffliction != ushort.MaxValue)
                    ApplyAffliction(ref x, position, ringingAffliction, 1);
                continue;
            }
            bool staleHex = hexAmount <= 0 && hexed != ushort.MaxValue && card.AfflictionId == hexed + 1;
            bool staleTangled = tangledAmount <= 0 && entangled != ushort.MaxValue && card.AfflictionId == entangled + 1;
            bool staleRinging = ringingAmount <= 0 && ringingAffliction != ushort.MaxValue
                && card.AfflictionId == ringingAffliction + 1;
            if (staleHex || staleTangled || staleRinging)
                ClearAffliction(ref x.B, position);
        }
        NormalizePowerAfflictions(ref x);
        NormalizeSwordSageReplays(ref x);
    }

    /// <summary>
    /// <c>SimulatedCombatState.PowerLifecycle.cs:NormalizeSwordSageReplays</c>: keep every Sovereign
    /// Blade's replay count at "canonical + current SwordSage amount". The per-card applied bonus lives
    /// in <see cref="FlashCard.Private"/> and travels with the card; a freshly generated non-clone starts
    /// from the frozen root amount, a clone from the current amount, exactly like the existing engine's
    /// <c>rootAmount</c>/<c>desired</c> baseline.
    /// </summary>
    private static void NormalizeSwordSageReplays(ref FlashExec x)
    {
        if (!x.R.HasIdentityContent)
            return;
        if (!FlashRegistry.TryPower("SWORD_SAGE_POWER", out ushort swordSage))
            return;
        if (!FlashRegistry.TryCard(FlashKernel.SovereignBladeId, out ushort blade))
            return;
        int desired = GetAmount(x.B, FlashLimits.PlayerOwner, swordSage);
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id != blade)
                continue;
            int applied = card.Private;
            if (applied == FlashLimits.UninitializedLedger)
            {
                applied = card.IsClone ? desired : x.R.RootSwordSageAmount;
                card.Private = checked((short)applied);
            }
            int delta = desired - applied;
            if (delta == 0)
                continue;
            int replay = card.ReplayCount + delta;
            if ((uint)replay > byte.MaxValue)
                throw new FlashCapacityException("Card.ReplayCount", byte.MaxValue);
            card.ReplayCount = (byte)replay;
            card.Private = checked((short)desired);
        }
    }

    /// <summary>
    /// <c>SimulatedCombatState.PowerLifecycle.cs:NormalizePowerAfflictions</c>: Vital Spark keeps the
    /// Tainted layers in sync only after its amount changed, clears Tainted when no Vital Spark is
    /// left, and afflicts a card created after the root once (Galvanized/Smog apply only to that first
    /// check). Tainted keeps the root's doubled amount until the power amount changes.
    /// </summary>
    private static void NormalizePowerAfflictions(ref FlashExec x)
    {
        ushort vitalSpark = PowerIdOrMax("VITAL_SPARK_POWER");
        ushort galvanic = PowerIdOrMax("GALVANIC_POWER");
        ushort smoggy = PowerIdOrMax("SMOGGY_POWER");
        ushort tainted = AfflictionIdOrMax("TAINTED");
        ushort galvanized = AfflictionIdOrMax("GALVANIZED");
        ushort smog = AfflictionIdOrMax("SMOG");
        int vitalSparkAmount = 0;
        for (int index = 0; index < x.B.PowerCount; index++)
        {
            ref readonly FlashPower power = ref x.B.Powers[index];
            if (power.Id == vitalSpark && power.Amount > 0)
                vitalSparkAmount = checked(vitalSparkAmount + power.Amount);
        }
        bool hasVitalSpark = vitalSparkAmount > 0;
        int previous = x.B.Counter(FlashCounter.VitalSparkNormalizedAmount);
        bool amountChanged = previous != int.MinValue && previous != vitalSparkAmount;
        x.B.SetCounter(FlashCounter.VitalSparkNormalizedAmount, vitalSparkAmount);

        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            bool enteredCombat = (card.Flags & FlashCardFlags.PowerAfflictionChecked) == 0
                && (card.Keywords & FlashKeywords.CreatedAfterRoot) != 0;
            card.Flags |= FlashCardFlags.PowerAfflictionChecked;
            if (tainted != ushort.MaxValue && card.AfflictionId == tainted + 1)
            {
                // VitalSparkPower.AfterRemoved clears every Tainted layer; AfterPowerAmountChanged
                // rewrites them to the current total (the existing engine normalizes the same way).
                if (!hasVitalSpark)
                    ClearAffliction(ref x.B, position);
                else if (amountChanged && card.AfflictionAmount != vitalSparkAmount)
                    SetAfflictionAmount(ref card, vitalSparkAmount);
                continue;
            }
            if (card.AfflictionId != 0 || !enteredCombat)
                continue;
            for (int index = 0; index < x.B.PowerCount; index++)
            {
                ref readonly FlashPower power = ref x.B.Powers[index];
                if (power.Amount <= 0)
                    continue;
                FlashCardStats stats = x.R.Card(card.Id, card.Upgrade);
                if (galvanized != ushort.MaxValue && power.Id == galvanic && stats.Type == FlashCardType.Power)
                {
                    ApplyAffliction(ref x, position, galvanized, power.Amount);
                    break;
                }
                if (tainted != ushort.MaxValue && power.Id == vitalSpark && stats.Type == FlashCardType.Skill)
                {
                    ApplyAffliction(ref x, position, tainted, vitalSparkAmount);
                    break;
                }
                if (smog != ushort.MaxValue && power.Id == smoggy && power.Owner == FlashLimits.PlayerOwner
                    && stats.Type == FlashCardType.Skill
                    && x.B.Counter(FlashCounter.SkillPlayedThisSideTurn) > 0)
                {
                    ApplyAffliction(ref x, position, smog, 1);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Mirrors <c>SimulatedCombatState.NormalizeAeonglassWithers</c>: every Wither is upgraded to the
    /// summed <c>WitherUpgradeCount</c> of the roster Aeonglass enemies. The fake upgrade adds 3 to the
    /// Damage variable per level, and Wither's only dynamic-variable delta is that upgrade (base
    /// 3, max upgrade level 0), so the Flash value is the delta itself.
    /// </summary>
    public static void NormalizeAeonglassWithers(ref FlashExec x)
    {
        if (!FlashRegistry.TryMonster("AEONGLASS", out ushort aeon))
            return;
        int expectedLevel = 0;
        for (int position = 0; position < x.B.RosterCount; position++)
        {
            byte owner = EnemyOwner(x.B.Roster[position]);
            if (x.B.Enemy(owner).MonsterId == aeon)
                expectedLevel += x.B.Enemy(owner).Ai[1];
        }
        if (expectedLevel <= 0 || !FlashRegistry.TryCard("WITHER", out ushort wither))
            return;
        int expectedDamage = expectedLevel * 3;
        if (expectedDamage > short.MaxValue)
            throw new FlashUnsupportedException("Wither 的伪升级层数超出短整型表示。");
        for (int position = 0; position < x.B.CardCount; position++)
        {
            ref FlashCard card = ref x.B.Cards[position];
            if (card.Id == wither && card.Var0 < expectedDamage)
            {
                card.Var0 = (short)expectedDamage;
            }
        }
    }

    private static void SetAfflictionAmount(ref FlashCard card, int amount)
    {
        if (amount is < short.MinValue or > short.MaxValue)
            throw new FlashUnsupportedException($"污染层数 {amount} 超出 Flash 的短整型表示。");
        card.AfflictionAmount = (short)amount;
    }

    private static ushort AfflictionIdOrMax(string nativeId)
        => FlashRegistry.TryAffliction(nativeId, out ushort id) ? id : ushort.MaxValue;
}
