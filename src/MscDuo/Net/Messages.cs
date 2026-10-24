using System.IO;
using UnityEngine;

namespace MscDuo.Net
{
    internal enum MessageType : byte
    {
        Hello = 1,
        Ping = 2,
        Pong = 3,
        PlayerState = 10,
        CarState = 11,
        ItemState = 12,
        ItemRest = 13,
        ItemsInfo = 14,
        SleepState = 15,
        ItemsMatched = 16,
        SaveRequest = 17,
        SaveData = 18,
        Interaction = 19,
        TimeState = 20,
        TrafficState = 21,
        GateState = 22,
        GateRequest = 23,
        ExteriorLight = 24,
        LiftState = 25,
        Toggle = 26,
        SaveChunk = 27,
        ItemBought = 28,
        ItemLinked = 29,
        CartState = 30,
    }

    internal static class Protocol
    {
        internal const ushort Version = 23;
    }

    internal struct PlayerStateMessage
    {
        internal const byte Player2Flag = 1;
        internal const byte PassengerFlag = 2;

        internal uint Sequence;
        internal float Time;
        internal Vector3 Position;
        internal float Yaw;
        internal float Pitch;
        internal byte Flags;
        internal byte Vehicle;
        internal Vector3 Head;
        internal ushort Gestures;
        internal byte Voice;
        internal byte VoiceCount;

        internal void Write(BinaryWriter w)
        {
            w.Write(Sequence);
            w.Write(Time);
            w.Write(Position.x);
            w.Write(Position.y);
            w.Write(Position.z);
            w.Write(Angle16(Yaw));
            w.Write(Angle16(Pitch));
            w.Write(Flags);
            w.Write(Vehicle);
            w.Write(Head.x);
            w.Write(Head.y);
            w.Write(Head.z);
            w.Write(Gestures);
            w.Write(Voice);
            w.Write(VoiceCount);
        }

        internal static PlayerStateMessage Read(BinaryReader r) => new PlayerStateMessage
        {
            Sequence = r.ReadUInt32(),
            Time = r.ReadSingle(),
            Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Yaw = FromAngle16(r.ReadUInt16()),
            Pitch = FromAngle16(r.ReadUInt16()),
            Flags = r.ReadByte(),
            Vehicle = r.ReadByte(),
            Head = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Gestures = r.ReadUInt16(),
            Voice = r.ReadByte(),
            VoiceCount = r.ReadByte(),
        };

        static ushort Angle16(float degrees) => (ushort)Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 360f * 65535f);

        static float FromAngle16(ushort value) => value / 65535f * 360f;
    }

    internal struct CarStateMessage
    {
        internal byte Car;
        internal uint Sequence;
        internal float Time;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 Velocity;
        internal Vector3 AngularVelocity;
        internal bool Driving;
        internal float Rpm;
        internal float Throttle;
        internal bool Running;
        internal byte Gear;
        internal float DiffSpeed;

        internal void Write(BinaryWriter w)
        {
            w.Write(Car);
            w.Write(Sequence);
            w.Write(Time);
            Write(w, Position);
            w.Write(Rotation.x);
            w.Write(Rotation.y);
            w.Write(Rotation.z);
            w.Write(Rotation.w);
            Write(w, Velocity);
            Write(w, AngularVelocity);
            w.Write(Driving);
            w.Write(Rpm);
            w.Write(Throttle);
            w.Write(Running);
            w.Write(Gear);
            w.Write(DiffSpeed);
        }

        internal static CarStateMessage Read(BinaryReader r) => new CarStateMessage
        {
            Car = r.ReadByte(),
            Sequence = r.ReadUInt32(),
            Time = r.ReadSingle(),
            Position = ReadVector(r),
            Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Velocity = ReadVector(r),
            AngularVelocity = ReadVector(r),
            Driving = r.ReadBoolean(),
            Rpm = r.ReadSingle(),
            Throttle = r.ReadSingle(),
            Running = r.ReadBoolean(),
            Gear = r.ReadByte(),
            DiffSpeed = r.ReadSingle(),
        };

        static void Write(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x);
            w.Write(v.y);
            w.Write(v.z);
        }

        static Vector3 ReadVector(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }

    internal struct ItemStateMessage
    {
        internal ushort Id;
        internal uint Sequence;
        internal float Time;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 Velocity;
        internal bool Held;

        internal void Write(BinaryWriter w)
        {
            w.Write(Id);
            w.Write(Sequence);
            w.Write(Time);
            w.Write(Position.x);
            w.Write(Position.y);
            w.Write(Position.z);
            w.Write(Rotation.x);
            w.Write(Rotation.y);
            w.Write(Rotation.z);
            w.Write(Rotation.w);
            w.Write(Velocity.x);
            w.Write(Velocity.y);
            w.Write(Velocity.z);
            w.Write(Held);
        }

        internal static ItemStateMessage Read(BinaryReader r) => new ItemStateMessage
        {
            Id = r.ReadUInt16(),
            Sequence = r.ReadUInt32(),
            Time = r.ReadSingle(),
            Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Rotation = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Velocity = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            Held = r.ReadBoolean(),
        };
    }

    internal static class MessageWriter
    {
        internal static byte[] Build(MessageType type, System.Action<BinaryWriter> body)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)type);
                body?.Invoke(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
