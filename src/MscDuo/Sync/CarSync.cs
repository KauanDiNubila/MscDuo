using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using MSCLoader;
using MscDuo.Duplication;
using MscDuo.Net;
using MscDuo.Player;
using UnityEngine;

namespace MscDuo.Sync
{
    internal static class CarSync
    {
        const float DriverTimeout = 0.5f;
        const float StateTimeout = 1f;
        const float MaxExtrapolation = 0.25f;
        const float MoveThreshold = 0.3f;
        const float SettleSeconds = 2f;
        const float MaxSettleAfterLeaving = 6f;
        const float RootScanInterval = 60f;
        const float RootFindInterval = 2f;
        const float YieldSeconds = 1f;
        const int MaxSnapshots = 32;
        const string EngineOnEvent = "MSCDUO_ENGINE_ON";
        const string StartEngineState = "Start engine";
        const string EngineObject = "Engine";
        const float EngineRetry = 2f;
        const float RunningShare = 0.5f;

        static readonly HashSet<Drivetrain> frozen = new HashSet<Drivetrain>();
        static readonly FieldInfo engineVelocity = typeof(Drivetrain).GetField("engineAngularVelo", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static readonly string[] Vehicles = BuildVehicles();

        static string[] BuildVehicles()
        {
            var names = new List<string>
            {
                "JONNEZ ES(Clone)", "HAYOSIKO(1500kg, 250)", "GIFU(750/450psi)", "KEKMET(350-400psi)", "FERNDALE(1630kg)",
                "RCO_RUSCKO12(270)", "FLATBED", "BOAT", "COMBINE(350-400psi)",
            };
            foreach (var spec in VehicleCloner.Specs) names.Add(spec.Root + VehicleCloner.Suffix);
            foreach (var extra in ExtraVehicles.Roots) if (!names.Contains(extra)) names.Add(extra);
            return names.ToArray();
        }

        static readonly List<CarLink> links = new List<CarLink>();
        static readonly Dictionary<string, GameObject> roots = new Dictionary<string, GameObject>();
        static float nextRootScan;
        static readonly Dictionary<string, float> nextRootFind = new Dictionary<string, float>();
        static Transform player;
        static HutongGames.PlayMaker.FsmString currentVehicle;

        internal static IEnumerable<CarLink> Links => links;

        static CarSync()
        {
            links.Add(new CarLink(1, "Satsuma 1", null));
            links.Add(new CarLink(2, "Satsuma 2", null));
            for (int i = 0; i < Vehicles.Length; i++) links.Add(new CarLink(3 + i, Short(Vehicles[i]), Vehicles[i]));
        }

        internal static void Tick(float now, float remoteNow, float delay, Vector3? remotePlayer, bool isHost, bool sendDue, Action<CarStateMessage> broadcast)
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER");
                if (go != null) player = go.transform;
            }
            foreach (var link in links) link.Resolve(now);
            foreach (var link in links)
            {
                if (!link.Ready) continue;
                var inside = Inside(link);
                var peerDriving = now < link.PeerDrivingUntil;
                if (inside) { link.LastLocalDriver = true; link.LeftAt = now; }
                if (peerDriving) link.LastLocalDriver = false;

                if (link.Owned)
                    link.Sending = inside || now < link.ForcedUntil || (link.Index == PlayerContext.Active && !peerDriving);
                else
                {
                    var moving = link.Moving;
                    if (moving) link.LastMoving = now;
                    var settling = now - link.LastMoving < SettleSeconds;
                    var nearer = player != null && (!remotePlayer.HasValue ||
                        (player.position - link.Root.position).sqrMagnitude < (remotePlayer.Value - link.Root.position).sqrMagnitude);
                    var receiving = link.Snapshots.Count > 0 && now - link.LastArrival < StateTimeout;
                    var justLeft = link.LastLocalDriver && settling && now - link.LeftAt < MaxSettleAfterLeaving;
                    link.Sending = inside || now < link.ForcedUntil || (!peerDriving && now >= link.YieldUntil && (justLeft || moving && nearer && !receiving));
                    if (!inside && !justLeft) link.LastLocalDriver = false;
                }

                if (link.Sending)
                {
                    link.Release();
                    if (sendDue) broadcast(link.Capture(now, inside || now < link.ForcedUntil));
                }
                else if (link.Snapshots.Count > 0 && now - link.LastArrival < StateTimeout)
                    link.Apply(remoteNow - delay);
                else
                    link.Release();
            }
        }

        internal static void Receive(CarStateMessage state, float now, bool isHost)
        {
            var link = Find(state.Car);
            if (link == null) return;
            if (state.Driving) link.PeerDrivingUntil = now + DriverTimeout;
            if (link.Sending)
            {
                var inside = link.Ready && Inside(link);
                if (link.Owned || inside || (!state.Driving && isHost)) return;
                link.YieldUntil = now + YieldSeconds;
                link.LastLocalDriver = false;
                link.Sending = false;
            }
            if (state.Sequence <= link.LastSequence) return;
            link.LastSequence = state.Sequence;
            link.LastArrival = now;
            link.Snapshots.Add(state);
            if (link.Snapshots.Count > MaxSnapshots) link.Snapshots.RemoveAt(0);
        }

        internal static void Reset()
        {
            foreach (var link in links)
            {
                link.Release();
                link.LastSequence = 0;
                link.PeerDrivingUntil = 0f;
                link.YieldUntil = 0f;
                link.LastLocalDriver = false;
                link.Sending = false;
            }
        }

        internal static void Claim(CarLink link, float seconds)
        {
            link.ForcedUntil = Time.realtimeSinceStartup + seconds;
            link.Release();
        }

        internal static string Describe(CarLink link, float now)
        {
            var inside = link.Ready && Inside(link);
            var body = link.Body;
            var engine = link.Engine;
            return $"{link.Label}: dentro={inside} enviando={link.Sending} seguindo={link.Applying} outroDirigindo={now < link.PeerDrivingUntil} " +
                   $"kinematic={(body != null ? body.isKinematic.ToString() : "-")} motor={(engine != null ? engine.enabled + " rpm " + engine.rpm.ToString("0") + " marcha " + engine.gear : "-")} partida={link.StarterState} " +
                   $"distância={(link.Root != null && player != null ? (link.Root.position - player.position).magnitude.ToString("0.0") : "-")}";
        }

        static bool Inside(CarLink link)
        {
            if (Passenger.Active) return false;
            if (link.Owned) return CurrentVehicle.Current == link.Index;
            if (player != null && player.IsChildOf(link.Root)) return true;
            if (link.CurrentName == null) return false;
            if (currentVehicle == null) currentVehicle = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
            if (currentVehicle == null || currentVehicle.Value != link.CurrentName || player == null) return false;
            var mine = (link.Root.position - player.position).sqrMagnitude;
            foreach (var other in links)
                if (other != link && other.Ready && other.CurrentName == link.CurrentName && (other.Root.position - player.position).sqrMagnitude < mine)
                    return false;
            return true;
        }

        static string CurrentNameOf(string rootName)
        {
            if (rootName == null) return null;
            var original = rootName.EndsWith(VehicleCloner.Suffix) ? rootName.Substring(0, rootName.Length - VehicleCloner.Suffix.Length) : rootName;
            foreach (var spec in VehicleCloner.Specs)
                if (spec.Root == original) return spec.Current;
            return null;
        }

        internal static Dictionary<string, GameObject> FindRoots()
        {
            var found = new Dictionary<string, GameObject>();
            var scores = new Dictionary<string, int>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t.parent != null || t.hideFlags != HideFlags.None || Array.IndexOf(Vehicles, t.name) < 0) continue;
                var score = Score(t);
                int best;
                if (scores.TryGetValue(t.name, out best) && best >= score) continue;
                scores[t.name] = score;
                found[t.name] = t.gameObject;
            }
            return found;
        }

        static int Score(Transform t)
        {
            var score = 0;
            if (t.GetComponent<Rigidbody>() != null) score += 2;
            if (t.GetComponentsInChildren<PlayMakerFSM>(true).Length > 0) score += 1;
            return score;
        }

        static GameObject RootOf(string name, float now)
        {
            GameObject go;
            if (roots.TryGetValue(name, out go) && go != null) return go;
            float findAt;
            if (name.IndexOf('/') < 0 && (!nextRootFind.TryGetValue(name, out findAt) || now >= findAt))
            {
                nextRootFind[name] = now + RootFindInterval;
                go = GameObject.Find(name);
                if (go != null && go.transform.parent == null && go.GetComponent<Rigidbody>() != null) return roots[name] = go;
            }
            if (now < nextRootScan) return null;
            nextRootScan = now + RootScanInterval;
            roots.Clear();
            foreach (var pair in FindRoots()) roots[pair.Key] = pair.Value;
            return roots.TryGetValue(name, out go) ? go : null;
        }

        internal static bool Following(int index)
        {
            var link = Find((byte)index);
            return link != null && link.Applying;
        }

        internal static void Resolve(float now)
        {
            foreach (var link in links) link.Resolve(now);
        }

        internal static int Driven()
        {
            foreach (var link in links)
                if (link.Ready && Inside(link)) return link.Index;
            return 0;
        }

        internal static int IndexOf(Transform t)
        {
            foreach (var link in links)
                if (link.Root != null && t.IsChildOf(link.Root)) return link.Index;
            return 0;
        }

        internal static Transform RootOf(int index)
        {
            var link = Find((byte)index);
            return link != null ? link.Root : null;
        }

        internal static bool IsBike(int index)
        {
            var link = Find((byte)index);
            return link != null && link.Label.StartsWith("jonnez");
        }

        static CarLink Find(byte index)
        {
            foreach (var link in links)
                if (link.Index == index) return link;
            return null;
        }

        static string Short(string name)
        {
            var copy = name.EndsWith(VehicleCloner.Suffix);
            var cut = name.IndexOf('(');
            var text = cut > 0 ? name.Substring(0, cut) : copy ? name.Substring(0, name.Length - VehicleCloner.Suffix.Length) : name;
            return text.Trim().ToLowerInvariant() + (copy ? " 2" : "");
        }

        internal class CarLink
        {
            internal readonly int Index;
            internal readonly string Label;
            internal readonly string RootName;
            internal readonly List<CarStateMessage> Snapshots = new List<CarStateMessage>();
            internal bool Sending;
            internal float PeerDrivingUntil;
            internal float YieldUntil;
            internal float LastArrival;
            internal float LastMoving = -100f;
            internal bool LastLocalDriver;
            internal float LeftAt;
            internal float ForcedUntil;
            internal bool Ready;
            internal readonly string CurrentName;
            internal uint LastSequence;
            internal bool Applying { get; private set; }
            internal Rigidbody Body => body;
            internal Drivetrain Engine => drivetrain;
            internal bool Owned => RootName == null;
            internal Transform Root => car != null ? car.transform : null;
            internal bool Moving => body != null && (body.velocity.sqrMagnitude > MoveThreshold * MoveThreshold || body.angularVelocity.sqrMagnitude > MoveThreshold);
            GameObject car;
            Rigidbody body;
            Drivetrain drivetrain;
            bool wasKinematic;
            GameObject[] exhausts = new GameObject[0];
            bool[] exhaustsWere = new bool[0];
            PlayMakerFSM starter;
            string starterIdle;
            readonly HashSet<string> starterRunning = new HashSet<string>();
            FsmBool shutOff;
            readonly List<FsmBool> keys = new List<FsmBool>();
            float nextEngineCommand;
            uint sequence;

            internal string StarterState => starter != null ? starter.ActiveStateName : "-";

            internal CarLink(int index, string label, string rootName)
            {
                Index = index;
                Label = label;
                RootName = rootName;
                CurrentName = CurrentNameOf(rootName);
            }

            internal bool Resolve(float now)
            {
                GameObject current;
                if (Owned) current = Index == 1 ? SatsumaCloner.Original : SatsumaCloner.Clone;
                else if (car != null) current = car;
                else current = RootOf(RootName, now);
                if (current != car)
                {
                    Release();
                    car = current;
                    body = car != null ? car.GetComponent<Rigidbody>() : null;
                    drivetrain = car != null ? car.GetComponentInChildren<Drivetrain>() : null;
                    exhausts = car != null ? ExhaustsOf(car) : new GameObject[0];
                    exhaustsWere = new bool[exhausts.Length];
                    FindStarter();
                }
                Ready = body != null && car.activeInHierarchy;
                return Ready;
            }

            void FindStarter()
            {
                starter = null;
                starterRunning.Clear();
                shutOff = null;
                keys.Clear();
                if (car == null) return;
                foreach (var fsm in car.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    try
                    {
                        FsmTools.EnsureLoaded(fsm);
                        if (fsm.FsmName == "Starter" && starter == null && PrepareStarter(fsm)) continue;
                        var motorOn = fsm.FsmVariables.FindFsmBool("MotorOn");
                        var acc = fsm.FsmVariables.FindFsmBool("ACC");
                        if (motorOn != null && acc != null) { keys.Add(motorOn); keys.Add(acc); }
                    }
                    catch (Exception e)
                    {
                        ModConsole.Warning($"[MSC Duo] motor de {Label}: '{fsm.gameObject.name}/{fsm.FsmName}' não preparado: {e.Message}");
                    }
                }
            }

            bool PrepareStarter(PlayMakerFSM fsm)
            {
                string on = null;
                var best = -1;
                var running = new List<string>();
                foreach (var state in fsm.FsmStates)
                {
                    var starts = false;
                    var score = state.Name == StartEngineState ? 1 : 0;
                    foreach (var action in state.Actions)
                    {
                        if (action is EnableBehaviour enable && enable.behaviour != null && enable.behaviour.Value == "Drivetrain" &&
                            enable.enable != null && !enable.enable.UseVariable && enable.enable.Value)
                            starts = true;
                        if (action is ActivateGameObject activate && activate.activate != null && activate.activate.Value && activate.gameObject != null &&
                            activate.gameObject.GameObject.Value != null && activate.gameObject.GameObject.Value.name == EngineObject)
                            score += 2;
                        if (action is BoolTest test && test.boolVariable != null && test.boolVariable.Name == "ShutOff" && test.isTrue != null)
                            running.Add(state.Name);
                    }
                    if (starts && score > best) { best = score; on = state.Name; }
                }
                var off = fsm.FsmVariables.FindFsmBool("ShutOff");
                if (on == null || running.Count == 0 || off == null || fsm.Fsm.StartState == null) return false;
                var hooked = false;
                foreach (var transition in fsm.FsmGlobalTransitions)
                    if (transition.EventName == EngineOnEvent) hooked = true;
                if (!hooked) fsm.AddGlobalTransition(EngineOnEvent, on);
                starter = fsm;
                starterIdle = fsm.Fsm.StartState;
                starterRunning.UnionWith(running);
                shutOff = off;
                return true;
            }

            internal CarStateMessage Capture(float now, bool driving)
            {
                var engineOn = drivetrain != null && drivetrain.enabled;
                return new CarStateMessage
                {
                    Car = (byte)Index,
                    Sequence = ++sequence,
                    Time = now,
                    Position = body.position,
                    Rotation = body.rotation,
                    Velocity = body.velocity,
                    AngularVelocity = body.angularVelocity,
                    Driving = driving,
                    Rpm = engineOn ? drivetrain.rpm : 0f,
                    Throttle = engineOn ? drivetrain.throttle : 0f,
                    Running = Running(),
                    Gear = drivetrain != null ? (byte)Mathf.Clamp(drivetrain.gear, 0, 255) : (byte)0,
                    DiffSpeed = engineOn ? drivetrain.differentialSpeed : 0f,
                };
            }

            bool Running()
            {
                if (starter == null && exhausts.Length > 0)
                {
                    foreach (var exhaust in exhausts)
                        if (exhaust != null && exhaust.activeSelf) return true;
                    return false;
                }
                return drivetrain != null && drivetrain.enabled && drivetrain.rpm > drivetrain.minRPM * RunningShare;
            }

            static GameObject[] ExhaustsOf(GameObject root)
            {
                var found = new List<GameObject>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Exhaust" && t.GetComponentsInChildren<AudioSource>(true).Length > 0) found.Add(t.gameObject);
                return found.ToArray();
            }

            internal void Apply(float renderTime)
            {
                if (!Applying)
                {
                    wasKinematic = body.isKinematic;
                    body.isKinematic = true;
                    if (drivetrain != null) frozen.Add(drivetrain);
                    for (int e = 0; e < exhausts.Length; e++) exhaustsWere[e] = exhausts[e] != null && exhausts[e].activeSelf;
                    Applying = true;
                }

                CarStateMessage a, b;
                var i = Snapshots.Count - 1;
                while (i > 0 && Snapshots[i - 1].Time > renderTime) i--;
                Vector3 position;
                Quaternion rotation;
                var latest = Snapshots[Snapshots.Count - 1];
                if (renderTime >= latest.Time)
                {
                    b = latest;
                    var ahead = Mathf.Min(renderTime - b.Time, MaxExtrapolation);
                    position = b.Position + b.Velocity * ahead;
                    var spin = b.AngularVelocity.magnitude;
                    rotation = spin > 0.0001f ? Quaternion.AngleAxis(spin * Mathf.Rad2Deg * ahead, b.AngularVelocity / spin) * b.Rotation : b.Rotation;
                }
                else if (i == 0)
                {
                    position = Snapshots[0].Position;
                    rotation = Snapshots[0].Rotation;
                }
                else
                {
                    a = Snapshots[i - 1];
                    b = Snapshots[i];
                    var t = Mathf.InverseLerp(a.Time, b.Time, renderTime);
                    position = Vector3.Lerp(a.Position, b.Position, t);
                    rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
                }

                car.transform.position = position;
                car.transform.rotation = rotation;
                if (drivetrain != null)
                {
                    drivetrain.rpm = latest.Rpm;
                    drivetrain.throttle = latest.Throttle;
                    drivetrain.differentialSpeed = latest.DiffSpeed;
                    var gear = Mathf.Min(latest.Gear, drivetrain.gearRatios.Length - 1);
                    if (drivetrain.gear != gear)
                    {
                        drivetrain.gear = gear;
                        if (gear != drivetrain.neutral) drivetrain.shiftTriggered = true;
                    }
                    drivetrain.ratio = drivetrain.gearRatios[gear] * drivetrain.finalDriveRatio;
                }
                if (starter != null) FollowEngine(latest.Running);
                else
                    foreach (var exhaust in exhausts)
                        if (exhaust != null && exhaust.activeSelf != latest.Running) exhaust.SetActive(latest.Running);
            }

            void FollowEngine(bool running)
            {
                var state = starter.ActiveStateName;
                if (running == starterRunning.Contains(state)) return;
                var now = Time.realtimeSinceStartup;
                if (now < nextEngineCommand || running && state != starterIdle) return;
                nextEngineCommand = now + EngineRetry;
                foreach (var key in keys) key.Value = running;
                if (running) starter.SendEvent(EngineOnEvent);
                else shutOff.Value = true;
            }

            internal void Release()
            {
                if (Applying && body != null)
                {
                    body.isKinematic = wasKinematic;
                    if (!wasKinematic && Snapshots.Count > 0)
                    {
                        var last = Snapshots[Snapshots.Count - 1];
                        body.velocity = last.Velocity;
                        body.angularVelocity = last.AngularVelocity;
                    }
                    if (drivetrain != null)
                    {
                        frozen.Remove(drivetrain);
                        if (engineVelocity != null) engineVelocity.SetValue(drivetrain, drivetrain.rpm * drivetrain.RPM2angularVelo);
                    }
                    if (starter == null)
                        for (int e = 0; e < exhausts.Length; e++)
                            if (exhausts[e] != null && exhausts[e].activeSelf != exhaustsWere[e]) exhausts[e].SetActive(exhaustsWere[e]);
                }
                Applying = false;
                Snapshots.Clear();
            }
        }

        [HarmonyPatch(typeof(Drivetrain), "FixedUpdate")]
        static class FrozenDrivetrain
        {
            static bool Prefix(Drivetrain __instance) => !frozen.Contains(__instance);
        }
    }
}
