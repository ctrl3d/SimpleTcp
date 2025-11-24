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

        private readonly ConcurrentDictionary<int, StreamWriter> _clients = new();
        private int _nextClientId;

        public event Action<string> OnLog;
        public event Action<int, string> OnMessageReceived;
        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;

        public void Start(int port)
        {
            if (_listener != null) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
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
                catch (Exception)
                {
                    /* 서버 종료 시 발생 가능 */
                }
            }
        }

        private async Task HandleClientAsync(int id, TcpClient client, CancellationToken token)
        {
            OnClientConnected?.Invoke(id);
            OnLog?.Invoke($"Client connected: {id}");

            using (client)
            await using (var networkStream = client.GetStream())
            await using (var writer = new StreamWriter(networkStream))
            {
                writer.AutoFlush = true;
                _clients.TryAdd(id, writer);

                var buffer = new byte[1024];

                try
                {
                    while (!token.IsCancellationRequested && client.Connected)
                    {
                        var bytesRead = await networkStream.ReadAsync(buffer, 0, buffer.Length, token);
                        if (bytesRead == 0) break; // Disconnected

                        var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        OnMessageReceived?.Invoke(id, message);
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
                    _clients.TryRemove(id, out _);
                    OnClientDisconnected?.Invoke(id);
                    OnLog?.Invoke($"Client disconnected: {id}");
                }
            }
        }

        public async void SendToClient(int clientId, string message)
        {
            if (_clients.TryGetValue(clientId, out var writer))
            {
                try
                {
                    await writer.WriteAsync(message);
                    //await writer.WriteLineAsync(message);
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Failed to send to client {clientId}: {ex.Message}");
                }
            }
            else
            {
                OnLog?.Invoke($"Client {clientId} not found.");
            }
        }

        public async void Broadcast(string message)
        {
            var tasks = new List<Task>();

            foreach (var writer in _clients.Values)
            {
                var currentWriter = writer;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await currentWriter.WriteAsync(message);
                        //await currentWriter.WriteLineAsync(message);
                    }
                    catch
                    {
                        /* Ignore send failure */
                    }
                }));
            }

            await Task.WhenAll(tasks);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _clients.Clear();
            OnLog?.Invoke("Server stopped");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}