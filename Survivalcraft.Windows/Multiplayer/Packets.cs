using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Nyx
{
    public enum PacketType : byte
    {
        Hello = 1,
        Welcome = 2,
        PlayerState = 3,
        BlockChange = 4,
        PlayerJoin = 5,
        PlayerLeave = 6,
        Reject = 7
    }

    public struct Packet
    {
        public PacketType Type;
        public int Id;
        public string Text;
        public int Seed;
        public float X, Y, Z, Qx, Qy, Qz, Qw;
        public int Cx, Cy, Cz, Value;
    }

    public static class PacketIO
    {
        public const int ProtocolVersion = 1;
        const int MaxPacketSize = 4096;

        public static void Write(NetworkStream stream, Packet p)
        {
            byte[] body;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((byte)p.Type);
                switch (p.Type)
                {
                    case PacketType.Hello:
                        w.Write(ProtocolVersion);
                        w.Write(p.Seed);
                        w.Write(p.Text ?? string.Empty);
                        break;
                    case PacketType.Welcome:
                        w.Write(p.Id);
                        w.Write(p.Seed);
                        break;
                    case PacketType.PlayerState:
                        w.Write(p.Id);
                        w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
                        w.Write(p.Qx); w.Write(p.Qy); w.Write(p.Qz); w.Write(p.Qw);
                        break;
                    case PacketType.BlockChange:
                        w.Write(p.Cx); w.Write(p.Cy); w.Write(p.Cz); w.Write(p.Value);
                        break;
                    case PacketType.PlayerJoin:
                        w.Write(p.Id);
                        w.Write(p.Text ?? string.Empty);
                        break;
                    case PacketType.PlayerLeave:
                        w.Write(p.Id);
                        break;
                    case PacketType.Reject:
                        w.Write(p.Text ?? string.Empty);
                        break;
                }
                w.Flush();
                body = ms.ToArray();
            }
            var header = BitConverter.GetBytes((ushort)body.Length);
            stream.Write(header, 0, 2);
            stream.Write(body, 0, body.Length);
        }

        public static bool TryRead(NetworkStream stream, out Packet p)
        {
            p = default;
            var header = new byte[2];
            if (!ReadExact(stream, header, 2)) return false;
            int len = BitConverter.ToUInt16(header, 0);
            if (len < 1 || len > MaxPacketSize) return false;
            var body = new byte[len];
            if (!ReadExact(stream, body, len)) return false;
            using (var ms = new MemoryStream(body))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
            {
                p.Type = (PacketType)r.ReadByte();
                switch (p.Type)
                {
                    case PacketType.Hello:
                        int version = r.ReadInt32();
                        p.Seed = r.ReadInt32();
                        p.Text = r.ReadString();
                        p.Id = version;
                        break;
                    case PacketType.Welcome:
                        p.Id = r.ReadInt32();
                        p.Seed = r.ReadInt32();
                        break;
                    case PacketType.PlayerState:
                        p.Id = r.ReadInt32();
                        p.X = r.ReadSingle(); p.Y = r.ReadSingle(); p.Z = r.ReadSingle();
                        p.Qx = r.ReadSingle(); p.Qy = r.ReadSingle(); p.Qz = r.ReadSingle(); p.Qw = r.ReadSingle();
                        break;
                    case PacketType.BlockChange:
                        p.Cx = r.ReadInt32(); p.Cy = r.ReadInt32(); p.Cz = r.ReadInt32(); p.Value = r.ReadInt32();
                        break;
                    case PacketType.PlayerJoin:
                        p.Id = r.ReadInt32();
                        p.Text = r.ReadString();
                        break;
                    case PacketType.PlayerLeave:
                        p.Id = r.ReadInt32();
                        break;
                    case PacketType.Reject:
                        p.Text = r.ReadString();
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int n = stream.Read(buffer, offset, count - offset);
                if (n <= 0) return false;
                offset += n;
            }
            return true;
        }
    }
}
