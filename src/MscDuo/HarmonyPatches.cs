using System.Collections.Generic;
using System.Reflection;
using Harmony;
using MSCLoader;

namespace MscDuo
{
    internal static class HarmonyPatches
    {
        const string Id = "MscDuo";
        static bool applied;

        internal static void Apply()
        {
            if (applied) return;
            applied = true;
            var harmony = HarmonyInstance.Create(Id);
            var done = new List<string>();
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                var methods = type.GetHarmonyMethods();
                if (methods == null || methods.Count == 0) continue;
                var merged = HarmonyMethod.Merge(methods);
                var name = merged.declaringType != null ? merged.declaringType.Name + "." + merged.methodName : type.Name;
                try
                {
                    new PatchProcessor(harmony, type, merged).Patch();
                    done.Add(name);
                }
                catch (System.Exception e)
                {
                    ModConsole.Error($"[MSC Duo] patch {name} não aplicado: {e.Message}");
                }
            }
            ModConsole.Print($"<color=orange>[MSC Duo]</color> patches aplicados: {string.Join(", ", done.ToArray())}.");
        }
    }
}
