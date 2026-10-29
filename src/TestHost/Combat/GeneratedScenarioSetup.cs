// Ported from CombatSolver/tools/search/OfflineSearchHarness/GeneratedScenarioSetup.cs
// at commit a62f1850 (MIT, Copyright (c) 2026 Torch1230 and contributors).
// Trimmed to request parsing, Resolve/Apply and the setup-card selector; evidence artifacts,
// protocol-host search policy and session forwarding are not carried over.
using System.Text.Json;

namespace Sts2Solver.TestHost;

internal sealed record GeneratedScenarioSetup(
    OfflineRequest Request,
    ResolvedGeneratedCombatScenario? Resolved,
    GeneratedScenarioCardSelector? Selector)
{
    internal static GeneratedScenarioSetup Prepare(OfflineRequest request,
        bool applyRelicObtainEffects = false)
    {
        if (string.IsNullOrWhiteSpace(request.GeneratedScenarioPath))
            return new(request, null, null);
        string specPath = request.GeneratedScenarioPath;
        if (!File.Exists(specPath))
            throw new FileNotFoundException($"找不到生成场景配置：{specPath}", specPath);
        GeneratedCombatScenarioOptions options = JsonSerializer.Deserialize<GeneratedCombatScenarioOptions>(
            File.ReadAllText(specPath), GeneratedCombatScenario.JsonOptions)
            ?? throw new InvalidDataException("生成场景配置为空。");
        ResolvedGeneratedCombatScenario resolved = GeneratedCombatScenario.Resolve(options);
        if (applyRelicObtainEffects && !resolved.Options.ApplyRelicObtainEffects)
        {
            resolved = resolved with
            {
                Options = resolved.Options with { ApplyRelicObtainEffects = true },
            };
        }
        OfflineRequest applied = GeneratedCombatScenario.Apply(request, resolved);
        return new(applied, resolved, new GeneratedScenarioCardSelector(resolved.Options.SetupChoices));
    }
}
