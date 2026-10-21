using CombatSolver.Flash.Kernel;
using CombatSolver.Flash.Search.Scoring;
using CombatSolver.Flash.Bridge;

namespace CombatSolver.Flash.Search;

/// <summary>
/// The existing solver's per-card power-commitment evidence over a Flash battle: trigger evidence,
/// opening-projection floors/potentials, and the Silent specialized progress/realized evidence. It
/// reads only the byte-exact Flash state (K1/K2/K4), the K4 feature values and the compiled card
/// score rules; no old-engine model or live value is touched.
/// </summary>
/// <summary>
/// The route-node commitment metadata: the ported <c>PowerCommitment</c> without its string list.
/// Registered cards are indexed in the bridge catalog, so the played set is an exact 128-bit mask.
/// </summary>
internal readonly record struct FlashPowerCommitmentState(
    PowerCommitmentFamily Family,
    PowerRoutePriority Priority,
    ulong CardsLow,
    ulong CardsHigh,
    int RoundTransitions,
    int LastEvidenceTurn,
    int Investment,
    int ProvisionalPotential,
    int ProgressEvidence,
    int RealizedEvidence,
    int OpenedActionCount)
{
    public int NetUnrealizedValue => (int)Math.Clamp(
        (long)ProvisionalPotential - Investment, int.MinValue, int.MaxValue);
}

internal static class FlashPowerEvidence
{
    internal readonly ref struct View
    {
        private readonly ref readonly FlashBattle _battle;
        private readonly FlashScoreFeatures _features;
        private readonly FlashRules _rules;
        private readonly FlashScorer _scorer;

        public View(in FlashBattle battle, in FlashScoreFeatures features, FlashRules rules,
            FlashScorer scorer)
        {
            _battle = ref battle;
            _features = features;
            _rules = rules;
            _scorer = scorer;
        }

        public int LiveCount => _battle.HandCount + _battle.DrawCount + _battle.DiscardCount;
        public int HandCount => _battle.HandCount;
        public FlashScoreFeatures Features => _features;
        public FlashRules Rules => _rules;
        public ref readonly FlashBattle Battle => ref _battle;

        public FlashCard LiveCard(int index)
        {
            if ((uint)index >= (uint)LiveCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _battle.Cards[index];
        }

        public FlashCard HandCard(int index) => _battle.Cards[index];
        public FlashCardStats Stats(in FlashCard card) => _rules.Card(card.Id, card.Upgrade);
        public FlashCardScore Score(in FlashCard card)
            => _scorer.CardScoreForEvidence(card.Id, card.Upgrade);
        public FlashCardType Type(in FlashCard card) => FlashKernel.CardType(_battle, _rules, card);
        public bool HasKeyword(in FlashCard card, FlashKeywords keyword)
            => FlashKernel.HasKeyword(_battle, card, keyword);
        public string NativeId(in FlashCard card) => FlashRegistry.Card(card.Id).NativeId;
        public int CardValue(in FlashCard card) => (int)Math.Round(_scorer.CardValueForEvidence(_battle, card));
        public int AttackHits(in FlashCard card) => _scorer.AttackHitsForEvidence(_battle, card);
        public int EnemyPowerAmount(int enemy, string nativeId)
        {
            if (!FlashRegistry.TryPower(nativeId, out ushort powerId))
                return 0;
            byte owner = (byte)(FlashLimits.FirstEnemyOwner + enemy);
            return Math.Max(0, FlashKernel.GetAmount(_battle, owner, powerId));
        }
        public int Var(in FlashCard card, int index) => _scorer.VarForEvidence(_battle, card, index);
        public int Block(in FlashCard card)
        {
            FlashCardScore score = Score(card);
            return score.BlockVar >= 0 ? Math.Max(0, Var(card, score.BlockVar)) : 0;
        }

        public int Damage(in FlashCard card)
        {
            FlashCardScore score = Score(card);
            return score.DamageVar >= 0 ? Math.Max(0, Var(card, score.DamageVar)) : 0;
        }

        public int Repeat(in FlashCard card)
        {
            FlashCardScore score = Score(card);
            return score.RepeatVar >= 0 ? Math.Max(1, Var(card, score.RepeatVar)) : 1;
        }

        public bool HasVarFragment(in FlashCard card, string fragment)
        {
            IReadOnlyList<string> names = FlashRegistry.Card(card.Id).VarNames;
            for (int index = 0; index < names.Count; index++)
            {
                if (names[index].Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public int CountType(FlashCardType type)
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (Type(card) == type)
                    count++;
            }
            return count;
        }

        public int CountVar(string fragment)
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                if (HasVarFragment(LiveCard(index), fragment))
                    count++;
            }
            return count;
        }

        public bool AnyVar(string fragment)
        {
            for (int index = 0; index < LiveCount; index++)
            {
                if (HasVarFragment(LiveCard(index), fragment))
                    return true;
            }
            return false;
        }

        public bool AnyNativeIdContaining(string fragment)
        {
            for (int index = 0; index < LiveCount; index++)
            {
                if (NativeId(LiveCard(index)).Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public int CountNativeIdContaining(string fragment)
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                if (NativeId(LiveCard(index)).Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        public int MaxBlock()
        {
            int max = 0;
            for (int index = 0; index < LiveCount; index++)
                max = Math.Max(max, Block(LiveCard(index)));
            return max;
        }

        public int MaxDamage()
        {
            int max = 0;
            for (int index = 0; index < LiveCount; index++)
                max = Math.Max(max, Damage(LiveCard(index)));
            return max;
        }

        public int TotalAttackDamage()
        {
            long total = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (Type(card) != FlashCardType.Attack)
                    continue;
                total += (long)Damage(card) * Repeat(card);
            }
            return (int)Math.Min(int.MaxValue, total);
        }

        public int CountShiv()
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if ((Stats(card).Tags & FlashCardTag.Shiv) != 0
                    || _scorer.ImmediateShivSupplyForEvidence(_battle, card) > 0
                    || NativeId(card) is "INFINITE_BLADES" or "FAN_OF_KNIVES")
                {
                    count++;
                }
            }
            return count;
        }

        public bool EnemyHasPower(string nativeId)
        {
            if (!FlashRegistry.TryPower(nativeId, out ushort powerId))
                return false;
            for (int enemy = 0; enemy < _battle.EnemyCount; enemy++)
            {
                byte owner = (byte)(FlashLimits.FirstEnemyOwner + enemy);
                if (_battle.Enemies[enemy].Creature.IsAlive
                    && FlashKernel.GetAmount(_battle, owner, powerId) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        public int PlayerPowerAmount(string nativeId)
        {
            return FlashRegistry.TryPower(nativeId, out ushort powerId)
                ? Math.Max(0, FlashKernel.GetAmount(_battle, FlashLimits.PlayerOwner, powerId))
                : 0;
        }

        public int PowerAmountGain(in FlashBattle parent, ushort cardId)
        {
            IReadOnlyList<string> applied = FlashRegistry.Card(cardId).AppliedPowers;
            int gain = 0;
            for (int index = 0; index < applied.Count; index++)
            {
                if (!FlashRegistry.TryPower(applied[index], out ushort powerId))
                    continue;
                int before = Math.Max(0, FlashKernel.GetAmount(parent, FlashLimits.PlayerOwner, powerId));
                int after = Math.Max(0, FlashKernel.GetAmount(_battle, FlashLimits.PlayerOwner, powerId));
                gain += Math.Max(0, after - before);
            }
            return gain;
        }

        public int RemainingTurns()
        {
            int drawPerTurn = Math.Max(1, _scorer.ModifiedHandDrawForEvidence(_battle));
            int damagePerTurn = Math.Max(1,
                _features.RetainedAttackValue * Math.Max(1, drawPerTurn)
                / Math.Max(1, _features.LiveDeckSize));
            return Math.Clamp(
                (_features.EnemyHp + damagePerTurn - 1) / damagePerTurn,
                1,
                SolverWeights.SetupValueHorizonTurns);
        }

        public int EnergyUnit()
        {
            if (LiveCount == 0)
                return 3;
            long total = 0;
            for (int index = 0; index < LiveCount; index++)
                total += Math.Max(0, CardValue(LiveCard(index)));
            return Math.Max(3, (int)(total / LiveCount));
        }

        public int DrawCount(in FlashCard card)
        {
            FlashCardScore score = Score(card);
            int cards = score.CardsVar >= 0 ? Math.Max(0, Var(card, score.CardsVar)) : 0;
            string id = NativeId(card);
            int draws = id switch
            {
                "DAGGER_THROW" or "ESCAPE_PLAN" => 1,
                "PREPARED" or "ADRENALINE" or "ACROBATICS" or "BACKFLIP"
                    or "EXPERTISE" or "REFLEX" => cards,
                "CALCULATED_GAMBLE" => Math.Max(0, HandCount - 1),
                _ => 0,
            };
            return Math.Max(0, draws);
        }

        public int ReachableCardPlays()
        {
            int remainingTurns = RemainingTurns();
            if (remainingTurns <= 0 || LiveCount == 0)
                return 0;
            int current = AffordableCount(currentTurn: true, energy: _battle.Energy,
                limit: HandCount);
            int futureTurns = Math.Max(0, remainingTurns - 1);
            if (futureTurns == 0)
                return current;
            int drawPerTurn = Math.Max(1, _scorer.ModifiedHandDrawForEvidence(_battle));
            int futureEnergy = Math.Max(0, _scorer.ModifiedMaxEnergyForEvidence(_battle));
            int drawIndex = 0;
            int future = 0;
            for (int turn = 0; turn < futureTurns && drawIndex < _battle.DrawCount; turn++)
            {
                int window = Math.Min(drawPerTurn, _battle.DrawCount - drawIndex);
                future = SaturatingAdd(future, AffordableCountInDraw(drawIndex, window, futureEnergy));
                drawIndex += window;
            }
            return SaturatingAdd(current, future);
        }

        private int AffordableCount(bool currentTurn, int energy, int limit)
        {
            Span<int> costs = stackalloc int[Math.Min(limit, 16)];
            int count = 0;
            for (int index = 0; index < limit && count < costs.Length; index++)
            {
                FlashCard card = HandCard(index);
                if (HasKeyword(card, FlashKeywords.Unplayable))
                    continue;
                int cost = currentTurn
                    ? FlashKernel.EnergyCostWithModifiers(_battle, _rules, card, FlashPile.Hand)
                    : FlashKernel.EnergyCostWithLocalModifiers(_battle, card);
                costs[count++] = Math.Max(0, cost);
            }
            return AffordableFromCosts(costs[..count], energy);
        }

        private int AffordableCountInDraw(int start, int window, int energy)
        {
            Span<int> costs = stackalloc int[Math.Min(window, 16)];
            int count = 0;
            for (int offset = 0; offset < window && count < costs.Length; offset++)
            {
                FlashCard card = _battle.Cards[_battle.HandCount + start + offset];
                if (HasKeyword(card, FlashKeywords.Unplayable))
                    continue;
                costs[count++] = Math.Max(0,
                    FlashKernel.EnergyCostWithLocalModifiers(_battle, card));
            }
            return AffordableFromCosts(costs[..count], energy);
        }

        private static int AffordableFromCosts(Span<int> costs, int energy)
        {
            costs.Sort();
            int count = 0;
            for (int index = 0; index < costs.Length; index++)
            {
                if (costs[index] > energy)
                    break;
                energy -= costs[index];
                count++;
            }
            return count;
        }

        public int CountKeyword(FlashKeywords keyword)
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                if (HasKeyword(LiveCard(index), keyword))
                    count++;
            }
            return count;
        }

        public int CountTag(FlashCardTag tag)
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                if ((Stats(LiveCard(index)).Tags & tag) != 0)
                    count++;
            }
            return count;
        }

        public int TotalDrawCount()
        {
            int total = 0;
            for (int index = 0; index < LiveCount; index++)
                total = SaturatingAdd(total, DrawCount(LiveCard(index)));
            return total;
        }

        public int IncomingDamage()
            => Math.Max(0, _features.PlayerHp - _features.ProjectedPlayerHp);

        public int DistinctOrbTypes()
        {
            ulong mask = 0;
            for (int index = 0; index < _battle.OrbCount; index++)
            {
                int id = _battle.Orbs[index].Id;
                if (id < 64)
                    mask |= 1UL << id;
            }
            return System.Numerics.BitOperations.PopCount(mask);
        }

        public int CountOrb(string fragment)
        {
            int count = 0;
            for (int index = 0; index < _battle.OrbCount; index++)
            {
                if (FlashRegistry.Orb(_battle.Orbs[index].Id).NativeId
                    .Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        public int CountZeroCostAttack()
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (Type(card) == FlashCardType.Attack
                    && FlashKernel.EnergyCostWithModifiers(_battle, _rules, card, FlashPile.Hand) == 0)
                {
                    count++;
                }
            }
            return count;
        }

        public bool StatusOrCurseInHand()
        {
            for (int index = 0; index < HandCount; index++)
            {
                FlashCardType type = Type(HandCard(index));
                if (type is FlashCardType.Status or FlashCardType.Curse)
                    return true;
            }
            return false;
        }

        public bool HasEnergyCostAtLeast(int minimum)
        {
            for (int index = 0; index < LiveCount; index++)
            {
                if (FlashKernel.EnergyCostWithModifiers(_battle, _rules, LiveCard(index), FlashPile.Hand)
                    >= minimum)
                {
                    return true;
                }
            }
            return false;
        }

        public int CountExhaustSources()
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (HasKeyword(card, FlashKeywords.Exhaust) || HasVarFragment(card, "Exhaust"))
                    count++;
            }
            return count;
        }

        public bool HasCardGeneration() => CountCardGeneration() > 0;

        public int CountCardGeneration()
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (Type(card) != FlashCardType.Attack
                    && (HasVarFragment(card, "Cards") || HasVarFragment(card, "Generated")))
                {
                    count++;
                }
            }
            return count;
        }

        public bool HasStarCost()
        {
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCardStats stats = Stats(LiveCard(index));
                if (stats.StarCost > 0 || stats.StarCostsX)
                    return true;
            }
            return false;
        }

        public bool HasVar(string fragment) => AnyVar(fragment);

        public bool HasForge()
        {
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (NativeId(card) is "FURNACE" or "SEEKING_EDGE" || HasVarFragment(card, "Forge"))
                    return true;
            }
            return false;
        }

        public bool HasSovereignBladeSource()
        {
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (NativeId(card) is "FURNACE" or "SEEKING_EDGE" or "SWORD_SAGE" or "PARRY"
                    || HasVarFragment(card, "Sovereign"))
                {
                    return true;
                }
            }
            return false;
        }

        public int CountSouls()
        {
            int count = 0;
            for (int index = 0; index < LiveCount; index++)
            {
                FlashCard card = LiveCard(index);
                if (NativeId(card).Contains("SOUL", StringComparison.OrdinalIgnoreCase)
                    || HasVarFragment(card, "Soul"))
                {
                    count++;
                }
            }
            return count;
        }

        public bool HasDebuffSource()
        {
            for (int index = 0; index < LiveCount; index++)
            {
                if (Score(LiveCard(index)).HasDebuffDynamicVar)
                    return true;
            }
            return false;
        }

        public bool OstyPresent => _features.OstyHp > 0;

        public int StarSpendCapacity()
        {
            int stars = Math.Max(0, _battle.Stars);
            if (stars == 0)
                return 0;
            long spend = 0;
            for (int index = 0; index < HandCount; index++)
            {
                FlashCardStats stats = Stats(HandCard(index));
                int cost = stats.StarCostsX ? stars : stats.StarCost;
                if (cost > 0)
                    spend += Math.Min(cost, stars);
            }
            return (int)Math.Min(stars, spend);
        }

        public int ModifiedMaxEnergy() => _scorer.ModifiedMaxEnergyForEvidence(_battle);
        public int ModifiedHandDraw() => Math.Max(1, _scorer.ModifiedHandDrawForEvidence(_battle));

        public bool WraithTiming(in FlashBattle parent, ushort cardId)
        {
            int intangible = PowerAmountGain(parent, cardId);
            return intangible > 0
                && ( _features.ProjectedPlayerHp <= 0
                    || RemainingTurns() <= intangible + 1);
        }
    }

    /// <summary><c>*PowerHasTriggerEvidence</c> over the child state.</summary>
    public static bool HasTriggerEvidence(ushort cardId, in FlashBattle parent, scoped in View view)
    {
        string id = FlashRegistry.Card(cardId).NativeId;
        int remainingTurns = view.RemainingTurns();
        bool hasAttack = view.CountType(FlashCardType.Attack) > 0;
        bool hasSkill = view.CountType(FlashCardType.Skill) > 0;
        bool hasBlockSkill = view.MaxBlock() > 0;
        int attackCount = view.CountType(FlashCardType.Attack);
        bool aliveEnemy = view.Features.AliveEnemyCount > 0;
        return id switch
        {
            // Ironclad.
            "AGGRESSION" => hasAttack && remainingTurns > 1,
            "BARRICADE" => hasBlockSkill,
            "CORRUPTION" => hasSkill,
            "CRIMSON_MANTLE" => remainingTurns > 1,
            "CRUELTY" => (view.CountVar("Vulnerable") > 0 || view.EnemyHasPower("VULNERABLE")) && hasAttack,
            "DARK_EMBRACE" => view.CountVar("Exhaust") + view.CountKeyword(FlashKeywords.Exhaust) > 0
                && remainingTurns > 1,
            "DEMON_FORM" => hasAttack && remainingTurns > 1,
            "FEEL_NO_PAIN" => view.CountVar("Exhaust") + view.CountKeyword(FlashKeywords.Exhaust) > 0,
            "HELLRAISER" => view.CountTag(FlashCardTag.Strike) > 0,
            "INFERNO" => remainingTurns > 1 && aliveEnemy,
            "INFLAME" => hasAttack,
            "JUGGERNAUT" => hasBlockSkill && aliveEnemy,
            "JUGGLING" => attackCount >= 3 || hasAttack && remainingTurns > 1,
            "PYRE" => remainingTurns > 1,
            "RUPTURE" => view.CountVar("SelfDamage") + view.CountVar("HpLoss") + view.CountVar("LoseHp") > 0,
            "STAMPEDE" => hasAttack && remainingTurns > 1,
            "STONE_ARMOR" => remainingTurns > 1,
            "UNMOVABLE" => hasBlockSkill,
            "VICIOUS" => (view.CountVar("Vulnerable") > 0 || view.EnemyHasPower("VULNERABLE"))
                && remainingTurns > 1,

            // Silent.
            "ABRASIVE" => hasBlockSkill || view.Features.ProjectedPlayerHp < view.Features.PlayerHp,
            "ACCELERANT" => view.CountVar("Poison") > 0 || view.EnemyHasPower("POISON"),
            "ACCURACY" => view.CountShiv() > 0,
            "AFTERIMAGE" => view.ReachableCardPlays() >= 5,
            "ENVENOM" => hasAttack && remainingTurns > 1,
            "FAN_OF_KNIVES" => aliveEnemy,
            "FOOTWORK" => hasBlockSkill,
            "INFINITE_BLADES" => remainingTurns > 1,
            "MASTER_PLANNER" => true,
            "NOXIOUS_FUMES" => remainingTurns > 1 && aliveEnemy,
            "PHANTOM_BLADES" => view.CountShiv() > 0,
            "SERPENT_FORM" => view.ReachableCardPlays() > 0,
            "SPEEDSTER" => view.TotalDrawCount() > 0,
            "TOOLS_OF_THE_TRADE" => remainingTurns > 1,
            "TRACKING" => (view.CountVar("Weak") > 0 || view.EnemyHasPower("WEAK")) && hasAttack,
            "WELL_LAID_PLANS" => remainingTurns > 1 && view.LiveCount > 0,
            "WRAITH_FORM" => view.WraithTiming(parent, cardId),

            // Defect.
            "BIASED_COGNITION" => view.Battle.OrbCount > 0 || view.CountVar("Orb") > 0,
            "BUFFER" => view.IncomingDamage() > 0,
            "BULK_UP" => hasAttack && hasBlockSkill,
            "CAPACITOR" => view.Battle.OrbCount > 0 || view.CountVar("Orb") > 0,
            "CONSUMING_SHADOW" => view.Battle.OrbCount > 0,
            "COOLANT" => view.DistinctOrbTypes() > 0,
            "CREATIVE_AI" => remainingTurns > 1,
            "DEFRAGMENT" => view.Battle.OrbCount > 0 || view.CountVar("Orb") > 0,
            "ECHO_FORM" => remainingTurns > 1 && (hasAttack || hasBlockSkill),
            "FERAL" => view.CountZeroCostAttack() > 0,
            "HAILSTORM" => view.CountOrb("FROST") > 0,
            "ITERATION" => view.StatusOrCurseInHand() || view.CountVar("Status") > 0,
            "LOOP" => view.Battle.OrbCount > 0,
            "MACHINE_LEARNING" => remainingTurns > 1,
            "SMOKESTACK" => view.CountVar("Status") > 0,
            "SPINNER" => remainingTurns > 1,
            "STORM" => view.CountType(FlashCardType.Power) > 0,
            "SUBROUTINE" => view.CountType(FlashCardType.Power) > 0,
            "THUNDER" => view.CountOrb("LIGHTNING") > 0,
            "TRASH_TO_TREASURE" => view.CountVar("Status") > 0,

            // Regent.
            "ARSENAL" => view.HasCardGeneration() && remainingTurns > 1,
            "BLACK_HOLE" => (view.HasStarCost() || view.HasVar("Star")) && aliveEnemy,
            "CHILD_OF_THE_STARS" => view.HasStarCost(),
            "FURNACE" => view.HasForge() || remainingTurns > 1,
            "GENESIS" => remainingTurns > 1,
            "MONARCHS_GAZE" => hasAttack && aliveEnemy,
            "NEUTRON_AEGIS" => remainingTurns > 1,
            "ORBIT" => remainingTurns > 1,
            "PALE_BLUE_DOT" => remainingTurns > 1 && view.HandCount > 0,
            "PARRY" => view.HasSovereignBladeSource(),
            "PILLAR_OF_CREATION" => view.HasCardGeneration(),
            "SEEKING_EDGE" => view.HasForge() || remainingTurns > 1,
            "SPECTRUM_SHIFT" => remainingTurns > 1,
            "SWORD_SAGE" => view.HasSovereignBladeSource(),
            "THE_SEALED_THRONE" => remainingTurns > 1 && view.LiveCount > 0,
            "TYRANNY" => remainingTurns > 1 && view.HandCount > 0,
            "VOID_FORM" => remainingTurns > 1,

            // Necrobinder.
            "CALCIFY" => view.OstyPresent,
            "CALL_OF_THE_VOID" => remainingTurns > 1,
            "COUNTDOWN" => aliveEnemy && remainingTurns > 1,
            "DANSE_MACABRE" => remainingTurns > 1 && view.HasEnergyCostAtLeast(2),
            "DEMESNE" => remainingTurns > 1,
            "DEVOUR_LIFE" => view.CountSouls() > 0 || view.OstyPresent,
            "FRIENDSHIP" => remainingTurns > 1 && hasAttack,
            "HAUNT" => view.CountSouls() > 0,
            "LETHALITY" => hasAttack,
            "NECRO_MASTERY" => view.OstyPresent,
            "NEUROSURGE" => hasAttack || remainingTurns > 1,
            "PAGESTORM" => view.CountKeyword(FlashKeywords.Ethereal) > 0,
            "REAPER_FORM" => hasAttack,
            "SENTRY_MODE" => view.OstyPresent || remainingTurns > 1,
            "SHROUD" => view.CountVar("Doom") > 0,
            "SLEIGHT_OF_FLESH" => hasAttack || view.HasDebuffSource(),
            "SPIRIT_OF_ASH" => view.CountKeyword(FlashKeywords.Ethereal) > 0,

            // Colorless.
            "AUTOMATION" => remainingTurns > 1,
            "CALAMITY" => hasAttack,
            "ENTROPY" => remainingTurns > 1 && view.HandCount > 0,
            "ETERNAL_ARMOR" => remainingTurns > 1,
            "FASTEN" => view.CountTag(FlashCardTag.Defend) > 0,
            "MAYHEM" => remainingTurns > 1 && view.Battle.DrawCount > 0,
            "NOSTALGIA" => hasAttack || hasSkill,
            "PANACHE" => view.HandCount > 0,
            "PREP_TIME" => hasAttack,
            "PROWESS" => hasAttack || hasBlockSkill,
            "ROLLING_BOULDER" => aliveEnemy && remainingTurns > 1,
            "STRATAGEM" => remainingTurns > 1,
            _ => false,
        };
    }

    /// <summary><c>*PowerTriggerProjectionFloor</c> over the child state.</summary>
    public static int TriggerProjectionFloor(ushort cardId, scoped in View view)
    {
        string id = FlashRegistry.Card(cardId).NativeId;
        int turns = Math.Max(1, view.RemainingTurns() - 1);
        int attacks = view.CountType(FlashCardType.Attack);
        return id switch
        {
            "DEMON_FORM" => SaturatingMul(4, Math.Max(1, attacks)),
            "INFLAME" => SaturatingMul(3, Math.Max(1, attacks)),
            "FEEL_NO_PAIN" => SaturatingMul(4, Math.Max(1, view.CountExhaustSources())),
            "JUGGERNAUT" => SaturatingMul(8, Math.Max(1, view.CountVar("Block"))),
            "PYRE" => SaturatingMul(2, turns),
            "STONE_ARMOR" => SaturatingMul(6, turns),
            "BARRICADE" or "CORRUPTION" or "CRIMSON_MANTLE" or "DARK_EMBRACE"
                or "INFERNO" or "UNMOVABLE" => 1,
            "FAN_OF_KNIVES" => Math.Max(1, view.Features.AliveEnemyCount),
            "FOOTWORK" or "INFINITE_BLADES" or "SERPENT_FORM" or "SPEEDSTER"
                or "TOOLS_OF_THE_TRADE" or "WELL_LAID_PLANS" => 1,
            "NOXIOUS_FUMES" => Math.Max(1, view.Features.AliveEnemyCount),
            "BIASED_COGNITION" or "DEFRAGMENT" => SaturatingMul(
                Math.Max(1, view.PlayerPowerAmount("FOCUS")),
                Math.Max(1, (int)view.Battle.OrbCount)),
            "BULK_UP" => SaturatingMul(Math.Max(1, attacks), 3),
            "CAPACITOR" or "CONSUMING_SHADOW" or "COOLANT" or "CREATIVE_AI"
                or "ECHO_FORM" or "LOOP" or "MACHINE_LEARNING" or "SPINNER"
                or "HAILSTORM" => 1,
            "FURNACE" => SaturatingMul(Math.Max(1, view.PlayerPowerAmount("FURNACE")), turns),
            "GENESIS" => SaturatingMul(Math.Max(1, view.PlayerPowerAmount("GENESIS")), turns),
            "NEUTRON_AEGIS" => Math.Max(1, view.PlayerPowerAmount("PLATING")),
            "ARSENAL" or "SEEKING_EDGE" or "SPECTRUM_SHIFT" or "TYRANNY"
                or "VOID_FORM" => 1,
            "COUNTDOWN" => SaturatingMul(Math.Max(1, view.PlayerPowerAmount("COUNTDOWN")), turns),
            "LETHALITY" => SaturatingMul(Math.Max(1, view.MaxDamage()), 2),
            "CALL_OF_THE_VOID" or "DEMESNE" or "FRIENDSHIP" or "NECRO_MASTERY"
                or "NEUROSURGE" or "REAPER_FORM" or "SENTRY_MODE" => 1,
            "ETERNAL_ARMOR" => SaturatingMul(Math.Max(1, view.PlayerPowerAmount("PLATING")), turns),
            "MAYHEM" => 1,
            "PROWESS" => SaturatingMul(
                Math.Max(1, view.PlayerPowerAmount("STRENGTH")),
                Math.Max(1, attacks)),
            "ROLLING_BOULDER" => SaturatingMul(
                Math.Max(1, view.PlayerPowerAmount("ROLLING_BOULDER")), turns),
            _ => 0,
        };
    }

    /// <summary><c>*PowerOpeningProjectionPotential</c>; the frontier-based terms use the bounded
    /// attack/block/turn estimate below.</summary>
    public static int ProjectionPotential(ushort cardId, scoped in View view, in FlashBattle parent)
    {
        string id = FlashRegistry.Card(cardId).NativeId;
        int turns = Math.Max(0, view.RemainingTurns() - 1);
        int incoming = view.IncomingDamage();
        int attacks = view.CountType(FlashCardType.Attack);
        int enemies = Math.Max(1, view.Features.AliveEnemyCount);
        int gain = view.PowerAmountGain(parent, cardId);
        return id switch
        {
            "AGGRESSION" => PerTurnResource(Math.Max(1, view.MaxDamage()), turns),
            "BARRICADE" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "CORRUPTION" => PerTriggerResource(Math.Max(0, view.CountType(FlashCardType.Skill)),
                Math.Max(1, view.EnergyUnit())),
            "CRIMSON_MANTLE" => PerTurnBlock(gain, turns, incoming),
            "CRUELTY" => PerTriggerDamage(gain, view.TotalAttackDamage() / 100),
            "DARK_EMBRACE" => PerTriggerResource(gain * Math.Max(1, attacks), view.CountExhaustSources()),
            "DEMON_FORM" => GrowthFrontier(view, damagePerAttack: gain),
            "FEEL_NO_PAIN" => PerTriggerBlock(gain, view.CountExhaustSources(), incoming),
            "HELLRAISER" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "INFERNO" => PerTurnDamage(gain, turns, enemies),
            "INFLAME" => GrowthFrontier(view, damagePerAttack: gain),
            "JUGGERNAUT" => PerTriggerDamage(gain, Math.Max(1, view.CountVar("Block"))),
            "JUGGLING" => PerTurnResource(Math.Max(1, view.MaxDamage()), turns),
            "PYRE" => PerTurnResource(gain, turns),
            "RUPTURE" => GrowthFrontier(view, damagePerAttack: SaturatingMul(gain,
                Math.Max(1, view.CountVar("SelfDamage") + view.CountVar("HpLoss") + view.CountVar("LoseHp")))),
            "STAMPEDE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "STONE_ARMOR" => PerTurnBlock(gain, turns, incoming),
            "UNMOVABLE" => PerTurnBlock(Math.Max(1, view.MaxBlock()), 1, incoming),
            "VICIOUS" => PerTriggerResource(gain * Math.Max(1, view.EnergyUnit()),
                view.CountVar("Vulnerable")),
            _ => SimpleProjection(cardId, view, parent, id, turns, incoming, enemies),
        };
    }

    private static int SimpleProjection(ushort cardId, scoped in View view, in FlashBattle parent,
        string id, int turns, int incoming, int enemies)
    {
        int gain = view.PowerAmountGain(parent, cardId);
        int attacks = view.CountType(FlashCardType.Attack);
        return id switch
        {
            "AFTERIMAGE" or "ABRASIVE" => PerTriggerBlock(Math.Max(1, view.MaxBlock()), 1, incoming),
            "ACCURACY" => PerTurnDamage(Math.Max(1, view.CountShiv()), turns, 1),
            "ENVENOM" => PerTriggerDamage(Math.Max(1, view.CountType(FlashCardType.Attack)), enemies),
            "NOXIOUS_FUMES" => PerTurnDamage(gain, turns, enemies),
            "PHANTOM_BLADES" => PerTurnResource(Math.Max(1, view.CountShiv()), turns),
            "SERPENT_FORM" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "SPEEDSTER" => PerTriggerDamage(2, view.TotalDrawCount()),
            "TOOLS_OF_THE_TRADE" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "WELL_LAID_PLANS" => PerTurnResource(Math.Max(1, (int)view.HandCount), turns),
            "TRACKING" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "BIASED_COGNITION" or "DEFRAGMENT" => PerTurnResource(
                Math.Max(1, view.PlayerPowerAmount("FOCUS")), Math.Max(1, (int)view.Battle.OrbCount)),
            "BULK_UP" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "COOLANT" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "CREATIVE_AI" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "ECHO_FORM" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "FERAL" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "HAILSTORM" => PerTurnDamage(Math.Max(1, view.CountOrb("FROST")), turns, enemies),
            "ITERATION" => PerTurnResource(Math.Max(1, (int)view.HandCount), turns),
            "LOOP" => PerTurnDamage(Math.Max(1, (int)view.Battle.OrbCount), turns, 1),
            "MACHINE_LEARNING" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "SMOKESTACK" => PerTurnDamage(Math.Max(1, view.CountVar("Status")), turns, enemies),
            "SPINNER" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "STORM" => PerTurnDamage(Math.Max(1, view.EnergyUnit()), turns, enemies),
            "THUNDER" => PerTurnDamage(Math.Max(1, view.CountOrb("LIGHTNING")), turns, enemies),
            "TRASH_TO_TREASURE" => PerTurnResource(Math.Max(1, view.CountVar("Status")), turns),
            "ARSENAL" => PerTurnResource(Math.Max(1, view.CountCardGeneration()), turns),
            "BLACK_HOLE" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "CHILD_OF_THE_STARS" => PerTriggerBlock(
                Math.Max(1, view.PlayerPowerAmount("CHILD_OF_THE_STARS")),
                view.StarSpendCapacity(), incoming),
            "FURNACE" => PerTurnResource(gain, turns),
            "GENESIS" => PerTurnResource(gain, turns),
            "MONARCHS_GAZE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "NEUTRON_AEGIS" => PerTurnBlock(gain, turns, incoming),
            "ORBIT" => PerTurnResource(Math.Max(1, view.ModifiedMaxEnergy()), turns),
            "PALE_BLUE_DOT" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "PARRY" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "PILLAR_OF_CREATION" => PerTurnResource(Math.Max(1, view.CountCardGeneration()), turns),
            "SEEKING_EDGE" => PerTurnResource(gain, turns),
            "SPECTRUM_SHIFT" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "SWORD_SAGE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "THE_SEALED_THRONE" => PerTurnResource(Math.Max(1, (int)view.HandCount), turns),
            "TYRANNY" => PerTurnResource(Math.Max(1, (int)view.HandCount), turns),
            "VOID_FORM" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "CALCIFY" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "CALL_OF_THE_VOID" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "COUNTDOWN" => PerTurnDamage(gain, turns, enemies),
            "DANSE_MACABRE" => PerTurnDamage(Math.Max(1, view.ModifiedMaxEnergy()), turns, enemies),
            "DEMESNE" => PerTurnResource(Math.Max(1, view.ModifiedMaxEnergy()), turns),
            "DEVOUR_LIFE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "FRIENDSHIP" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "HAUNT" => PerTurnDamage(Math.Max(1, view.CountSouls()), turns, enemies),
            "LETHALITY" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "NECRO_MASTERY" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "NEUROSURGE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "PAGESTORM" => PerTurnResource(Math.Max(1, view.CountKeyword(FlashKeywords.Ethereal)), turns),
            "REAPER_FORM" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "SENTRY_MODE" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "SHROUD" => PerTurnDamage(gain, turns, enemies),
            "SLEIGHT_OF_FLESH" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "SPIRIT_OF_ASH" => PerTurnDamage(Math.Max(1, view.CountKeyword(FlashKeywords.Ethereal)), turns, enemies),
            "AUTOMATION" => PerTurnResource(Math.Max(1, view.ModifiedHandDraw()), turns),
            "CALAMITY" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "ENTROPY" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "ETERNAL_ARMOR" => PerTurnBlock(gain, turns, incoming),
            "FASTEN" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "MAYHEM" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "NOSTALGIA" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            "PANACHE" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, enemies),
            "PREP_TIME" => PerTurnBlock(Math.Max(1, view.MaxBlock()), turns, incoming),
            "PROWESS" => PerTurnDamage(Math.Max(1, view.MaxDamage()), turns, 1),
            "ROLLING_BOULDER" => PerTurnDamage(gain, turns, enemies),
            "STRATAGEM" => PerTurnResource(Math.Max(1, view.EnergyUnit()), turns),
            _ => 0,
        };
    }

    public static int GrowthFrontier(scoped in View view, int blockPerSkillBonus = 0,
        int blockPerCardBonus = 0, int damagePerDraw = 0, int damageTargets = 1,
        int damagePerShiv = 0, int firstShivDamageBonus = 0, int damagePerCard = 0,
        int damagePerUnblockedAttackHit = 0, int weakAttackBonusPercent = 0,
        int damagePerAttack = 0)
    {
        int incoming = view.IncomingDamage();
        PowerTurnCardOption[] baseline = BuildHandOptions(view);
        if (baseline.Length == 0)
            return 0;
        PowerTurnCardOption[] powered = weakAttackBonusPercent > 0
            ? BuildHandOptions(view, weakAttackBonusPercent)
            : baseline;
        int value = MarginalFrontierValue(
            PowerTurnFrontier.Build(view.Battle.Energy, incoming, baseline),
            PowerTurnFrontier.Build(
                view.Battle.Energy,
                incoming,
                powered,
                blockPerSkillBonus,
                blockPerCardBonus,
                damagePerDraw,
                Math.Max(1, damageTargets),
                damagePerShiv,
                firstShivDamageBonus,
                damagePerCard,
                damagePerUnblockedAttackHit,
                damagePerAttack));
        return Math.Min(view.Features.EnemyHp, value);
    }

    private static int MarginalFrontierValue(IReadOnlyList<PowerTurnFrontierState> baseline,
        IReadOnlyList<PowerTurnFrontierState> powered)
        => SaturatingAdd(
            PowerTurnFrontier.DefensiveDamageUplift(baseline, powered),
            SaturatingMul(PowerTurnFrontier.DefensiveHpUplift(baseline, powered), 8));

    private static PowerTurnCardOption[] BuildHandOptions(scoped in View view,
        int weakAttackBonusPercent = 0)
    {
        if (view.HandCount == 0)
            return [];
        int weakTargets = 0;
        int minimumEnemyBlock = int.MaxValue;
        for (int enemy = 0; enemy < view.Battle.EnemyCount; enemy++)
        {
            if (!view.Battle.Enemies[enemy].Creature.IsAlive)
                continue;
            if (view.EnemyPowerAmount(enemy, "WEAK") > 0)
                weakTargets++;
            minimumEnemyBlock = Math.Min(minimumEnemyBlock,
                Math.Max(0, view.Battle.Enemies[enemy].Creature.Block));
        }
        if (minimumEnemyBlock == int.MaxValue)
            minimumEnemyBlock = 0;
        int shivTargets = view.PlayerPowerAmount("FAN_OF_KNIVES") > 0
            ? Math.Max(1, view.Features.AliveEnemyCount)
            : 1;
        List<PowerTurnCardOption> options = new(view.HandCount);
        for (int index = 0; index < view.HandCount; index++)
        {
            FlashCard card = view.HandCard(index);
            if (view.HasKeyword(card, FlashKeywords.Unplayable))
                continue;
            int cost = Math.Max(0,
                FlashKernel.EnergyCostWithModifiers(view.Battle, view.Rules, card, FlashPile.Hand));
            bool isShiv = (view.Stats(card).Tags & FlashCardTag.Shiv) != 0;
            int baseDamage = view.Type(card) == FlashCardType.Attack ? view.Damage(card) : 0;
            int damage = isShiv ? SaturatingMul(baseDamage, shivTargets) : baseDamage;
            if (baseDamage > 0 && weakTargets > 0 && weakAttackBonusPercent > 0)
            {
                int affected = isShiv ? weakTargets : 1;
                damage = SaturatingAdd(damage,
                    (int)Math.Min(int.MaxValue,
                        (long)baseDamage * affected * weakAttackBonusPercent / 100));
            }
            int block = view.Type(card) == FlashCardType.Skill ? view.Block(card) : 0;
            int draws = view.DrawCount(card);
            int hits = baseDamage == 0 ? 0 : view.AttackHits(card);
            int unblockedHits = baseDamage == 0
                || (long)baseDamage * hits <= minimumEnemyBlock
                    ? 0
                    : Math.Max(1, hits - minimumEnemyBlock / baseDamage);
            if (damage > 0 || block > 0 || draws > 0)
            {
                options.Add(new PowerTurnCardOption(cost, damage, block,
                    CardAccess: draws, Draws: draws, IsShiv: isShiv,
                    UnblockedAttackHits: unblockedHits));
            }
        }
        return options.ToArray();
    }

    private static int PerTurnDamage(int damagePerTurn, int turns, int targets)
        => Math.Min(int.MaxValue, SaturatingMul(damagePerTurn, Math.Max(0, turns), Math.Max(1, targets)));

    private static int PerTurnBlock(int blockPerTurn, int turns, int incoming)
        => BoundedPrevention(SaturatingMul(blockPerTurn, Math.Max(0, turns)), incoming);

    private static int PerTriggerDamage(int perTrigger, int triggers)
        => SaturatingMul(perTrigger, Math.Max(0, triggers));

    private static int PerTriggerBlock(int perTrigger, int triggers, int incoming)
        => BoundedPrevention(SaturatingMul(perTrigger, Math.Max(0, triggers)), incoming);

    private static int PerTurnResource(int perTurn, int turns)
        => SaturatingMul(perTurn, Math.Max(0, turns));

    private static int PerTriggerResource(int perTrigger, int triggers)
        => SaturatingMul(perTrigger, Math.Max(0, triggers));

    private static int BoundedPrevention(int rawBlock, int incomingDamage)
    {
        int ceiling = Math.Max(Math.Max(0, incomingDamage), rawBlock / 2);
        return Math.Min(rawBlock, ceiling);
    }

    private static int SaturatingMul(int left, int right)
        => (int)Math.Min(int.MaxValue, (long)left * right);

    private static int SaturatingMul(int a, int b, int c)
        => (int)Math.Min(int.MaxValue, (long)a * b * c);

    private static int SaturatingAdd(int left, int right)
        => (int)Math.Min(int.MaxValue, (long)left + right);

    /// <summary><c>SilentPowerProgressEvidence</c>: Master Planner's Sly skill value delta.</summary>
    public static int SilentProgress(in FlashPowerCommitmentState commitment, scoped in View parent,
        scoped in View child)
    {
        if (!CommitmentHasCard(commitment, "MASTER_PLANNER"))
            return 0;
        return Math.Max(0, SilentSlySkillValue(child) - SilentSlySkillValue(parent));
    }

    /// <summary><c>SilentPowerRealizedEvidence</c> over the K4-checked state deltas and counters.</summary>
    public static int SilentRealized(in FlashPowerCommitmentState commitment,
        int parentPlayerBlock, int parentProjectedHp, int parentReachable, int parentTurn,
        int childPlayerBlock, int childProjectedHp, int childReachable, int childTurn,
        int childAliveEnemies, in FlashBattle parentBattle, in FlashBattle childBattle,
        ushort playedCardId, bool actionIsPlay, scoped in View childView, out bool handled)
    {
        long gain = 0;
        handled = false;
        if (CommitmentHasCard(commitment, "AFTERIMAGE"))
        {
            handled = true;
            if (actionIsPlay)
                gain += Math.Max(1, childPlayerBlock - parentPlayerBlock);
        }
        if (CommitmentHasCard(commitment, "FOOTWORK"))
        {
            handled = true;
            FlashCard played = default;
            if (playedCardId != ushort.MaxValue)
            {
                for (int index = 0; index < childBattle.PlayCount; index++)
                {
                    int slot = FlashPiles.Start(childBattle, FlashPile.Play) + index;
                    if (childBattle.Cards[slot].Id == playedCardId)
                    {
                        played = childBattle.Cards[slot];
                        break;
                    }
                }
            }
            if (played.Id == playedCardId && playedCardId != ushort.MaxValue
                && childView.Type(played) == FlashCardType.Skill && childView.Block(played) > 0)
            {
                gain += Math.Max(1, childPlayerBlock - parentPlayerBlock);
                gain += Math.Max(0, childProjectedHp - parentProjectedHp);
            }
        }
        if (CommitmentHasCard(commitment, "WELL_LAID_PLANS"))
        {
            handled = true;
            if (childTurn > parentTurn)
                gain += Math.Max(1, childReachable);
        }
        if (CommitmentHasCard(commitment, "MASTER_PLANNER"))
        {
            handled = true;
            gain += SlyAutoPlayValue(parentBattle, childBattle, childView);
        }
        if (CommitmentHasCard(commitment, "SPEEDSTER"))
        {
            handled = true;
            int draws = Math.Max(0,
                childBattle.Counter(FlashCounter.NonHandDrawsThisTurn)
                - parentBattle.Counter(FlashCounter.NonHandDrawsThisTurn));
            gain += (long)draws * 2 * Math.Max(0, childAliveEnemies);
        }
        if (CommitmentHasCard(commitment, "TOOLS_OF_THE_TRADE"))
        {
            handled = true;
            if (childTurn > parentTurn)
                gain += Math.Max(1, childReachable);
        }
        return (int)Math.Min(int.MaxValue, gain);
    }

    private static int SilentSlySkillValue(scoped in View view)
    {
        long total = 0;
        for (int index = 0; index < view.LiveCount; index++)
        {
            FlashCard card = view.LiveCard(index);
            if (view.Type(card) == FlashCardType.Skill
                && FlashKernel.IsSlyThisTurn(view.Battle, card))
            {
                total += Math.Max(1, view.CardValue(card));
            }
        }
        return (int)Math.Min(int.MaxValue, total);
    }

    private static int SlyAutoPlayValue(in FlashBattle parentBattle, in FlashBattle childBattle,
        scoped in View childView)
    {
        int plays = childBattle.PlayCount - parentBattle.PlayCount;
        if (plays <= 0)
            return 0;
        int start = FlashPiles.Start(childBattle, FlashPile.Play);
        int value = 0;
        for (int offset = 1; offset < plays; offset++)
        {
            FlashCard card = childBattle.Cards[start + offset];
            if (childView.Type(card) != FlashCardType.Skill
                || !FlashKernel.IsSlyThisTurn(childBattle, card))
            {
                continue;
            }
            value = SaturatingAdd(value, Math.Max(1, childView.CardValue(card)));
        }
        return value;
    }

    private static bool IsBlockSkill(scoped in View view)
    {
        for (int index = 0; index < view.LiveCount; index++)
        {
            FlashCard card = view.LiveCard(index);
            if (view.Type(card) == FlashCardType.Skill && view.Block(card) > 0)
                return true;
        }
        return false;
    }

    private static bool CommitmentHasCard(in FlashPowerCommitmentState commitment, string nativeId)
    {
        if (!FlashRegistry.TryCard(nativeId, out ushort cardId))
            return false;
        return cardId < 64
            ? (commitment.CardsLow & (1UL << cardId)) != 0
            : cardId < 128 && (commitment.CardsHigh & (1UL << (cardId - 64))) != 0;
    }
}
