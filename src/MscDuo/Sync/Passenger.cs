using System.Collections.Generic;
using HutongGames.PlayMaker;
using MscDuo.Net;
using UnityEngine;

namespace MscDuo.Sync
{
    internal static class Passenger
    {
        const float Reach = 0.9f;
        const float BikeBack = 0.4f;
        const float BikeEyes = 0.3f;
        const float CarEyes = 0.12f;
        const float StepOut = 1.1f;
        const float BodyRadius = 0.3f;
        const string Hint = "PASSAGEIRO (ENTER)";
        static readonly string[] SingleSeat = { "kekmet", "combine", "boat", "flatbed" };
        static readonly string[] Movement = { "CharacterMotor", "FPSInputController" };

        static Transform player;
        static Transform vehicle;
        static Vector3 seat;
        static float eyes;
        static bool seated;
        static FsmString interaction;
        static FsmBool inCar;
        static bool hinting;
        static readonly Dictionary<Transform, Transform> pivots = new Dictionary<Transform, Transform>();

        internal static bool Active => vehicle != null;

        internal static Vector3 Head => vehicle.TransformPoint(seat);

        internal static void Tick(bool pressed)
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER");
                if (go == null) return;
                player = go.transform;
                inCar = null;
            }
            var camera = Camera.main;
            if (camera == null) return;

            if (Active)
            {
                player.position += Head + vehicle.up * eyes - camera.transform.position;
                if (pressed) Leave();
                return;
            }
            if (seated) GetUp();

            if (NetSession.Current == null) CarSync.Resolve(Time.realtimeSinceStartup);
            Transform near = null;
            var nearSeat = Vector3.zero;
            var nearBike = false;
            if (interaction == null) interaction = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
            var gameBusy = interaction != null && interaction.Value != "" && interaction.Value != Hint;
            if (player.parent == null && !gameBusy)
                foreach (var link in CarSync.Links)
                {
                    Vector3 local;
                    bool bike;
                    Vector3 driver;
                    if (link.Root == null || !SeatOf(link, out local, out driver, out bike)) continue;
                    var toSeat = (link.Root.TransformPoint(local) - camera.transform.position).sqrMagnitude;
                    if (toSeat > Reach * Reach || toSeat >= (link.Root.TransformPoint(driver) - camera.transform.position).sqrMagnitude) continue;
                    near = link.Root;
                    nearSeat = local;
                    nearBike = bike;
                    break;
                }
            ShowHint(near != null);
            if (near != null && pressed) Enter(near, nearSeat, nearBike);
        }

        static bool SeatOf(CarSync.CarLink link, out Vector3 local, out Vector3 driver, out bool bike)
        {
            local = driver = Vector3.zero;
            bike = link.Label.StartsWith("jonnez");
            foreach (var single in SingleSeat)
                if (link.Label.StartsWith(single)) return false;
            Transform pivot;
            if (!pivots.TryGetValue(link.Root, out pivot)) pivots[link.Root] = pivot = Find(link.Root, "DriverHeadPivot");
            if (pivot == null) return false;
            local = driver = link.Root.InverseTransformPoint(pivot.position);
            if (bike) local.z -= BikeBack;
            else local.x = -local.x;
            return true;
        }

        static void Enter(Transform target, Vector3 local, bool bike)
        {
            ShowHint(false);
            vehicle = target;
            seat = local;
            eyes = bike ? BikeEyes : CarEyes;
            seated = true;
            SetMovement(false);
            player.parent = vehicle;
            player.rotation = Quaternion.LookRotation(vehicle.forward, vehicle.up);
            SetInCar(true);
        }

        static void Leave()
        {
            var outside = ExitPoint();
            var yaw = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            vehicle = null;
            player.parent = null;
            player.position = outside;
            player.rotation = yaw;
            GetUp();
        }

        static void GetUp()
        {
            seated = false;
            vehicle = null;
            if (player.parent != null) player.parent = null;
            SetMovement(true);
            SetInCar(false);
        }

        static Vector3 ExitPoint()
        {
            var head = Head;
            var side = (seat.x >= 0f ? 1f : -1f) * vehicle.right;
            foreach (var direction in new[] { side, -side, -vehicle.forward, vehicle.forward })
                foreach (var distance in new[] { StepOut, StepOut + 0.6f })
                {
                    if (Blocked(head, direction, distance)) continue;
                    var spot = head + direction * distance;
                    RaycastHit hit;
                    if (!Physics.Raycast(spot + Vector3.up, Vector3.down, out hit, 4f) || Mine(hit.collider)) continue;
                    if (Crowded(hit.point)) continue;
                    return hit.point + Vector3.up * 0.3f;
                }
            return head + side * StepOut;
        }

        static bool Blocked(Vector3 from, Vector3 direction, float distance)
        {
            foreach (var hit in Physics.RaycastAll(from, direction, distance + BodyRadius))
                if (!hit.collider.isTrigger && !Mine(hit.collider)) return true;
            return false;
        }

        static bool Crowded(Vector3 feet)
        {
            foreach (var height in new[] { BodyRadius + 0.1f, 1.4f })
                foreach (var collider in Physics.OverlapSphere(feet + Vector3.up * height, BodyRadius))
                    if (!collider.isTrigger && !Mine(collider)) return true;
            return false;
        }

        static bool Mine(Collider collider) => collider.transform.IsChildOf(player) || vehicle != null && collider.transform.IsChildOf(vehicle);

        static void SetMovement(bool on)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = on;
            foreach (var name in Movement)
            {
                var behaviour = player.GetComponent(name) as Behaviour;
                if (behaviour != null) behaviour.enabled = on;
            }
        }

        static void SetInCar(bool on)
        {
            if (inCar == null)
                foreach (var fsm in player.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "Crouch") inCar = fsm.FsmVariables.FindFsmBool("PlayerInCar");
            if (inCar != null) inCar.Value = on;
        }

        static void ShowHint(bool on)
        {
            if (!on && !hinting) return;
            if (interaction == null) interaction = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
            if (interaction == null) return;
            if (on) interaction.Value = Hint;
            else if (interaction.Value == Hint) interaction.Value = "";
            hinting = on;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
