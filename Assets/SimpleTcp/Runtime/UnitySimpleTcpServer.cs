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
        [field: Header("Server Settings")]
        [field: SerializeField]
        
        public string IpAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 7777;

        [SerializeField] private bool autoStart = true;
        [SerializeField] private bool showLogs = true;

        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;
        public event Action<int, string> OnMessageReceived;

        [Header("Events")] public UnityEvent<int> onClientConnected;
        public UnityEvent<int> onClientDisconnected;
        public UnityEvent<int, string> onMessageReceived;

        private SimpleTcpServer _server;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        #region Unity Lifecycle

        private void Awake()
        {
            _server = new SimpleTcpServer();

            _server.OnLog += HandleLog;
            _server.OnClientConnected += HandleClientConnected;
            _server.OnClientDisconnected += HandleClientDisconnected;
            _server.OnMessageReceived += HandleMessageReceived;
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

            _server.Dispose();
            _server = null;
        }

        #endregion

        #region Public Methods

#if USE_ALCHEMY
        [Button, HorizontalGroup("Control")]
#endif
        public void StartServer() => _server?.Start(IpAddress, Port);

#if USE_ALCHEMY
        [Button, HorizontalGroup("Control")]
#endif
        public void StopServer() => _server?.Stop();

#if USE_ALCHEMY
        [Button]
#endif
        public void SendToClient(int clientId, string message) => _server?.SendToClient(clientId, message);

#if USE_ALCHEMY
        [Button]
#endif
        public void Broadcast(string message) => _server?.Broadcast(message);

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
                _mainThreadQueue.Enqueue(() => Debug.Log($"{name} From({id}): {msg}"));
            }

            _mainThreadQueue.Enqueue(() =>
            {
                OnMessageReceived?.Invoke(id, msg);
                onMessageReceived?.Invoke(id, msg);
            });
        }

        #endregion
    }
}