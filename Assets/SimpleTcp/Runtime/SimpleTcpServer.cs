using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace work.ctrl3d
{
    public class SimpleTcpServer : IDisposable
    {
        private TcpListener _listener;
        private CancellationTokenSource _cts;

        private readonly ConcurrentDictionary<int, ClientConnection> _clients = new();
        private int _nextClientId;

        public event Action<string> OnLog;
        public event Action<int, string> OnMessageReceived;
        public event Action<int, byte[]> OnBytesReceived;
        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;

        public int MaxMessageSize { get; set; } = SimpleTcpProtocol.DefaultMaxMessageSize;

        public void Start(string ip, int port)
        {
            if (_listener != null) return;

            _cts = new CancellationTokenSource();
            _listener = ip != "0.0.0.0"
                ? new TcpListener(IPAddress.Parse(ip), port)
                : new TcpListener(IPAddress.Any, port);

            _listener.Start();

            OnLog?.Invoke($"Server started (Port: {port})");

            Task.Run(() => AcceptClientsAsync(_cts.Token));
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    tcpClient.NoDelay = true;
                    var id = _nextClientId++;

                    _ = HandleClientAsync(id, tcpClient, token);
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        OnLog?.Invoke($"Accept client failed: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleClientAsync(int id, TcpClient client, CancellationToken token)
        {
            OnClientConnected?.Invoke(id);
            OnLog?.Invoke($"Client connected: {id}");

            ClientConnection connection = null;

            using (client)
            using (var networkStream = client.GetStream())
            {
                connection = new ClientConnection(client, networkStream);
                _clients.TryAdd(id, connection);

                try
                {
                    while (!token.IsCancellationRequested && client.Connected)
                    {
                        var frame = await SimpleTcpProtocol.ReadFrameAsync(networkStream, MaxMessageSize, token);
                        if (frame == null) break; // Disconnected

                        switch (frame.Type)
                        {
                            case SimpleTcpFrameType.String:
                                var message = Encoding.UTF8.GetString(frame.Payload, 0, frame.Payload.Length);
                                OnMessageReceived?.Invoke(id, message);
                                break;
                            case SimpleTcpFrameType.Bytes:
                                OnBytesReceived?.Invoke(id, frame.Payload);
                                break;
                            default:
                                throw new InvalidDataException($"Unknown frame type: {(byte)frame.Type}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Don't log cancellation exceptions as errors
                    if (!(ex is OperationCanceledException))
                    {
                        OnLog?.Invoke($"Client {id} error: {ex.Message}");
                    }
                }
                finally
                {
                    if (_clients.TryRemove(id, out var removedConnection))
                    {
                        removedConnection.Dispose();
                    }
                    else
                    {
                        connection?.Dispose();
                    }

                    OnClientDisconnected?.Invoke(id);
                    OnLog?.Invoke($"Client disconnected: {id}");
                }
            }
        }

        public async void SendToClient(int clientId, string message)
        {
            await SendToClientAsync(clientId, message);
        }

        public Task SendToClientAsync(int clientId, string message)
        {
            var payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return SendFrameToClientAsync(clientId, SimpleTcpFrameType.String, payload);
        }

        public async void SendBytesToClient(int clientId, byte[] bytes)
        {
            await SendBytesToClientAsync(clientId, bytes);
        }

        public Task SendBytesToClientAsync(int clientId, byte[] bytes)
        {
            return SendFrameToClientAsync(clientId, SimpleTcpFrameType.Bytes, bytes ?? Array.Empty<byte>());
        }

        public async void Broadcast(string message)
        {
            await BroadcastAsync(message);
        }

        public Task BroadcastAsync(string message)
        {
            var payload = Encoding.UTF8.GetBytes(message ?? string.Empty);
            return BroadcastFrameAsync(SimpleTcpFrameType.String, payload);
        }

        public async void BroadcastBytes(byte[] bytes)
        {
            await BroadcastBytesAsync(bytes);
        }

        public Task BroadcastBytesAsync(byte[] bytes)
        {
            return BroadcastFrameAsync(SimpleTcpFrameType.Bytes, bytes ?? Array.Empty<byte>());
        }

        private async Task SendFrameToClientAsync(int clientId, SimpleTcpFrameType frameType, byte[] payload)
        {
            if (_clients.TryGetValue(clientId, out var connection))
            {
                await SendFrameToClientAsync(clientId, connection, frameType, payload);
            }
            else
            {
                OnLog?.Invoke($"Client {clientId} not found.");
            }
        }

        private async Task SendFrameToClientAsync(
            int clientId,
            ClientConnection connection,
            SimpleTcpFrameType frameType,
            byte[] payload)
        {
            try
            {
                await SimpleTcpProtocol.WriteFrameAsync(
                    connection.Stream,
                    connection.SendLock,
                    frameType,
                    payload,
                    MaxMessageSize,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Failed to send to client {clientId}: {ex.Message}");
            }
        }

        private async Task BroadcastFrameAsync(SimpleTcpFrameType frameType, byte[] payload)
        {
            var tasks = new List<Task>();

            foreach (var pair in _clients)
            {
                tasks.Add(SendFrameToClientAsync(pair.Key, pair.Value, frameType, payload));
            }

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            _cts?.Cancel();

            var listener = _listener;
            _listener = null;

            try
            {
                listener?.Stop();
            }
            catch
            {
                // Ignore errors during cleanup
            }

            foreach (var connection in _clients.Values)
            {
                try
                {
                    connection.Client.Close();
                }
                catch
                {
                    // Ignore errors during cleanup
                }

                connection.Dispose();
            }

            _clients.Clear();
            OnLog?.Invoke("Server stopped");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
        }

        private sealed class ClientConnection : IDisposable
        {
            private int _disposed;

            public ClientConnection(TcpClient client, NetworkStream stream)
            {
                Client = client;
                Stream = stream;
            }

            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public SemaphoreSlim SendLock { get; } = new(1, 1);

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                {
                    return;
                }

                SendLock.Dispose();
            }
        }
    }
}
