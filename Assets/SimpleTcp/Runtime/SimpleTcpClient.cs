using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace work.ctrl3d
{
    public class SimpleTcpClient : IDisposable
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private TcpClient _client;
        private CancellationTokenSource _cts;

        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnConnectionFailed;
        public event Action<string> OnMessageReceived;
        public event Action<byte[]> OnBytesReceived;
        public event Action OnDisconnected;

        public int MaxMessageSize { get; set; } = SimpleTcpProtocol.DefaultMaxMessageSize;

        public bool IsConnected => _client is { Connected: true };

        public void Connect(string ip, int port)
        {
            if (IsConnected) return;

            try
            {
                _client = new TcpClient();
                _client.NoDelay = true;
                _client.Connect(ip, port);

                _cts = new CancellationTokenSource();

                OnLog?.Invoke($"Connected to server ({ip}:{port})");
                OnConnected?.Invoke();

                Task.Run(() => ReceiveLoop(_cts.Token));
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"Connection failed: {e.Message}");
                OnConnectionFailed?.Invoke();
                Disconnect();
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            var stream = _client.GetStream();

            try
            {
                while (!token.IsCancellationRequested && IsConnected)
                {
                    var frame = await SimpleTcpProtocol.ReadFrameAsync(stream, MaxMessageSize, token);
                    if (frame == null) break; // Server closed connection

                    switch (frame.Type)
                    {
                        case SimpleTcpFrameType.String:
                            var message = Encoding.UTF8.GetString(frame.Payload, 0, frame.Payload.Length);
                            OnMessageReceived?.Invoke(message);
                            break;
                        case SimpleTcpFrameType.Bytes:
                            OnBytesReceived?.Invoke(frame.Payload);
                            break;
                        default:
                            throw new InvalidDataException($"Unknown frame type: {(byte)frame.Type}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Connection cancelled
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"Receive failed: {e.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        public async void SendString(string message)
        {
            await SendStringAsync(message);
        }

        public Task SendStringAsync(string message)
        {
            var payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return SendFrameAsync(SimpleTcpFrameType.String, payload);
        }

        public async void SendBytes(byte[] bytes)
        {
            await SendBytesAsync(bytes);
        }

        public Task SendBytesAsync(byte[] bytes)
        {
            return SendFrameAsync(SimpleTcpFrameType.Bytes, bytes ?? Array.Empty<byte>());
        }

        private async Task SendFrameAsync(SimpleTcpFrameType frameType, byte[] payload)
        {
            var client = _client;
            if (client == null || !client.Connected) return;

            try
            {
                await SimpleTcpProtocol.WriteFrameAsync(
                    client.GetStream(),
                    _sendLock,
                    frameType,
                    payload,
                    MaxMessageSize,
                    CancellationToken.None);
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"Send failed: {e.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            var client = _client;
            if (client == null) return;

            _client = null;
            _cts?.Cancel();

            try
            {
                client.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }

            OnDisconnected?.Invoke();
            OnLog?.Invoke("Disconnected");
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
            _cts = null;
            _sendLock.Dispose();
        }
    }

    internal enum SimpleTcpFrameType : byte
    {
        String = 1,
        Bytes = 2
    }

    internal sealed class SimpleTcpFrame
    {
        public SimpleTcpFrame(SimpleTcpFrameType type, byte[] payload)
        {
            Type = type;
            Payload = payload;
        }

        public SimpleTcpFrameType Type { get; }
        public byte[] Payload { get; }
    }

    public enum SimpleTcpMessageSize
    {
        [InspectorName("1 MiB")]
        MiB1 = 1 * 1024 * 1024,
        [InspectorName("5 MiB")]
        MiB5 = 5 * 1024 * 1024,
        [InspectorName("8 MiB")]
        MiB8 = 8 * 1024 * 1024,
        [InspectorName("16 MiB")]
        MiB16 = 16 * 1024 * 1024,
        [InspectorName("32 MiB")]
        MiB32 = 32 * 1024 * 1024,
        [InspectorName("64 MiB")]
        MiB64 = 64 * 1024 * 1024,
        [InspectorName("128 MiB")]
        MiB128 = 128 * 1024 * 1024
    }

    internal static class SimpleTcpProtocol
    {
        public const int DefaultMaxMessageSize = (int)SimpleTcpMessageSize.MiB32;

        private const int HeaderSize = 5;
        private const int PayloadLengthOffset = 1;

        public static async Task<SimpleTcpFrame> ReadFrameAsync(
            NetworkStream stream,
            int maxMessageSize,
            CancellationToken token)
        {
            ValidateMaxMessageSize(maxMessageSize);

            var header = new byte[HeaderSize];
            if (!await ReadExactAsync(stream, header, header.Length, token))
            {
                return null;
            }

            var payloadLength = ReadPayloadLength(header);
            if (payloadLength < 0 || payloadLength > maxMessageSize)
            {
                throw new InvalidDataException(
                    $"Invalid payload length: {payloadLength} bytes. Max allowed: {maxMessageSize} bytes.");
            }

            var payload = payloadLength == 0 ? Array.Empty<byte>() : new byte[payloadLength];
            if (payloadLength > 0 && !await ReadExactAsync(stream, payload, payload.Length, token))
            {
                return null;
            }

            return new SimpleTcpFrame((SimpleTcpFrameType)header[0], payload);
        }

        public static async Task WriteFrameAsync(
            NetworkStream stream,
            SemaphoreSlim sendLock,
            SimpleTcpFrameType frameType,
            byte[] payload,
            int maxMessageSize,
            CancellationToken token)
        {
            ValidateMaxMessageSize(maxMessageSize);

            payload ??= Array.Empty<byte>();
            if (payload.Length > maxMessageSize)
            {
                throw new InvalidDataException(
                    $"Payload size {payload.Length} bytes exceeds max message size {maxMessageSize} bytes.");
            }

            var header = new byte[HeaderSize];
            header[0] = (byte)frameType;
            WritePayloadLength(header, payload.Length);

            await sendLock.WaitAsync(token);
            try
            {
                await stream.WriteAsync(header, 0, header.Length, token);
                if (payload.Length > 0)
                {
                    await stream.WriteAsync(payload, 0, payload.Length, token);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static async Task<bool> ReadExactAsync(
            NetworkStream stream,
            byte[] buffer,
            int length,
            CancellationToken token)
        {
            var offset = 0;
            while (offset < length)
            {
                var bytesRead = await stream.ReadAsync(buffer, offset, length - offset, token);
                if (bytesRead == 0)
                {
                    return false;
                }

                offset += bytesRead;
            }

            return true;
        }

        private static int ReadPayloadLength(byte[] header)
        {
            return (header[PayloadLengthOffset] << 24)
                   | (header[PayloadLengthOffset + 1] << 16)
                   | (header[PayloadLengthOffset + 2] << 8)
                   | header[PayloadLengthOffset + 3];
        }

        private static void WritePayloadLength(byte[] header, int length)
        {
            header[PayloadLengthOffset] = (byte)((length >> 24) & 0xFF);
            header[PayloadLengthOffset + 1] = (byte)((length >> 16) & 0xFF);
            header[PayloadLengthOffset + 2] = (byte)((length >> 8) & 0xFF);
            header[PayloadLengthOffset + 3] = (byte)(length & 0xFF);
        }

        private static void ValidateMaxMessageSize(int maxMessageSize)
        {
            if (maxMessageSize <= 0)
            {
                throw new InvalidDataException("Max message size must be greater than 0.");
            }
        }
    }
}
