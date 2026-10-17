using UnityEngine;

namespace MscDuo.Sync
{
    internal static class Gestures
    {
        internal const ushort MiddleFinger = 1;
        internal const ushort Punch = 2;
        internal const ushort Wave = 4;
        internal const ushort Smoke = 8;
        internal const ushort Drink = 16;
        internal const ushort Pee = 32;
        internal const ushort Watch = 64;
        internal const ushort Crouch = 128;
        internal const ushort CrouchLow = 256;

        static readonly string[] Hands = { "MiddleFinger", "Fist", "Hello", "Smoking", "Drink/Hand", "Watch" };
        static readonly ushort[] HandFlags = { MiddleFinger, Punch, Wave, Smoke, Drink, Watch };
        static readonly string[] VoiceGroups = { "Swearing", "Fuck" };

        static Transform camera;
        static readonly Transform[] hands = new Transform[Hands.Length];
        static Transform pee;
        static PlayMakerFSM crouch;
        static AudioSource[][] voices;
        static bool[][] playing;
        static byte lastVoice;
        static byte voiceCount;

        internal static ushort Capture()
        {
            var main = Camera.main;
            if (main == null) return 0;
            if (camera != main.transform)
            {
                camera = main.transform;
                voices = null;
                for (int i = 0; i < Hands.Length; i++) hands[i] = camera.Find(Hands[i]);
                pee = camera.parent != null ? camera.parent.Find("Piss/Fluid") : null;
                crouch = null;
                var player = GameObject.Find("PLAYER");
                if (player != null) foreach (var fsm in player.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Crouch") crouch = fsm;
            }
            ushort flags = 0;
            for (int i = 0; i < hands.Length; i++)
                if (hands[i] != null && hands[i].gameObject.activeInHierarchy) flags |= HandFlags[i];
            if (pee != null && pee.gameObject.activeInHierarchy) flags |= Pee;
            var state = crouch != null ? crouch.ActiveStateName : "";
            if (state == "Move down 1" || state == "Crouched 1") flags |= Crouch;
            else if (state == "Move down 2" || state == "Crouched 2") flags |= CrouchLow;
            return flags;
        }

        internal static void CaptureVoice(out byte voice, out byte count)
        {
            if (voices == null) FindVoices();
            for (int g = 0; g < voices.Length; g++)
                for (int v = 0; v < voices[g].Length; v++)
                {
                    var now = voices[g][v] != null && voices[g][v].isPlaying;
                    if (now && !playing[g][v])
                    {
                        lastVoice = (byte)((g + 1) * 16 + v);
                        voiceCount++;
                    }
                    playing[g][v] = now;
                }
            voice = lastVoice;
            count = voiceCount;
        }

        internal static AudioClip VoiceClip(byte voice)
        {
            if (voices == null) FindVoices();
            var g = voice / 16 - 1;
            var v = voice % 16;
            if (g < 0 || g >= voices.Length || v >= voices[g].Length || voices[g][v] == null) return null;
            return voices[g][v].clip;
        }

        static void FindVoices()
        {
            var master = GameObject.Find("MasterAudio");
            voices = new AudioSource[VoiceGroups.Length][];
            playing = new bool[VoiceGroups.Length][];
            for (int g = 0; g < VoiceGroups.Length; g++)
            {
                var group = master != null ? master.transform.Find(VoiceGroups[g]) : null;
                voices[g] = group != null ? group.GetComponentsInChildren<AudioSource>(true) : new AudioSource[0];
                playing[g] = new bool[voices[g].Length];
            }
        }
    }
}
