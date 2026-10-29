using System.Diagnostics.CodeAnalysis;
using System.Text;
using BlitzSolver.Simulation.Combat;

namespace BlitzSolver.Simulation.Run;

/// <summary>Pure, all-or-nothing validation. Message prefixes are stable rejection codes.
/// A successful check does not consume a token; the native host must invalidate/consume it.</summary>
public static class RunDecisionProtocol
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void ValidateRequest(in RunDecisionRequest request)
    {
        if (request.Token.SessionId == 0 || request.Token.Revision == 0)
            Fail("request-token", "session and revision must be nonzero");
        if (request.Kind is < RunDecision.MapPoint or > RunDecision.Treasure || request.Kind == RunDecision.GameOver)
            throw new UnsupportedException("request-kind: no pending supported choice");
        RunDecisionBuilder.CheckCount(request.OptionCount, RunLimits.DecisionOptions, "Decision.Options");
        RunDecisionBuilder.CheckCount(request.FactCount, RunDecisionLimits.Facts, "Decision.Facts");
        RunDecisionBuilder.CheckCount(request.CardCount, RunDecisionLimits.Cards, "Decision.Cards");
        RunDecisionBuilder.CheckCount(request.KeyByteCount, RunDecisionLimits.KeyBytes, "Decision.Keys");
        if (request.SelectionPreviewKnowledge != RunDecisionKnowledge.Unknown)
            Fail("selection-preview", "modification previews are not represented yet");
        if (request.Cancelable > 1 || request.RequireConfirmation > 1)
            Fail("request-flags", "boolean fields must be zero or one");
        if (request.SelectionPurpose > RunSelectionPurpose.Enchant
            || (request.Kind != RunDecision.CardSelect && request.SelectionPurpose != RunSelectionPurpose.None))
            Fail("selection-purpose", "purpose is only supported for CardSelect");
        if (request.MinSelect > request.MaxSelect || request.MaxSelect > request.OptionCount
            || request.MaxSelect > RunLimits.DecisionOptions)
            Fail("selection-bounds", "invalid min/max or option count");
        if (request.Kind != RunDecision.CardSelect && (request.MinSelect != 1 || request.MaxSelect != 1))
            Fail("selection-bounds", "ordinary screens select exactly one row");
        if (request.Source.Kind is not (RunRefKind.None or RunRefKind.Event or RunRefKind.Relic))
            Fail("source-kind", "unsupported visible source kind");
        ValidateModel(request.Source);
        ValidateKey(request, request.PageKey);
        // Validate every populated pool element, including elements not currently referenced by a row.
        for (int index = 0; index < request.CardCount; index++)
            ValidateCard(request.Cards[index]);
        for (int index = 0; index < request.FactCount; index++)
            ValidateFact(request.Facts[index]);
        ValidateUtf8(request.Keys, 0, request.KeyByteCount);
        int enabled = 0;
        RunDecisionPayloadKind cardRewardEntity = RunDecisionPayloadKind.None;
        for (int index = 0; index < request.OptionCount; index++)
        {
            ref readonly RunDecisionOption option = ref request.Options[index];
            if (option.OptionId == 0)
                Fail("option-id", "zero option id");
            for (int previous = 0; previous < index; previous++)
            {
                if (request.Options[previous].OptionId == option.OptionId)
                    Fail("option-id", "duplicate option id");
            }
            if ((option.Flags & ~(RunDecisionOptionFlags.Disabled | RunDecisionOptionFlags.Locked)) != 0)
                Fail("option-flags", "unknown row flags");
            if (option.Kind is < RunDecisionOptionKind.MapPoint or > RunDecisionOptionKind.Open)
                Fail("option-kind", "invalid option enum");
            if (option.PayloadKind > RunDecisionPayloadKind.CardBundle)
                Fail("payload-kind", "invalid payload enum");
            ValidateSlice(option.Costs, request.FactCount);
            ValidateSlice(option.Effects, request.FactCount);
            ValidateSlice(option.Bundle, request.CardCount);
            ValidateKey(request, option.NativeKey);
            ValidateKnowledge(option.CostsKnowledge, option.Costs.Count);
            ValidateKnowledge(option.EffectsKnowledge, option.Effects.Count);
            for (int fact = 0; fact < option.Costs.Count; fact++)
            {
                RunDecisionFactKind kind = request.Facts[option.Costs.Start + fact].Kind;
                if (kind is not (RunDecisionFactKind.GoldLoss or RunDecisionFactKind.HpLoss))
                    Fail("cost-kind", "effect fact used as a cost");
            }
            for (int fact = 0; fact < option.Effects.Count; fact++)
            {
                RunDecisionFactKind kind = request.Facts[option.Effects.Start + fact].Kind;
                if (kind is not (RunDecisionFactKind.GoldGain or RunDecisionFactKind.RemoveCards or RunDecisionFactKind.AddCard))
                    Fail("effect-kind", "cost fact used as an effect");
            }
            ValidatePayload(option);
            if (!Compatible(request.Kind, option))
                Fail("kind-payload", "row kind/payload does not belong to this request");
            if (request.Kind == RunDecision.CardReward
                && option.PayloadKind is RunDecisionPayloadKind.Card or RunDecisionPayloadKind.CardBundle)
            {
                if (cardRewardEntity != RunDecisionPayloadKind.None && cardRewardEntity != option.PayloadKind)
                    Fail("reward-entity", "card and bundle rows cannot share a reward request");
                cardRewardEntity = option.PayloadKind;
            }
            if (option.Flags == RunDecisionOptionFlags.None)
                enabled++;
        }
        if (enabled < request.MinSelect)
            Fail("enabled-count", "fewer selectable rows than MinSelect");
    }

    public static void ValidateAction(in RunDecisionRequest request, in RunAction action)
    {
        ValidateRequest(request);
        if (action.Token != request.Token)
            Fail("action-token", "answer belongs to another session or revision");
        RunDecisionBuilder.CheckCount(action.Count, RunLimits.DecisionOptions, "Decision.Action");
        if (action.Kind == RunActionKind.Cancel)
        {
            if (request.Cancelable == 0 || action.Count != 0)
                Fail("action-cancel", "cancel must be allowed and contain no selections");
            return;
        }
        if (action.Kind != RunActionKind.Select)
            Fail("action-kind", "invalid answer kind");
        if (action.Count < request.MinSelect || action.Count > request.MaxSelect)
            Fail("action-count", "selection count outside min/max");
        for (int index = 0; index < action.Count; index++)
        {
            uint id = action.OptionIds[index];
            for (int previous = 0; previous < index; previous++)
            {
                if (action.OptionIds[previous] == id)
                    Fail("action-duplicate", "duplicate selected id");
            }
            int resolved = Find(request, id);
            if (resolved < 0)
                Fail("action-option", "selected id does not exist");
            if (request.Options[resolved].Flags != RunDecisionOptionFlags.None)
                Fail("action-disabled", "selected row is disabled or locked");
        }
    }

    public static RunAction Select(in RunDecisionRequest request, ReadOnlySpan<uint> optionIds)
    {
        ValidateRequest(request);
        if (optionIds.Length > RunLimits.DecisionOptions)
            throw new CapacityException("Decision.Action", RunLimits.DecisionOptions);
        RunAction action = default;
        action.Token = request.Token;
        action.Kind = RunActionKind.Select;
        action.Count = checked((ushort)optionIds.Length);
        for (int index = 0; index < optionIds.Length; index++)
            action.OptionIds[index] = optionIds[index];
        ValidateAction(request, action);
        return action;
    }

    public static RunAction Cancel(in RunDecisionRequest request)
    {
        RunAction action = default;
        action.Token = request.Token;
        action.Kind = RunActionKind.Cancel;
        ValidateAction(request, action);
        return action;
    }

    public static int ResolveOptionIndex(in RunDecisionRequest request, uint optionId)
    {
        ValidateRequest(request);
        int result = Find(request, optionId);
        if (result < 0)
            Fail("action-option", "selected id does not exist");
        return result;
    }

    private static int Find(in RunDecisionRequest request, uint id)
    {
        for (int index = 0; index < request.OptionCount; index++)
        {
            if (request.Options[index].OptionId == id)
                return index;
        }
        return -1;
    }

    private static bool Compatible(RunDecision request, in RunDecisionOption row)
        => request switch
        {
            RunDecision.MapPoint => row.Kind == RunDecisionOptionKind.MapPoint,
            RunDecision.Event => row.Kind is RunDecisionOptionKind.EventOption or RunDecisionOptionKind.Leave,
            RunDecision.CombatReward => row.Kind is RunDecisionOptionKind.Reward or RunDecisionOptionKind.Leave,
            RunDecision.CardReward => row.Kind is RunDecisionOptionKind.Card or RunDecisionOptionKind.CardBundle
                or RunDecisionOptionKind.Skip or RunDecisionOptionKind.Reroll,
            RunDecision.Shop => row.Kind is RunDecisionOptionKind.ShopEntry or RunDecisionOptionKind.CardRemoval
                or RunDecisionOptionKind.Leave,
            RunDecision.RestSite => row.Kind is RunDecisionOptionKind.RestSiteOption or RunDecisionOptionKind.Leave,
            RunDecision.CardSelect => row.Kind == RunDecisionOptionKind.Card,
            RunDecision.RelicSelect => row.Kind is RunDecisionOptionKind.Relic or RunDecisionOptionKind.BossRelic
                or RunDecisionOptionKind.Skip,
            RunDecision.Treasure => row.Kind is RunDecisionOptionKind.Open or RunDecisionOptionKind.Skip or RunDecisionOptionKind.Leave,
            RunDecision.PotionDiscard => row.Kind is RunDecisionOptionKind.Potion or RunDecisionOptionKind.Skip,
            _ => false,
        };

    private static void ValidatePayload(in RunDecisionOption row)
    {
        if (row.PayloadKind != RunDecisionPayloadKind.Map
            && (!row.Coord.Equals(default) || row.PointType != RunMapPointType.Unassigned))
            Fail("payload-unused", "map data on another payload");
        if (row.PayloadKind != RunDecisionPayloadKind.Card && !CardEmpty(row.Card))
            Fail("payload-unused", "card data on another payload");
        if (row.PayloadKind is not (RunDecisionPayloadKind.Model or RunDecisionPayloadKind.Reward)
            && row.Model != default)
            Fail("payload-unused", "model data on another payload");
        if (row.PayloadKind != RunDecisionPayloadKind.Reward && row.RewardType != RunRewardType.None)
            Fail("payload-unused", "reward type on another payload");
        if (row.PayloadKind != RunDecisionPayloadKind.CardBundle && row.Bundle != default)
            Fail("payload-unused", "bundle data on another payload");
        switch (row.PayloadKind)
        {
            case RunDecisionPayloadKind.Map:
                if (row.PointType > RunMapPointType.Ancient)
                    Fail("map-point-type", "invalid point type");
                break;
            case RunDecisionPayloadKind.Card:
                ValidateCard(row.Card);
                break;
            case RunDecisionPayloadKind.Model:
                if (row.Model.Kind == RunRefKind.None)
                    Fail("payload-model", "missing model");
                ValidateModel(row.Model);
                break;
            case RunDecisionPayloadKind.Reward:
                if (row.RewardType is < RunRewardType.Gold or > RunRewardType.CardRemoval)
                    Fail("reward-type", "invalid reward type");
                RunRefKind expected = row.RewardType switch
                {
                    RunRewardType.Potion => RunRefKind.Potion,
                    RunRewardType.Relic => RunRefKind.Relic,
                    _ => RunRefKind.None,
                };
                if (row.Model.Kind != expected)
                    Fail("reward-model", "reward container/model mismatch");
                ValidateModel(row.Model);
                break;
            case RunDecisionPayloadKind.CardBundle:
                if (row.Bundle.Count == 0)
                    Fail("bundle-empty", "a bundle must contain cards");
                break;
        }
        bool shape = row.Kind switch
        {
            RunDecisionOptionKind.MapPoint => row.PayloadKind == RunDecisionPayloadKind.Map,
            RunDecisionOptionKind.EventOption => row.PayloadKind == RunDecisionPayloadKind.None
                || row.PayloadKind == RunDecisionPayloadKind.Model && row.Model.Kind is RunRefKind.Event or RunRefKind.Relic,
            RunDecisionOptionKind.Reward => row.PayloadKind == RunDecisionPayloadKind.Reward,
            RunDecisionOptionKind.Card => row.PayloadKind == RunDecisionPayloadKind.Card,
            RunDecisionOptionKind.Potion => row.PayloadKind == RunDecisionPayloadKind.Model && row.Model.Kind == RunRefKind.Potion,
            RunDecisionOptionKind.Relic or RunDecisionOptionKind.BossRelic => row.PayloadKind == RunDecisionPayloadKind.Model
                && row.Model.Kind == RunRefKind.Relic,
            RunDecisionOptionKind.ShopEntry => row.PayloadKind == RunDecisionPayloadKind.Card
                || row.PayloadKind == RunDecisionPayloadKind.Model && row.Model.Kind is RunRefKind.Potion or RunRefKind.Relic,
            RunDecisionOptionKind.CardBundle => row.PayloadKind == RunDecisionPayloadKind.CardBundle,
            _ => row.PayloadKind == RunDecisionPayloadKind.None,
        };
        if (!shape)
            Fail("kind-payload", "row kind/payload mismatch");
    }

    private static void ValidateFact(in RunDecisionFact fact)
    {
        if (fact.Kind is < RunDecisionFactKind.GoldLoss or > RunDecisionFactKind.AddCard)
            Fail("fact-kind", "invalid fact enum");
        if (fact.Amount <= 0)
            Fail("fact-amount", "facts use positive amounts");
        if (fact.Kind == RunDecisionFactKind.AddCard)
            ValidateCard(fact.Card);
        else if (!CardEmpty(fact.Card))
            Fail("fact-card", "only AddCard carries card data");
    }

    private static void ValidateCard(in RunView.CardView card)
    {
        if (card.Id >= Registry.CardCount)
            throw new UnsupportedException("card-model: unknown card");
        if (card.HasEnchantment > 1)
            Fail("card-flags", "invalid enchantment presence flag");
        if (card.HasEnchantment == 0 && (card.EnchantmentId != 0 || card.EnchantmentAmount != 0))
            Fail("card-enchantment", "inactive enchantment data");
        if (card.HasEnchantment != 0 && card.EnchantmentId >= Registry.EnchantmentCount)
            throw new UnsupportedException("enchantment-model: unknown enchantment");
    }

    private static bool CardEmpty(in RunView.CardView card) => card.Id == 0 && card.Upgrade == 0
        && card.HasEnchantment == 0 && card.EnchantmentId == 0 && card.EnchantmentAmount == 0;

    private static void ValidateModel(RunRef model)
    {
        if (model.Kind > RunRefKind.Orb || model.Kind == RunRefKind.None && model.Id != 0)
            Fail("model-ref", "invalid model reference");
        if (!RunCatalog.IsKnown(model))
            throw new UnsupportedException("model-content: unknown native model");
    }

    private static void ValidateKnowledge(RunDecisionKnowledge knowledge, int count)
    {
        if (knowledge > RunDecisionKnowledge.Complete
            || knowledge == RunDecisionKnowledge.Unknown && count != 0
            || knowledge == RunDecisionKnowledge.Partial && count == 0)
            Fail("knowledge", "invalid completeness/count combination");
    }

    private static void ValidateSlice(RunDecisionSlice slice, int count)
    {
        if (slice.Start > count || slice.Count > count - slice.Start || slice.Count == 0 && slice.Start != 0)
            Fail("payload-slice", "slice outside populated pool or noncanonical empty slice");
    }

    private static void ValidateKey(in RunDecisionRequest request, RunDecisionSlice slice)
    {
        ValidateSlice(slice, request.KeyByteCount);
        ValidateUtf8(request.Keys, slice.Start, slice.Count);
    }

    private static void ValidateUtf8(ReadOnlySpan<byte> bytes, int start, int count)
    {
        try { _ = StrictUtf8.GetCharCount(bytes.Slice(start, count)); }
        catch (DecoderFallbackException) { Fail("key-utf8", "invalid UTF-8 key or split code point"); }
    }

    [DoesNotReturn]
    private static void Fail(string reason, string detail) => throw new InvalidDataException(reason + ": " + detail);
}
