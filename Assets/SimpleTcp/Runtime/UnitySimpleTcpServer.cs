using System;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;
#if USE_ALCHEMY
using Alchemy.Inspector;
#endif

namespace work.ctrl3d
{
    public class UnitySimpleTcpServer : MonoBehaviour
    {
        private const int MaxLoggedMessageLength = 512;

        [field: Header("Server Settings")]
        [field: SerializeField]
        public string IpAddress { get; set; } = "127.0.0.1";
        [field: SerializeField]
        public int Port { get; set; } = 7777;

        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool showLogs = true;
        [SerializeField] private SimpleTcpMessageSize maxMessageSize = SimpleTcpMessageSize.MiB32;

        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;
        public event Action<int, string> OnMessageReceived;
        public event Action<int, byte[]> OnBytesReceived;

        [Header("Events")] public UnityEvent<int> onClientConnected;
        public UnityEvent<int> onClientDisconnected;
        public UnityEvent<int, string> onMessageReceived;
        public UnityEvent<int, byte[]> onBytesReceived;

        private SimpleTcpServer _server;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        #region Unity Lifecycle

        private void Awake()
        {
            _server = new SimpleTcpServer { MaxMessageSize = (int)maxMessageSize };

            _server.OnLog += HandleLog;
            _server.OnClientConnected += HandleClientConnected;
            _server.OnClientDisconnected += HandleClientDisconnected;
            _server.OnMessageReceived += HandleMessageReceived;
            _server.OnBytesReceived += HandleBytesReceived;
        }

        private void Start()
        {
            if (autoStart)
            {
                StartServer();
            }
        }

        private void Update()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_server == null) return;
            _server.OnLog -= HandleLog;
            _server.OnClientConnected -= HandleClientConnected;
            _server.OnClientDisconnected -= HandleClientDisconnected;
            _server.OnMessageReceived -= HandleMessageReceived;
            _server.OnBytesReceived -= HandleBytesReceived;

            _server.Dispose();
            _server = null;
        }

        #endregion

        #region Public Methods

#if USE_ALCHEMY
        [Button, HorizontalGroup("Control")]
#endif
        public void StartServer()
        {
            if (_server != null)
            {
                _server.MaxMessageSize = (int)maxMessageSize;
            }

            _server?.Start(IpAddress, Port);
        }

#if USE_ALCHEMY
        [Button, HorizontalGroup("Control")]
#endif
        public void StopServer() => _server?.Stop();

#if USE_ALCHEMY
        [Button]
#endif
        public void SendToClient(int clientId, string message) => _server?.SendToClient(clientId, message);

        public void SendBytesToClient(int clientId, byte[] bytes) => _server?.SendBytesToClient(clientId, bytes);

#if USE_ALCHEMY
        [Button]
#endif
        public void Broadcast(string message) => _server?.Broadcast(message);

        public void BroadcastBytes(byte[] bytes) => _server?.BroadcastBytes(bytes);

        #endregion

        #region Event Handlers (Background Thread -> Main Thread)

        private void HandleLog(string msg)
        {
            if (showLogs)
            {
                _mainThreadQueue.Enqueue(() => Debug.Log($"[SimpleServer System] {msg}"));
            }
        }

        private void HandleClientConnected(int id)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                OnClientConnected?.Invoke(id);
                onClientConnected?.Invoke(id);
            });
        }

        private void HandleClientDisconnected(int id)
        {
            _mainThreadQueue.Enqueue(() =>
            {
                OnClientDisconnected?.Invoke(id);
                onClientDisconnected?.Invoke(id);
            });
        }

        private void HandleMessageReceived(int id, string msg)
        {
            if (showLogs)
            {
                _mainThreadQueue.Enqueue(() => Debug.Log($"{name} From({id}): {FormatMessageForLog(msg)}"));
            }

            _mainThreadQueue.Enqueue(() =>
            {
                OnMessageReceived?.Invoke(id, msg);
                onMessageReceived?.Invoke(id, msg);
            });
        }

        private void HandleBytesReceived(int id, byte[] bytes)
        {
            if (showLogs)
            {
                var length = bytes?.Length ?? 0;
                _mainThreadQueue.Enqueue(() => Debug.Log($"{name} From({id}): {length} bytes"));
            }

            _mainThreadQueue.Enqueue(() =>
            {
                OnBytesReceived?.Invoke(id, bytes);
                onBytesReceived?.Invoke(id, bytes);
            });
        }

        #endregion

        private static string FormatMessageForLog(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length <= MaxLoggedMessageLength)
            {
                return message;
            }

            return $"{message.Substring(0, MaxLoggedMessageLength)}... ({message.Length} chars)";
        }
    }
}
