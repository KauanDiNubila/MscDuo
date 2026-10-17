using System.Collections.Generic;
using MscDuo.Net;
using UnityEngine;

namespace MscDuo.Sync
{
    internal class RemotePlayer
    {
        const int MaxSnapshots = 32;
        const float MaxExtrapolation = 0.25f;
        const float MinDelay = 0.05f;
        const float MaxDelay = 0.4f;

        readonly List<PlayerStateMessage> snapshots = new List<PlayerStateMessage>();
        readonly Avatar avatar;
        readonly Vector3 displayOffset;
        uint lastSequence;
        float lastArrival = -1f;
        float jitter;

        internal float InterpolationDelay { get; private set; } = 0.1f;
        internal int Buffered => snapshots.Count;
        internal int Received { get; private set; }
        internal int Lost { get; private set; }
        internal bool Extrapolating { get; private set; }
        internal Vector3 Position { get; private set; }

        internal RemotePlayer(string name, Vector3 displayOffset)
        {
            this.displayOffset = displayOffset;
            avatar = new Avatar(name);
        }

        internal void Push(PlayerStateMessage state, float arrival)
        {
            if (state.Sequence <= lastSequence) return;
            if (lastSequence != 0 && state.Sequence > lastSequence + 1) Lost += (int)(state.Sequence - lastSequence - 1);
            lastSequence = state.Sequence;
            Received++;

            if (lastArrival >= 0f)
            {
                var deviation = Mathf.Abs(arrival - lastArrival - 1f / NetSession.SendRate);
                jitter = Mathf.Lerp(jitter, deviation, 0.1f);
            }
            lastArrival = arrival;
            InterpolationDelay = Mathf.Clamp(2f / NetSession.SendRate + jitter * 3f, MinDelay, MaxDelay);

            snapshots.Add(state);
            if (snapshots.Count > MaxSnapshots) snapshots.RemoveAt(0);
        }

        internal void Update(float remoteNow)
        {
            if (snapshots.Count == 0) return;
            var renderTime = remoteNow - InterpolationDelay;

            PlayerStateMessage a, b;
            float t;
            if (renderTime <= snapshots[0].Time)
            {
                a = b = snapshots[0];
                t = 0f;
                Extrapolating = false;
            }
            else
            {
                var i = snapshots.Count - 1;
                while (i > 0 && snapshots[i - 1].Time > renderTime) i--;
                if (i == snapshots.Count - 1 && renderTime > snapshots[i].Time)
                {
                    b = snapshots[i];
                    a = i > 0 ? snapshots[i - 1] : b;
                    var span = Mathf.Max(0.001f, b.Time - a.Time);
                    var ahead = Mathf.Min(renderTime - b.Time, MaxExtrapolation);
                    t = 1f + ahead / span;
                    Extrapolating = true;
                }
                else
                {
                    a = snapshots[i - 1];
                    b = snapshots[i];
                    t = Mathf.InverseLerp(a.Time, b.Time, renderTime);
                    Extrapolating = false;
                }
            }

            var position = a.Position + (b.Position - a.Position) * t;
            var yaw = a.Yaw + Mathf.DeltaAngle(a.Yaw, b.Yaw) * t;
            var pitch = a.Pitch + Mathf.DeltaAngle(a.Pitch, b.Pitch) * Mathf.Clamp01(t);

            Position = position;
            if (SaveTransfer.InMenu) return;
            avatar.SetSkin((b.Flags & PlayerStateMessage.Player2Flag) != 0 ? 2 : 1);
            var seat = b.Vehicle != 0 ? CarSync.RootOf(b.Vehicle) : null;
            if (seat != null) avatar.Sit(seat, seat.TransformPoint(b.Head), CarSync.IsBike(b.Vehicle), (b.Flags & PlayerStateMessage.PassengerFlag) != 0);
            else avatar.Apply(position + displayOffset, yaw, pitch);
            avatar.Express(b.Gestures, b.Voice, b.VoiceCount);
        }

        internal void Dispose()
        {
            avatar.Destroy();
        }
    }
}
