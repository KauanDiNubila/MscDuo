using MscDuo.Net;
using MscDuo.Player;
using UnityEngine;

namespace MscDuo.Sync
{
    internal static class Local
    {
        static Transform player;

        internal static PlayerStateMessage? Capture()
        {
            if (player == null)
            {
                var go = GameObject.Find("PLAYER");
                if (go == null) return null;
                player = go.transform;
            }

            var camera = Camera.main;
            var forward = camera != null ? camera.transform.forward : player.forward;
            var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            var pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;

            var vehicle = player.parent != null ? CarSync.IndexOf(player) : 0;
            if (vehicle == 0) vehicle = CarSync.Driven();
            var root = vehicle != 0 ? CarSync.RootOf(vehicle) : null;
            var head = Passenger.Active ? Passenger.Head : camera != null ? camera.transform.position : player.position;
            byte voice, voiceCount;
            Gestures.CaptureVoice(out voice, out voiceCount);
            var flags = PlayerContext.Active == 2 ? PlayerStateMessage.Player2Flag : (byte)0;
            if (Passenger.Active) flags |= PlayerStateMessage.PassengerFlag;

            return new PlayerStateMessage
            {
                Position = player.position,
                Yaw = yaw,
                Pitch = pitch,
                Flags = (byte)flags,
                Vehicle = (byte)vehicle,
                Head = root != null ? root.InverseTransformPoint(head) : head,
                Gestures = Gestures.Capture(),
                Voice = voice,
                VoiceCount = voiceCount,
            };
        }
    }
}
