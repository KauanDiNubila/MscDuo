using System;
using System.IO;
using HutongGames.PlayMaker;
using MSCLoader;
using MscDuo.Duplication;
using UnityEngine;

namespace MscDuo.Sync
{
    internal static class TimeSync
    {
        const string TimePrefix = "MSCDUO_TIME_";
        const string WeatherEvent = "MSCDUO_WEATHER";
        const float SendInterval = 2f;

        static readonly string[] Blocks = { "00-02", "02-04", "04-06", "06-08", "08-10", "10-12", "12-14", "14-16", "16-18", "18-20", "20-22", "22-00" };

        static PlayMakerFSM color;
        static PlayMakerFSM rotation;
        static PlayMakerFSM weather;
        static Transform needleHour;
        static Transform needleMinute;
        static float nextSend;

        internal static int? ClockTime
        {
            get
            {
                if (color == null) color = Fsm("SUN", "Color");
                var time = color != null ? color.FsmVariables.FindFsmInt("Time") : null;
                return time != null ? time.Value : (int?)null;
            }
        }

        internal static void Patch()
        {
            color = Fsm("SUN", "Color");
            rotation = color != null && color.transform.parent != null ? Find(color.transform.parent.gameObject, "Rotation") : null;
            weather = Fsm("Clouds", "Weather");
            var clock = Fsm("Clock", "Time", "SuomiClock");
            needleHour = clock != null ? Find(clock.transform.parent, "NeedleHour") : null;
            needleMinute = clock != null ? Find(clock.transform.parent, "NeedleMinute") : null;
            var blocks = 0;
            if (color != null)
            {
                FsmTools.EnsureLoaded(color);
                foreach (var block in Blocks)
                    if (color.Fsm.GetState(block) != null)
                    {
                        color.AddGlobalTransition(TimePrefix + block, block);
                        blocks++;
                    }
            }
            var roofChecks = 0;
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmName != "RaycastAndFog") continue;
                FsmTools.EnsureLoaded(fsm);
                foreach (var state in fsm.FsmStates)
                    foreach (var action in state.Actions)
                        if (action is HutongGames.PlayMaker.Actions.Raycast ray && ray.repeatInterval != null && ray.repeatInterval.Value > 1)
                        {
                            ray.repeatInterval.Value = 1;
                            roofChecks++;
                        }
            }
            if (roofChecks > 0) ModConsole.Print($"<color=orange>[MSC Duo]</color> chuva: checagem de telhado e nuvem a cada 1 s ({roofChecks} raios).");
            if (weather != null)
            {
                FsmTools.EnsureLoaded(weather);
                if (weather.Fsm.GetState("Set cloud") != null) weather.AddGlobalTransition(WeatherEvent, "Set cloud");
            }
            ModConsole.Print($"<color=orange>[MSC Duo]</color> hora e clima: relógio {(color != null ? "ok" : "não encontrado")} ({blocks} blocos), ponteiros {(needleHour != null && needleMinute != null ? "ok" : "não encontrados")}, sol {(rotation != null ? "ok" : "não encontrado")}, nuvens {(weather != null ? "ok" : "não encontradas")}.");
        }

        internal static void Tick(float now, bool isHost, bool connected, Action<Action<BinaryWriter>> send)
        {
            if (!isHost || !connected || now < nextSend || color == null) return;
            nextSend = now + SendInterval;
            send(Write);
        }

        internal static void ForceSend() => nextSend = 0f;

        internal static bool SetBlock(string block)
        {
            if (color == null || Array.IndexOf(Blocks, block) < 0) return false;
            int hour;
            if (int.TryParse(block.Substring(0, 2), out hour)) SetInt(color, "Time", hour);
            SetFloat(color, "Minutes", 0f);
            color.SendEvent(TimePrefix + block);
            ForceSend();
            return true;
        }

        internal static bool SetWeather(int type)
        {
            if (weather == null) return false;
            SetInt(weather, "WeatherType", type);
            if (Int(weather, "WeatherCloudID") <= 0) SetInt(weather, "WeatherCloudID", 1);
            weather.SendEvent(WeatherEvent);
            CloudOverPlayer(type > 0 ? 60f : 6000f);
            ForceSend();
            return true;
        }

        static void CloudOverPlayer(float ahead)
        {
            var player = GameObject.Find("PLAYER");
            var clouds = weather.transform.Find("CloudObjects") ?? (GameObject.Find("CloudObjects") != null ? GameObject.Find("CloudObjects").transform : null);
            if (player == null || clouds == null) return;
            var cloud = clouds.Find(Int(weather, "WeatherCloudID").ToString());
            if (cloud == null && clouds.childCount > 0) cloud = clouds.GetChild(0);
            if (cloud == null) return;
            var offset = cloud.position - weather.transform.position;
            var target = player.transform.position - offset + Vector3.right * ahead;
            SetFloat(weather, "PosX", target.x);
            SetFloat(weather, "PosZ", target.z);
            weather.transform.position = new Vector3(target.x, weather.transform.position.y, target.z);
        }

        internal static string CurrentBlock => color != null ? color.ActiveStateName : "";

        internal static int CurrentWeather => weather != null ? Int(weather, "WeatherType") : -1;

        static void Write(BinaryWriter w)
        {
            w.Write(color.ActiveStateName ?? "");
            w.Write(Float(color, "Minutes"));
            w.Write(Int(color, "Time"));
            w.Write(rotation != null ? Float(rotation, "Rotation") : -1f);
            var day = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            w.Write(day != null ? day.Value : 0);
            w.Write(needleHour != null ? needleHour.localEulerAngles.y : -1f);
            w.Write(needleMinute != null ? needleMinute.localEulerAngles.y : -1f);
            w.Write(weather != null);
            if (weather == null) return;
            w.Write(Int(weather, "WeatherType"));
            w.Write(Int(weather, "WeatherCloudID"));
            w.Write(Float(weather, "ThunderForce"));
            w.Write(Float(weather, "PosX"));
            w.Write(Float(weather, "PosZ"));
            w.Write(Float(weather, "Rotation"));
            w.Write(Float(weather, "Offset"));
        }

        internal static void Read(BinaryReader r)
        {
            var block = r.ReadString();
            var minutes = r.ReadSingle();
            var time = r.ReadInt32();
            var sunRotation = r.ReadSingle();
            var day = r.ReadInt32();
            var hourAngle = r.ReadSingle();
            var minuteAngle = r.ReadSingle();
            var hasWeather = r.ReadBoolean();

            if (color != null && Array.IndexOf(Blocks, block) >= 0)
            {
                if (color.ActiveStateName != block)
                {
                    SetInt(color, "Time", time);
                    color.SendEvent(TimePrefix + block);
                }
                SetFloat(color, "Minutes", minutes);
            }
            if (rotation != null && sunRotation >= 0f) SetFloat(rotation, "Rotation", sunRotation);
            var globalDay = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            if (globalDay != null && day > 0) globalDay.Value = day;
            Turn(needleHour, hourAngle);
            Turn(needleMinute, minuteAngle);

            if (!hasWeather) return;
            var type = r.ReadInt32();
            var cloud = r.ReadInt32();
            var thunder = r.ReadSingle();
            var posX = r.ReadSingle();
            var posZ = r.ReadSingle();
            var cloudRotation = r.ReadSingle();
            var offset = r.ReadSingle();
            if (weather == null) return;
            var changed = Int(weather, "WeatherType") != type || Int(weather, "WeatherCloudID") != cloud;
            SetFloat(weather, "PosX", posX);
            SetFloat(weather, "PosZ", posZ);
            SetFloat(weather, "Rotation", cloudRotation);
            SetFloat(weather, "Offset", offset);
            weather.transform.rotation = Quaternion.Euler(0f, cloudRotation, 0f);
            if (!changed) return;
            SetInt(weather, "WeatherType", type);
            SetInt(weather, "WeatherCloudID", cloud);
            SetFloat(weather, "ThunderForce", thunder);
            weather.SendEvent(WeatherEvent);
        }

        static void Turn(Transform needle, float angle)
        {
            if (needle == null || angle < 0f) return;
            var euler = needle.localEulerAngles;
            euler.y = angle;
            needle.localEulerAngles = euler;
        }

        static Transform Find(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }

        static float Float(PlayMakerFSM fsm, string name)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            return v != null ? v.Value : 0f;
        }

        static int Int(PlayMakerFSM fsm, string name)
        {
            var v = fsm.FsmVariables.FindFsmInt(name);
            return v != null ? v.Value : 0;
        }

        static void SetFloat(PlayMakerFSM fsm, string name, float value)
        {
            var v = fsm.FsmVariables.FindFsmFloat(name);
            if (v != null) v.Value = value;
        }

        static void SetInt(PlayMakerFSM fsm, string name, int value)
        {
            var v = fsm.FsmVariables.FindFsmInt(name);
            if (v != null) v.Value = value;
        }

        static PlayMakerFSM Find(GameObject go, string fsmName)
        {
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == fsmName) return fsm;
            return null;
        }

        static PlayMakerFSM Fsm(string objectName, string fsmName, string parentName = null)
        {
            foreach (var fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                if (fsm != null && fsm.gameObject.name == objectName && fsm.FsmName == fsmName && fsm.gameObject.hideFlags == HideFlags.None &&
                    (parentName == null || fsm.transform.parent != null && fsm.transform.parent.name == parentName)) return fsm;
            return null;
        }
    }
}
