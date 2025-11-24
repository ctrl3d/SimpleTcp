using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;
#if USE_ALCHEMY
using Alchemy.Inspector;
#endif

namespace work.ctrl3d
{
    public class UnitySimpleTcpClient : MonoBehaviour
    {
        [Header("Client Settings")] 
        [SerializeField] private string ipAddress = "127.0.0.1";
        [SerializeField] private int port = 7777;
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool showLogs = true;

        [Header("Reconnection Settings")] 
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private float reconnectInterval = 3f;
        [SerializeField] private int maxReconnectAttempts = -1;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnMessageReceived;
        public event Action<int> OnReconnectAttempt;
        
        [Header("Events")] 
        public UnityEvent onConnected;
        public UnityEvent onDisconnected;
        public UnityEvent<string> onMessageReceived;
        public UnityEvent<int> onReconnectAttempt;

        private SimpleTcpClient _client;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

        private bool _isReconnecting;
        private int _reconnectAttempts;
        private Coroutine _reconnectCoroutine;

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeClient();
        }

        private void InitializeClient()
        {
            _client = new SimpleTcpClient();

            // 이벤트 구독
            _client.OnLog += HandleLog;
            _client.OnConnected += HandleConnected;
            _client.OnConnectionFailed += HandleConnectionFailed;
            _client.OnMessageReceived += HandleMessageReceived;
            _client.OnDisconnected += HandleDisconnected;
        }

        private void Start()
        {
            if (connectOnStart)
            {
                Connect();
            }
        }

        private void Update()
        {
            // 메인 스레드 큐 처리
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        private void OnDestroy()
        {
            if (_client == null) return;
            _client.OnLog -= HandleLog;
            _client.OnConnected -= HandleConnected;
            _client.OnConnectionFailed -= HandleConnectionFailed;
            _client.OnMessageReceived -= HandleMessageReceived;
            _client.OnDisconnected -= HandleDisconnected;

            _client.Dispose();
            _client = null;
        }

        #endregion

        #region Public Methods

#if USE_ALCHEMY
        [Button, HorizontalGroup("Connection")]
#endif
        public void Connect()
        {
            _client?.Connect(ipAddress, port);
        }
#if USE_ALCHEMY
        [Button, HorizontalGroup("Connection")]
#endif
        public void Disconnect() => _client?.Disconnect();

#if USE_ALCHEMY
        [Button]
#endif
        public void SendString(string message) => _client?.SendString(message);

        public void SendBytes(byte[] bytes) => _client?.SendBytes(bytes);

        #endregion

        #region Reconnection Logic

        private void StartReconnection()
        {
            if (!autoReconnect || _isReconnecting) return;

            _isReconnecting = true;
            _reconnectAttempts = 0;

            if (_reconnectCoroutine != null) StopCoroutine(_reconnectCoroutine);
            _reconnectCoroutine = StartCoroutine(ReconnectionCoroutine());
        }

        private void StopReconnection()
        {
            _isReconnecting = false;
            if (_reconnectCoroutine != null)
            {
                StopCoroutine(_reconnectCoroutine);
                _reconnectCoroutine = null;
            }
        }

        private IEnumerator ReconnectionCoroutine()
        {
            while (_isReconnecting && (maxReconnectAttempts < 0 || _reconnectAttempts < maxReconnectAttempts))
            {
                yield return new WaitForSeconds(reconnectInterval);

                if (_client.IsConnected)
                {
                    StopReconnection();
                    yield break;
                }

                _reconnectAttempts++;
                _mainThreadQueue.Enqueue(() =>
                {
                    if (showLogs)
                    {
                        var attemptInfo = maxReconnectAttempts < 0
                            ? $"{_reconnectAttempts}"
                            : $"{_reconnectAttempts}/{maxReconnectAttempts}";
                        Debug.Log($"[SimpleClient] Reconnecting... Attempt {attemptInfo}");
                    }

                    OnReconnectAttempt?.Invoke(_reconnectAttempts);
                    onReconnectAttempt?.Invoke(_reconnectAttempts);
                });

                _client.Connect(ipAddress, port);
            }

            if (maxReconnectAttempts >= 0 && _reconnectAttempts >= maxReconnectAttempts)
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    if (showLogs) Debug.LogError("[SimpleClient] Max reconnection attempts reached.");
                });
                StopReconnection();
            }
        }

        #endregion
        
        #region Event Handlers (Background Thread -> Main Thread)

        private void HandleLog(string msg)
        {
            if (showLogs)
            {
                _mainThreadQueue.Enqueue(() => Debug.Log($"[SimpleClient System] {msg}"));
            }
        }

        private void HandleConnected()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                OnConnected?.Invoke();
                onConnected?.Invoke();
            });
        }

        private void HandleConnectionFailed()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                if (autoReconnect && !_isReconnecting)
                {
                    StartReconnection();
                }
            });
        }

        private void HandleMessageReceived(string message)
        {
            if (showLogs)
            {
                _mainThreadQueue.Enqueue(() => Debug.Log($"{name} : {message}"));
            }

            _mainThreadQueue.Enqueue(() =>
            {
                OnMessageReceived?.Invoke(message);
                onMessageReceived?.Invoke(message);
            });
        }

        private void HandleDisconnected()
        {
            _mainThreadQueue.Enqueue(() =>
            {
                OnDisconnected?.Invoke();
                onDisconnected?.Invoke();

                // 연결이 끊어졌을 때 자동 재접속 시작
                if (autoReconnect && !_isReconnecting)
                {
                    StartReconnection();
                }
            });
        }

        #endregion
    }
}