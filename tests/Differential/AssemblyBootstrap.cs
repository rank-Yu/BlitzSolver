// Ported from CombatSolver/tools/search/OfflineSearchHarness/AssemblyBootstrap.cs
// at commit a62f1850 (MIT, Copyright (c) 2026 Torch1230 and contributors).
// Adapted for the differential host: the harness no longer loads the CombatSolver assembly.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Differential;

/// <summary>
/// 运行时程序集解析。模块初始化器在 Main 之前跑，所以任何引用 sts2 的方法被 JIT 之前
/// 解析器已经装好了。
/// </summary>
internal static class AssemblyBootstrap
{
    public static string Sts2DataDir { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Sts2DataDir = (string?)AppContext.GetData("Sts2DataDir") ?? string.Empty;
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(name.Name) || Sts2DataDir.Length == 0)
            return null;
        string candidate = Path.Combine(Sts2DataDir, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(Path.GetFullPath(candidate)) : null;
    }
}
