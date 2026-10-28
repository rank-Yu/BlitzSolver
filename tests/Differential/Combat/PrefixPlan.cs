// Translates the frozen flashPrefixActions/flashPrefixPlan fields with CombatSolver's parser
// semantics (FlashDifferentialProbe.Run at 91641ff0). Anything outside that grammar is rejected.
using System.Globalization;
using System.Text.Json;
using BlitzSolver.Simulation.Combat;
using BlitzSolver.Import;

namespace Differential;

internal static class PrefixPlan
{
    public static PrefixAction[] Parse(OfflineRequest request)
    {
        if (request.FlashPrefixPlan.Length != 0)
            return request.FlashPrefixPlan.Select((element, index) => ParsePlanAction(element, index)).ToArray();
        if (request.FlashPrefixActions.Length == 0)
            return [];
        List<PrefixAction> actions = [];
        int turn = 1;
        foreach (JsonElement element in request.FlashPrefixActions)
        {
            string text = element.GetString()
                ?? throw Reject("flashPrefixActions 含非字符串元素。");
            if (text == "EndTurn")
            {
                actions.Add(new PrefixAction(PrefixActionKind.EndTurn, turn));
                turn++;
                continue;
            }
            string[] parts = text.Split(':');
            if (parts.Length is < 3 or > 4 || parts[0] != "PlayCard")
                throw Reject($"flashPrefixActions 不支持 {text}。");
            int occurrence = int.Parse(parts[2], CultureInfo.InvariantCulture);
            uint? target = parts.Length == 4
                ? uint.Parse(parts[3], CultureInfo.InvariantCulture)
                : null;
            actions.Add(new PrefixAction(PrefixActionKind.PlayCard, turn,
                CardId: parts[1], CardOccurrence: occurrence, TargetCombatId: target));
        }
        return [.. actions];
    }

    private static PrefixAction ParsePlanAction(JsonElement element, int index)
    {
        string kind = RequiredString(element, "kind", index);
        int turn = RequiredInt(element, "turn", index);
        return kind switch
        {
            "EndTurn" => new PrefixAction(PrefixActionKind.EndTurn, turn),
            "PlayCard" => new PrefixAction(PrefixActionKind.PlayCard, turn,
                CardId: RequiredString(element, "cardId", index),
                CardOccurrence: OptionalInt(element, "cardOccurrence"),
                CardStateKey: OptionalString(element, "cardStateKey"),
                CardStateOccurrence: OptionalInt(element, "cardStateOccurrence"),
                TargetCombatId: OptionalTarget(element),
                Choice: OptionalChoice(element, "choice", index),
                NestedChoices: OptionalChoices(element, "nestedChoices", index),
                NestedChoicesBeforePrimary: OptionalInt(element, "nestedChoicesBeforePrimary")),
            "UsePotion" => new PrefixAction(PrefixActionKind.UsePotion, turn,
                TargetCombatId: OptionalTarget(element),
                PotionSlot: RequiredInt(element, "potionSlot", index),
                PotionId: RequiredString(element, "potionId", index),
                Choice: OptionalChoice(element, "choice", index),
                NestedChoices: OptionalChoices(element, "nestedChoices", index),
                NestedChoicesBeforePrimary: OptionalInt(element, "nestedChoicesBeforePrimary")),
            _ => throw Reject($"计划动作 {index} 的种类 {kind} 不受支持。"),
        };
    }

    private static PrefixChoice? OptionalChoice(JsonElement element, string name, int index)
    {
        if (!element.TryGetProperty(name, out JsonElement choice) || choice.ValueKind != JsonValueKind.Object)
            return null;
        return ParseChoice(choice, index);
    }

    private static PrefixChoice[]? OptionalChoices(JsonElement element, string name, int index)
    {
        if (!element.TryGetProperty(name, out JsonElement nested) || nested.ValueKind != JsonValueKind.Array)
            return null;
        return nested.EnumerateArray()
            .Select(item => ParseChoice(item, index))
            .ToArray();
    }

    private static PrefixChoice ParseChoice(JsonElement choice, int index)
    {
        string effectName = RequiredString(choice, "effect", index);
        if (!Enum.TryParse(effectName, ignoreCase: false, out ChoiceEffect effect)
            || !Enum.IsDefined(effect))
        {
            throw Reject($"计划动作 {index} 的选择效果 {effectName} 不受 Core 支持。");
        }
        string sourceName = RequiredString(choice, "sourcePile", index);
        bool sourceIsNone = sourceName == "None";
        Pile source = sourceIsNone
            ? Pile.Hand
            : Enum.TryParse(sourceName, ignoreCase: false, out Pile parsed) && Enum.IsDefined(parsed)
                ? parsed
                : throw Reject($"计划动作 {index} 的选择牌堆 {sourceName} 不受支持。");
        if (!choice.TryGetProperty("cards", out JsonElement cards) || cards.ValueKind != JsonValueKind.Array)
            throw Reject($"计划动作 {index} 的选择缺少 cards 数组。");
        PrefixCardToken[] tokens = cards.EnumerateArray()
            .Select(token => new PrefixCardToken(
                RequiredString(token, "cardId", index),
                OptionalInt(token, "upgradeLevel"),
                OptionalInt(token, "sourceOccurrence"),
                OptionalInt(token, "optionOccurrence")))
            .ToArray();
        return new PrefixChoice(effect, source, sourceIsNone, tokens);
    }

    private static uint? OptionalTarget(JsonElement element)
    {
        if (!element.TryGetProperty("targetCombatId", out JsonElement target)
            || target.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }
        return target.GetUInt32();
    }

    private static string RequiredString(JsonElement element, string name, int index)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw Reject($"计划动作 {index} 的 {name} 为空。")
            : throw Reject($"计划动作 {index} 缺少字符串字段 {name}。");

    private static string OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int RequiredInt(JsonElement element, string name, int index)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : throw Reject($"计划动作 {index} 缺少整数字段 {name}。");

    private static int OptionalInt(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;

    private static UnsupportedImportException Reject(string detail)
        => new(ImportReason.PrefixPlan, detail);
}
