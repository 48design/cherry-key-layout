using System;
using System.Linq;
using HidSharp;

namespace CherryKeyLayout
{
    internal sealed class CherryKeyboard : IDisposable
    {
        private readonly HidDevice _device;
        private readonly HidStream _stream;
        private readonly int _outputReportLength;
        private readonly int _inputReportLength;

        private CherryKeyboard(HidDevice device, HidStream stream)
        {
            _device = device;
            _stream = stream;
            _outputReportLength = Math.Max(device.GetMaxOutputReportLength(), CherryProtocol.PacketSize);
            _inputReportLength = Math.Max(device.GetMaxInputReportLength(), CherryProtocol.PacketSize);
        }

        public static CherryKeyboard Open(ushort vendorId, ushort? productId)
        {
            var devices = productId.HasValue
                ? DeviceList.Local.GetHidDevices(vendorId, productId.Value).ToList()
                : DeviceList.Local.GetHidDevices(vendorId).ToList();
            if (devices.Count == 0)
            {
                throw new InvalidOperationException("No CHERRY HID devices found.");
            }

            foreach (var device in devices)
            {
                if (device.GetMaxOutputReportLength() < CherryProtocol.PacketSize || device.GetMaxInputReportLength() < CherryProtocol.PacketSize)
                {
                    continue;
                }

                if (!device.TryOpen(out var stream))
                {
                    continue;
                }

                stream.ReadTimeout = 1000;
                stream.WriteTimeout = 1000;

                return new CherryKeyboard(device, stream);
            }

            throw new InvalidOperationException("No suitable HID interface found for CHERRY device.");
        }

        public void SetStaticColor(Rgb color, Brightness brightness)
        {
            SetAnimation(LightingMode.Static, brightness, Speed.VeryFast, color, false);
        }

        public void SetAnimation(LightingMode mode, Brightness brightness, Speed speed, Rgb color, bool rainbow)
        {
            SendRaw(CherryProtocol.BuildTransactionStart());

            var payload = CherryProtocol.BuildSetAnimationPayload(
                new byte[] { 0x09, 0x00, 0x00, 0x55, 0x00 },
                mode,
                brightness,
                speed,
                (byte)(rainbow ? 1 : 0),
                color);

            SendPayload(0x06, payload);

            var secondPayload = CherryProtocol.BuildSetAnimationPayload(
                new byte[] { 0x01, 0x18, 0x00, 0x55, 0x01 },
                LightingMode.Wave,
                Brightness.Off,
                Speed.VeryFast,
                0,
                new Rgb(0, 0, 0));

            SendPayload(0x06, secondPayload);
            SendRaw(CherryProtocol.BuildTransactionEnd());
        }

        public void SetCustomColors(IReadOnlyList<Rgb> colors, Brightness brightness, Speed speed)
        {
            SetAnimation(LightingMode.Custom, brightness, speed, new Rgb(0, 0, 0), false);

            var totalKeys = CherryProtocol.TotalKeys;
            var data = new byte[totalKeys * 3];

            for (var i = 0; i < totalKeys; i++)
            {
                var color = i < colors.Count ? colors[i] : new Rgb(0, 0, 0);
                var baseIndex = i * 3;
                data[baseIndex] = color.R;
                data[baseIndex + 1] = color.G;
                data[baseIndex + 2] = color.B;
            }

            for (var offset = 0; offset < data.Length; offset += CherryProtocol.ChunkSize)
            {
                var len = Math.Min(CherryProtocol.ChunkSize, data.Length - offset);
                var payload = CherryProtocol.BuildSetCustomLedPayload(offset, data.AsSpan(offset, len));
                SendPayload(0x0B, payload);
            }
        }

        public void SendPayload(byte payloadType, ReadOnlySpan<byte> payload)
        {
            var packet = CherryProtocol.BuildPacket(payloadType, payload);
            SendRaw(packet);
        }

        public void SendRaw(byte[] packet)
        {
            if (packet.Length != CherryProtocol.PacketSize)
            {
                throw new ArgumentException("Packet must be 64 bytes.", nameof(packet));
            }

            var output = new byte[_outputReportLength];

            if (_outputReportLength == CherryProtocol.PacketSize)
            {
                packet.CopyTo(output, 0);
            }
            else
            {
                output[0] = CherryProtocol.ReportId;
                packet.AsSpan(1).CopyTo(output.AsSpan(1));
            }

            _stream.Write(output);

            var response = new byte[_inputReportLength];
            _stream.Read(response);
        }

        public void SendFeature(byte reportId, ReadOnlySpan<byte> data)
        {
            var feature = new byte[_outputReportLength];
            feature[0] = reportId;
            data.CopyTo(feature.AsSpan(1));
            _stream.SetFeature(feature);
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }
}
