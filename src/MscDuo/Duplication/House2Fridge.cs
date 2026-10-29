using System.Collections.Generic;
using Harmony;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace MscDuo.Duplication
{
    internal static class House2Fridge
    {
        const string FridgeGlobal = "FridgePoint1";

        internal static Transform Point;
        internal static readonly List<Transform> Extras = new List<Transform>();

        [HarmonyPatch(typeof(GetDistance), "DoGetDistance")]
        static class GetDistancePatch
        {
            static void Postfix(GetDistance __instance)
            {
                if (Point == null && Extras.Count == 0 || __instance.storeResult == null) return;
                var target = __instance.target;
                if (target == null || !target.UseVariable || target.Name != FridgeGlobal) return;
                var owner = __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject);
                if (owner == null) return;
                if (Point != null)
                {
                    var distance = Vector3.Distance(owner.transform.position, Point.position);
                    if (distance < __instance.storeResult.Value) __instance.storeResult.Value = distance;
                }
                for (int i = Extras.Count - 1; i >= 0; i--)
                {
                    var extra = Extras[i];
                    if (extra == null) { Extras.RemoveAt(i); continue; }
                    if (!extra.gameObject.activeInHierarchy) continue;
                    var distance = Vector3.Distance(owner.transform.position, extra.position);
                    if (distance < __instance.storeResult.Value) __instance.storeResult.Value = distance;
                }
            }
        }
    }
}
