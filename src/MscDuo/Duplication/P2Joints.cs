using System.Collections.Generic;
using System.IO;
using System.Text;
using MSCLoader;
using UnityEngine;

namespace MscDuo.Duplication
{
    internal class P2Joints : MonoBehaviour
    {
        static readonly float[] Passes = { 3f, 6f, 10f, 20f, 40f };
        const float Interval = 10f;
        const int QuietPasses = 6;

        float started;
        int pass;
        float next;
        int orphans;
        int relinked;
        int sounds;
        int freed;
        int quiet;
        readonly Dictionary<PlayMakerFSM, int> attempts = new Dictionary<PlayMakerFSM, int>();
        readonly List<string> reassembled = new List<string>();
        const int MaxAttempts = 3;
        static List<GameObject> originals = new List<GameObject>();
        static Transform player;

        internal static void Watch()
        {
            var old = GameObject.Find("MscDuo_P2Joints");
            if (old != null) Destroy(old);
            new GameObject("MscDuo_P2Joints").AddComponent<P2Joints>();
        }

        void Start()
        {
            started = Time.time;
            next = started + Passes[0];
        }

        void Update()
        {
            if (Time.time < next) return;
            if (SatsumaCloner.Clone == null) { Destroy(gameObject); return; }
            pass++;
            next = pass < Passes.Length ? started + Passes[pass] : Time.time + Interval;
            var report = pass <= Passes.Length ? new StringBuilder() : null;
            var before = orphans + relinked + sounds + freed;
            originals = new List<GameObject>(OriginalRoots());
            Sweep(report);
            var fixedNow = Reassemble();
            if (fixedNow > 0)
                ModConsole.Print($"<color=orange>[MSC Duo]</color> Satsuma 2: {fixedNow} peças montadas no save mas soltas foram remontadas pelo gatilho do jogo ({string.Join(", ", reassembled.ToArray())}).");
            if (report != null) Save(report, pass);
            var changed = orphans + relinked + sounds + freed > before;
            if (changed || pass == 1)
                ModConsole.Print($"<color=orange>[MSC Duo]</color> juntas da Satsuma 2: {orphans} duplicadas removidas, {relinked} religadas, {freed} presas ao mundo na carroceria soltas, {sounds} sons presos desligados.");
            if (pass < Passes.Length) return;
            quiet = changed || fixedNow > 0 ? 0 : quiet + 1;
            if (quiet < QuietPasses) return;
            ModConsole.Print($"<color=orange>[MSC Duo]</color> juntas da Satsuma 2 estáveis; varredura encerrada depois de {pass} passadas.");
            Destroy(gameObject);
        }

        int Reassemble()
        {
            var car = SatsumaCloner.Clone;
            var db = SatsumaCloner.Database;
            if (car == null || db == null) return 0;
            var count = 0;
            foreach (var data in db.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (data.FsmName != "Data") continue;
                var installed = data.FsmVariables.FindFsmBool("Installed");
                var part = data.FsmVariables.FindFsmGameObject("ThisPart");
                var trigger = data.FsmVariables.FindFsmGameObject("Trigger");
                if (installed == null || !installed.Value || part == null || part.Value == null || trigger == null || trigger.Value == null) continue;
                if (part.Value.transform.IsChildOf(car.transform) || Inside(part.Value.transform, originals)) continue;
                if (!trigger.Value.transform.IsChildOf(car.transform)) continue;
                int tries;
                attempts.TryGetValue(data, out tries);
                if (tries >= MaxAttempts) continue;
                attempts[data] = tries + 1;

                PlayMakerFSM assembly = null;
                foreach (var fsm in trigger.Value.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "Assembly") assembly = fsm;
                if (assembly == null) continue;
                for (var t = trigger.Value.transform; t != null && t != car.transform; t = t.parent)
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                var target = assembly.FsmVariables.FindFsmGameObject("Part");
                var setup = assembly.FsmVariables.FindFsmBool("Setup");
                if (target == null || setup == null) continue;
                target.Value = part.Value;
                setup.Value = true;
                var state = assembly.ActiveStateName;
                if (!string.IsNullOrEmpty(state) && state != "Check for Part collision" && state != "Wait" && state != "Wait for assembly")
                    assembly.SendEvent("MSCDUO_Wait 2");
                if (!reassembled.Contains(data.gameObject.name)) reassembled.Add(data.gameObject.name);
                count++;
            }
            return count;
        }

        static IEnumerable<GameObject> Roots()
        {
            if (SatsumaCloner.Clone != null) yield return SatsumaCloner.Clone;
            if (SatsumaCloner.Parts != null) yield return SatsumaCloner.Parts;
            if (SatsumaCloner.Database != null) yield return SatsumaCloner.Database;
            foreach (var part in SatsumaCloner.Loose)
                if (part != null) yield return part;
        }

        static IEnumerable<GameObject> OriginalRoots()
        {
            if (SatsumaCloner.Original != null) yield return SatsumaCloner.Original;
            if (SatsumaCloner.OriginalDatabase != null) yield return SatsumaCloner.OriginalDatabase;
            var parts = SatsumaCloner.FindRoot("CARPARTS");
            if (parts != null) yield return parts;
        }

        static bool Inside(Transform t, IEnumerable<GameObject> roots)
        {
            foreach (var root in roots)
                if (root != null && t.IsChildOf(root.transform)) return true;
            return false;
        }

        static bool OnPlayer(Transform t)
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER");
                if (go == null) return false;
                player = go.transform;
            }
            return t.IsChildOf(player);
        }

        static bool Settled(GameObject car)
        {
            foreach (var fsm in car.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Setup") return fsm.ActiveStateName == "Lod ON" || fsm.ActiveStateName == "Lod OFF";
            return false;
        }

        void RemoveDuplicates<T>(HashSet<Joint> referenced) where T : Joint
        {
            var seen = new HashSet<GameObject>();
            foreach (var root in Roots())
                foreach (var joint in root.GetComponentsInChildren<T>(true))
                {
                    if (joint == null || joint.gameObject == SatsumaCloner.Clone || OnPlayer(joint.transform) || !seen.Add(joint.gameObject)) continue;
                    var all = new List<T>(joint.GetComponents<T>());
                    if (all.Count < 2) continue;
                    all.Sort((x, y) => (x.connectedBody == null ? 0 : 1).CompareTo(y.connectedBody == null ? 0 : 1));
                    var left = all.Count;
                    foreach (var candidate in all)
                    {
                        if (left <= 1) break;
                        if (referenced.Contains(candidate)) continue;
                        Destroy(candidate);
                        left--;
                        orphans++;
                    }
                }
        }

        void Sweep(StringBuilder report)
        {
            var referenced = new HashSet<Joint>();
            foreach (var root in Roots())
                foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
                    foreach (var variable in fsm.FsmVariables.ObjectVariables)
                    {
                        var joint = variable.Value as Joint;
                        if (joint != null) referenced.Add(joint);
                    }

            var map = SatsumaCloner.Map;
            var reverse = new Dictionary<GameObject, GameObject>();
            if (map != null)
                foreach (var pair in map)
                {
                    var original = pair.Key as GameObject;
                    var copy = pair.Value as GameObject;
                    if (original != null && copy != null) reverse[copy] = original;
                }

            var clone = SatsumaCloner.Clone;
            if (Settled(clone))
                foreach (var joint in clone.GetComponents<FixedJoint>())
                {
                    Destroy(joint);
                    freed++;
                }

            foreach (var root in Roots())
                foreach (var joint in root.GetComponentsInChildren<Joint>(true))
                {
                    if (joint == null || joint.gameObject == clone || OnPlayer(joint.transform)) continue;
                    var body = joint.connectedBody;
                    var p1 = body != null && Inside(body.transform, originals);
                    var used = referenced.Contains(joint);
                    report?.AppendLine($"{PathOf(joint.transform)} | {joint.GetType().Name} | ligada a {(body != null ? PathOf(body.transform) : "mundo")} | {(p1 ? "SATSUMA 1" : "ok")} | referenciada={used} | iguais no objeto={joint.GetComponents(joint.GetType()).Length}");

                    Object mapped;
                    if (p1)
                    {
                        if (map != null && map.TryGetValue(body, out mapped) && mapped is Rigidbody)
                        {
                            joint.connectedBody = (Rigidbody)mapped;
                            relinked++;
                        }
                        continue;
                    }
                    if (body != null) continue;
                    GameObject source;
                    if (!reverse.TryGetValue(joint.gameObject, out source) || source == null) continue;
                    foreach (var other in source.GetComponents(joint.GetType()))
                    {
                        var target = ((Joint)other).connectedBody;
                        if (target == null || map == null || !map.TryGetValue(target, out mapped) || !(mapped is Rigidbody)) continue;
                        joint.connectedBody = (Rigidbody)mapped;
                        relinked++;
                        break;
                    }
                }

            RemoveDuplicates<FixedJoint>(referenced);
            RemoveDuplicates<HingeJoint>(referenced);

            foreach (var root in Roots())
                foreach (var audio in root.GetComponentsInChildren<AudioSource>())
                {
                    if (audio.gameObject.name != "PouringSound" || !audio.gameObject.activeSelf) continue;
                    var trigger = audio.transform.parent != null ? audio.transform.parent.GetComponent<PlayMakerFSM>() : null;
                    if (trigger != null && trigger.ActiveStateName == "Pouring") continue;
                    audio.gameObject.SetActive(false);
                    sounds++;
                }
        }

        static void Save(StringBuilder report, int pass)
        {
            try
            {
                var folder = Path.Combine(MscDuo.Instance.SettingsFolder, "dumps");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, pass == 1 ? "juntas-p2.txt" : "juntas-p2-depois.txt"), report.ToString());
            }
            catch (IOException) { }
        }

        static string PathOf(Transform t)
        {
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
