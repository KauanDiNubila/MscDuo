using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Harmony;
using MSCLoader;
using MscDuo.Duplication;
using UnityEngine;

namespace MscDuo.Tools
{
    internal class FpsCommand : ConsoleCommand
    {
        const string HarmonyId = "MscDuo.Fps";
        const float Settle = 0.5f;
        static readonly string[] Loops = { "Update", "LateUpdate", "FixedUpdate" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public override string Name => "duo_fps";
        public override string Help => "duo_fps [segundos por etapa = 4]  mede o FPS, quanto cada parte do mod gasta e quanto o FPS sobe escondendo cada grupo (fique parado)";

        static bool running;
        static List<Group> groups;
        static long[] fsmTicks;
        static int[] fsmSeen;
        static readonly Dictionary<PlayMakerFSM, int> fsmGroups = new Dictionary<PlayMakerFSM, int>();
        static readonly Dictionary<string, long> modTicks = new Dictionary<string, long>();

        class Group
        {
            internal readonly string Name;
            internal readonly List<GameObject> Roots = new List<GameObject>();

            internal Group(string name, IEnumerable<GameObject> roots)
            {
                Name = name;
                foreach (var root in roots)
                    if (root != null && !Roots.Contains(root)) Roots.Add(root);
            }
        }

        public override void Run(string[] args)
        {
            var seconds = 4f;
            if (args.Length > 0 && (!DevCommands.TryFloat(args[0], out seconds) || seconds < 1f)) { ModConsole.Error(Help); return; }
            if (running) { ModConsole.Warning("[MSC Duo] duo_fps já está medindo."); return; }
            DevCommands.StartRoutine(Measure(seconds));
        }

        static IEnumerator Measure(float seconds)
        {
            running = true;
            groups = BuildGroups();
            var harmony = HarmonyInstance.Create(HarmonyId);
            harmony.UnpatchAll(HarmonyId);
            var stages = 3 + groups.Count;
            ModConsole.Print($"<color=orange>[MSC Duo]</color> duo_fps: fique parado olhando para o mesmo lugar por uns {Mathf.CeilToInt(stages * (seconds + Settle))} s. Partes do mapa vão sumir por alguns segundos.");

            var report = new StringBuilder();
            report.AppendLine(string.Format(Inv, "# duo_fps em {0:dd/MM/yyyy HH:mm:ss}, {1} s por etapa, câmera em {2}", DateTime.Now, seconds, Camera.main != null ? HouseCloner.Format(Camera.main.transform.position) : "?"));

            var baseline = new List<float>();
            var sample = Sample(seconds, baseline);
            while (sample.MoveNext()) yield return sample.Current;
            var normal = Fps(baseline);
            report.AppendLine(string.Format(Inv, "FPS normal: {0:0.0} ({1:0.0} ms por quadro), 1% piores quadros: {2:0.0} FPS", normal, 1000f / normal, Worst(baseline)));
            report.AppendLine();

            fsmTicks = new long[groups.Count + 1];
            fsmSeen = new int[groups.Count + 1];
            fsmGroups.Clear();
            modTicks.Clear();
            var patched = Patch(harmony);
            var profiled = new List<float>();
            sample = Sample(seconds, profiled);
            while (sample.MoveNext()) yield return sample.Current;
            harmony.UnpatchAll(HarmonyId);
            foreach (var group in fsmGroups.Values) fsmSeen[group]++;
            Profile(report, profiled.Count, patched);

            report.AppendLine("## o que cada grupo tem agora");
            foreach (var group in groups) report.AppendLine(Describe(group.Name, group.Roots));
            report.AppendLine(Describe("cena inteira", null));
            report.AppendLine();

            report.AppendLine(string.Format(Inv, "## FPS escondendo cada grupo (modelos e luzes desligados, normal = {0:0.0})", normal));
            var gains = new List<KeyValuePair<string, float>>();
            var everything = new Group("tudo do MSC Duo junto", AllMine());
            foreach (var group in new List<Group>(groups) { everything })
            {
                var renderers = new List<Renderer>();
                var lights = new List<Light>();
                Hide(group.Roots, renderers, lights);
                var hidden = new List<float>();
                sample = Sample(seconds, hidden);
                while (sample.MoveNext()) yield return sample.Current;
                foreach (var r in renderers) if (r != null) r.enabled = true;
                foreach (var l in lights) if (l != null) l.enabled = true;
                var fps = Fps(hidden);
                gains.Add(new KeyValuePair<string, float>(group.Name, fps - normal));
                report.AppendLine(string.Format(Inv, "{0}: {1:0.0} FPS ({2:+0.0;-0.0}), {3} modelos e {4} luzes escondidos", group.Name, fps, fps - normal, renderers.Count, lights.Count));
            }

            var file = Save(report);
            ModConsole.Print(string.Format(Inv, "<color=orange>[MSC Duo]</color> FPS normal {0:0.0} (1% piores {1:0.0}).", normal, Worst(baseline)));
            foreach (var gain in gains) ModConsole.Print(string.Format(Inv, "  sem {0}: {1:+0.0;-0.0} FPS", gain.Key, gain.Value));
            ModConsole.Print($"<color=orange>[MSC Duo]</color> relatório completo em {file}");
            running = false;
        }

        static List<Group> BuildGroups()
        {
            var satsuma2 = new List<GameObject> { SatsumaCloner.Clone, SatsumaCloner.Parts, SatsumaCloner.Database };
            satsuma2.AddRange(SatsumaCloner.Loose);
            var copies = new List<GameObject>();
            foreach (var spec in VehicleCloner.Specs) copies.Add(VehicleCloner.CloneOf(spec.Root));
            return new List<Group>
            {
                new Group("Satsuma 2", satsuma2),
                new Group("cópias de veículos do jogador 2", copies),
                new Group("galpão", new[] { Warehouse.Root }),
                new Group("casa 2", new[] { HouseCloner.Clone }),
                new Group("Satsuma 1 (do jogo, para comparar)", new[] { SatsumaCloner.Original }),
            };
        }

        static IEnumerable<GameObject> AllMine()
        {
            for (int i = 0; i < groups.Count - 1; i++)
                foreach (var root in groups[i].Roots) yield return root;
        }

        static IEnumerator Sample(float seconds, List<float> frames)
        {
            var start = Time.realtimeSinceStartup + Settle;
            while (Time.realtimeSinceStartup < start) yield return null;
            var end = start + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime);
            }
        }

        static float Fps(List<float> frames)
        {
            var total = 0f;
            foreach (var f in frames) total += f;
            return total > 0f ? frames.Count / total : 0f;
        }

        static float Worst(List<float> frames)
        {
            if (frames.Count == 0) return 0f;
            var sorted = new List<float>(frames);
            sorted.Sort();
            var count = Mathf.Max(1, sorted.Count / 100);
            var total = 0f;
            for (int i = sorted.Count - count; i < sorted.Count; i++) total += sorted[i];
            return count / total;
        }

        static int Patch(HarmonyInstance harmony)
        {
            var prefix = new HarmonyMethod(typeof(FpsCommand).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static));
            var fsmPostfix = new HarmonyMethod(typeof(FpsCommand).GetMethod("FsmPostfix", BindingFlags.NonPublic | BindingFlags.Static));
            var modPostfix = new HarmonyMethod(typeof(FpsCommand).GetMethod("ModPostfix", BindingFlags.NonPublic | BindingFlags.Static));
            var count = 0;
            foreach (var name in Loops)
                count += TryPatch(harmony, typeof(PlayMakerFSM).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), prefix, fsmPostfix);
            count += TryPatch(harmony, typeof(MscDuo).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic), prefix, modPostfix);
            foreach (var type in typeof(FpsCommand).Assembly.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters) continue;
                foreach (var name in Loops)
                    count += TryPatch(harmony, type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), prefix, modPostfix);
            }
            return count;
        }

        static int TryPatch(HarmonyInstance harmony, MethodInfo method, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            if (method == null || method.GetParameters().Length > 0) return 0;
            try
            {
                harmony.Patch(method, prefix, postfix);
                return 1;
            }
            catch (Exception e)
            {
                ModConsole.Warning($"[MSC Duo] duo_fps não mede {method.DeclaringType.Name}.{method.Name}: {e.Message}");
                return 0;
            }
        }

        static void Prefix(out long __state) => __state = Stopwatch.GetTimestamp();

        static void FsmPostfix(PlayMakerFSM __instance, long __state)
        {
            var elapsed = Stopwatch.GetTimestamp() - __state;
            if (fsmTicks == null || __instance == null) return;
            int group;
            if (!fsmGroups.TryGetValue(__instance, out group)) fsmGroups[__instance] = group = GroupOf(__instance.transform);
            fsmTicks[group] += elapsed;
        }

        static void ModPostfix(object __instance, long __state)
        {
            var elapsed = Stopwatch.GetTimestamp() - __state;
            var name = __instance != null ? __instance.GetType().Name : "?";
            long total;
            modTicks.TryGetValue(name, out total);
            modTicks[name] = total + elapsed;
        }

        static int GroupOf(Transform t)
        {
            for (int i = 0; i < groups.Count; i++)
                foreach (var root in groups[i].Roots)
                    if (root != null && t.IsChildOf(root.transform)) return i;
            return groups.Count;
        }

        static double Ms(long ticks, int frames) => frames > 0 ? ticks * 1000.0 / Stopwatch.Frequency / frames : 0.0;

        static void Profile(StringBuilder report, int frames, int patched)
        {
            report.AppendLine($"## tempo de CPU dos FSMs (PlayMaker) por grupo, em ms por quadro ({frames} quadros, {patched} métodos medidos; medir deixa o jogo um pouco mais lento)");
            var fsmTotal = 0.0;
            for (int i = 0; i <= groups.Count; i++)
            {
                var ms = Ms(fsmTicks[i], frames);
                fsmTotal += ms;
                report.AppendLine(string.Format(Inv, "{0}: {1:0.00} ms ({2} FSMs rodando)", i < groups.Count ? groups[i].Name : "resto do jogo", ms, fsmSeen[i]));
            }
            report.AppendLine(string.Format(Inv, "total dos FSMs: {0:0.00} ms", fsmTotal));
            report.AppendLine();
            report.AppendLine("## código do MSC Duo, em ms por quadro");
            var mod = new List<KeyValuePair<string, long>>(modTicks);
            mod.Sort((a, b) => b.Value.CompareTo(a.Value));
            var modTotal = 0.0;
            foreach (var pair in mod)
            {
                var ms = Ms(pair.Value, frames);
                modTotal += ms;
                report.AppendLine(string.Format(Inv, "{0}: {1:0.000} ms", pair.Key, ms));
            }
            report.AppendLine(string.Format(Inv, "total do código do MSC Duo: {0:0.00} ms", modTotal));
            report.AppendLine();
        }

        static string Describe(string name, List<GameObject> roots)
        {
            int renderers = 0, visible = 0, lights = 0, shadows = 0, bodies = 0, awake = 0, sounds = 0, fsms = 0;
            long vertices = 0;
            foreach (var r in Collect<Renderer>(roots))
            {
                renderers++;
                if (!r.enabled || !r.isVisible) continue;
                visible++;
                vertices += Vertices(r);
            }
            foreach (var l in Collect<Light>(roots))
            {
                if (!l.enabled) continue;
                lights++;
                if (l.shadows != LightShadows.None) shadows++;
            }
            foreach (var b in Collect<Rigidbody>(roots))
            {
                if (b.isKinematic) continue;
                bodies++;
                if (!b.IsSleeping()) awake++;
            }
            foreach (var a in Collect<AudioSource>(roots)) if (a.isPlaying) sounds++;
            foreach (var f in Collect<PlayMakerFSM>(roots)) if (f.enabled) fsms++;
            return string.Format(Inv, "{0}: {1} modelos ativos ({2} na tela, {3:0.0} mil vértices na tela), {4} luzes ({5} com sombra), {6} corpos físicos ({7} acordados), {8} sons tocando, {9} FSMs ligados",
                name, renderers, visible, vertices / 1000.0, lights, shadows, bodies, awake, sounds, fsms);
        }

        static IEnumerable<T> Collect<T>(List<GameObject> roots) where T : Component
        {
            if (roots == null)
            {
                foreach (var c in UnityEngine.Object.FindObjectsOfType<T>()) yield return c;
                yield break;
            }
            foreach (var root in roots)
            {
                if (root == null) continue;
                foreach (var c in root.GetComponentsInChildren<T>()) yield return c;
            }
        }

        static long Vertices(Renderer r)
        {
            Mesh mesh = null;
            var skinned = r as SkinnedMeshRenderer;
            if (skinned != null) mesh = skinned.sharedMesh;
            else
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            if (mesh == null || mesh.name.StartsWith("Combined Mesh")) return 0;
            return mesh.vertexCount;
        }

        static void Hide(List<GameObject> roots, List<Renderer> renderers, List<Light> lights)
        {
            foreach (var r in Collect<Renderer>(roots))
                if (r.enabled) { r.enabled = false; renderers.Add(r); }
            foreach (var l in Collect<Light>(roots))
                if (l.enabled) { l.enabled = false; lights.Add(l); }
        }

        static string Save(StringBuilder report)
        {
            try
            {
                var folder = Path.Combine(MscDuo.Instance.SettingsFolder, "dumps");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "fps-" + DateTime.Now.ToString("HHmmss") + ".txt");
                File.WriteAllText(file, report.ToString());
                return file;
            }
            catch (IOException e)
            {
                ModConsole.Warning($"[MSC Duo] duo_fps não salvou o relatório: {e.Message}");
                return "(não salvo)";
            }
        }
    }
}
