using System;

namespace CherryKeyLayout
{
    internal static class CherryProtocol
    {
        public const byte ReportId = 0x04;
        public const int PacketSize = 64;
        public const int ChunkSize = 56;
        public const int TotalKeys = 126;

        public static ushort CalcChecksum(byte payloadType, ReadOnlySpan<byte> data)
        {
            var len = data.Length;
            if (payloadType == 0x07 || payloadType == 0x1B)
            {
                len = Math.Min(len, 0x04);
            }

            ushort sum = payloadType;
            for (var i = 0; i < len; i++)
            {
                sum += data[i];
            }

            return sum;
        }

        public static byte[] BuildPacket(byte payloadType, ReadOnlySpan<byte> payload)
        {
            var packet = new byte[PacketSize];
            packet[0] = ReportId;

            var checksum = CalcChecksum(payloadType, payload);
            packet[1] = (byte)(checksum & 0xFF);
            packet[2] = (byte)((checksum >> 8) & 0xFF);
            packet[3] = payloadType;

            payload.CopyTo(packet.AsSpan(4));
            return packet;
        }

        public static byte[] BuildTransactionStart() => BuildPacket(0x01, ReadOnlySpan<byte>.Empty);
        public static byte[] BuildTransactionEnd() => BuildPacket(0x02, ReadOnlySpan<byte>.Empty);
        public static byte[] BuildUnknown3(byte value) => BuildPacket(0x03, new[] { value });

        public static byte[] BuildSetAnimationPayload(byte[] unknown, LightingMode mode, Brightness brightness, Speed speed, byte rainbow, Rgb color)
        {
            if (unknown == null || unknown.Length != 5)
            {
                throw new ArgumentException("Unknown array must be 5 bytes.", nameof(unknown));
            }

            return new byte[]
            {
                unknown[0], unknown[1], unknown[2], unknown[3], unknown[4],
                (byte)mode,
                (byte)brightness,
                (byte)speed,
                0x00,
                rainbow,
                color.R,
                color.G,
                color.B
            };
        }

        public static byte[] BuildSetCustomLedPayload(int offset, ReadOnlySpan<byte> data)
        {
            if (offset < 0 || offset > 0xFFFF)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (data.Length > 0xFF)
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }

            var payload = new byte[4 + data.Length];
            payload[0] = (byte)data.Length;
            payload[1] = (byte)(offset & 0xFF);
            payload[2] = (byte)((offset >> 8) & 0xFF);
            payload[3] = 0x00;
            data.CopyTo(payload.AsSpan(4));
            return payload;
        }
    }

    internal enum LightingMode : byte
    {
        Wave = 0x00,
        Spectrum = 0x01,
        Breathing = 0x02,
        Static = 0x03,
        Radar = 0x04,
        Vortex = 0x05,
        Fire = 0x06,
        Stars = 0x07,
        Custom = 0x08,
        Rolling = 0x0A,
        Rain = 0x0B,
        Curve = 0x0C,
        WaveMid = 0x0E,
        Scan = 0x0F,
        Radiation = 0x12,
        Ripples = 0x13,
        SingleKey = 0x15
    }

    internal enum Speed : byte
    {
        VeryFast = 0,
        Fast = 1,
        Medium = 2,
        Slow = 3,
        VerySlow = 4
    }

    internal enum Brightness : byte
    {
        Off = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Full = 4
    }

    internal readonly struct Rgb
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }
    }
}
