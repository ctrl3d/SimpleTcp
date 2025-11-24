using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace work.ctrl3d
{
    public class SimpleTcpClient : IDisposable
    {
        private TcpClient _client;
        private StreamWriter _writer;
        private CancellationTokenSource _cts;

        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnConnectionFailed;
        public event Action<string> OnMessageReceived;
        public event Action OnDisconnected;

        public bool IsConnected => _client is { Connected: true };

        public void Connect(string ip, int port)
        {
            if (IsConnected) return;

            try
            {
                _client = new TcpClient();
                _client.NoDelay = true;
                _client.Connect(ip, port);

                var stream = _client.GetStream();
                _writer = new StreamWriter(stream) { AutoFlush = true };
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
            var buffer = new byte[1024];

            try
            {
                while (!token.IsCancellationRequested && IsConnected)
                {
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0) break; // Server closed connection

                    var message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    OnMessageReceived?.Invoke(message);
                }
            }
            catch (Exception)
            {
                // Connection lost or cancelled
            }
            finally
            {
                Disconnect();
            }
        }

        public async void SendString(string message)
        {
            if (!IsConnected) return;

            try
            {
                await _writer.WriteAsync(message);
                //await _writer.WriteLineAsync(message);
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"Send failed: {e.Message}");
                Disconnect();
            }
        }

        public async void SendBytes(byte[] bytes)
        {
            if (!IsConnected) return;

            try
            {
                await _client.GetStream().WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                OnLog?.Invoke($"Send failed: {e.Message}");
                Disconnect();
            }
        }

        public void Disconnect()
        {
            if (_client == null) return;

            _cts?.Cancel();

            try
            {
                _writer?.Dispose();
                _client?.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }

            _client = null;
            _writer = null;

            OnDisconnected?.Invoke();
            OnLog?.Invoke("Disconnected");
        }

        public void Dispose()
        {
            Disconnect();
            _cts?.Dispose();
        }
    }
}