using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SocketIOClient;
using UnityEngine;

namespace WarPoker.Net
{
    /// <summary>
    /// Socket.IO connection (SocketIOUnity). Every callback from the library arrives on a
    /// background thread; this class posts all of them through <see cref="MainThread"/> so that
    /// events and acks reach game code on the main thread, in server order.
    /// </summary>
    public class SocketClient
    {
        // Main-thread events.
        public event Action Connected;
        public event Action<string> Disconnected;          // reason
        public event Action<string> ConnectError;          // e.g. "UNAUTHORIZED"
        public event Action<string, JToken> EventReceived; // event name, first argument

        public bool IsConnected { get; private set; }
        public bool IsStarted { get { return _socket != null; } }

        SocketIOUnity _socket;
        int _generation;            // bumps on each (re)create; stale callbacks are dropped
        string _url, _token;
        float _lastAliveTime;       // last ping / message from the server (main-thread time)

        // Pending acks, so a request never hangs forever (e.g. connection died mid-request).
        class Pending
        {
            public Action<JObject> Callback;
            public float Deadline;
        }
        readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        int _nextRequestId;

        const float AckTimeoutSeconds = 10f;
        // Server pings every ~25 s (Socket.IO default). With no sign of life for this long the
        // connection is treated as dead (half-open TCP on mobile) and recreated.
        const float DeadConnectionSeconds = 60f;

        static readonly string[] ServerEvents =
        {
            "lobby:rooms", "table:state", "table:event", "user:balance", "table:removed", "session:replaced"
        };

        /// <summary>Connects with the JWT in the auth payload, WebSocket transport, auto-reconnect.</summary>
        public void Connect(string url, string token)
        {
            _url = url;
            _token = token;
            Create();
        }

        void Create()
        {
            DisposeSocket();
            int gen = ++_generation;

            var options = new SocketIOOptions
            {
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
                Reconnection = true,
                ReconnectionDelay = 1000,
                ReconnectionDelayMax = 5000,
                ConnectionTimeout = TimeSpan.FromSeconds(15),
                Auth = new Dictionary<string, string> { { "token", _token } }
            };

            _socket = new SocketIOUnity(new Uri(_url), options);
            // Must be set before connecting: the auth payload is serialized with it.
            _socket.JsonSerializer = new SocketIOClient.Newtonsoft.Json.NewtonsoftJsonSerializer();

            _socket.OnConnected += (sender, e) => MainThread.Post(() =>
            {
                if (gen != _generation) return;
                IsConnected = true;
                _lastAliveTime = Time.realtimeSinceStartup;
                Debug.Log("[socket] connected");
                Connected?.Invoke();
            });
            _socket.OnDisconnected += (sender, reason) => MainThread.Post(() =>
            {
                if (gen != _generation) return;
                IsConnected = false;
                Debug.Log("[socket] disconnected: " + reason);
                FailAllPending("NOT_CONNECTED");
                Disconnected?.Invoke(reason);
            });
            _socket.OnError += (sender, message) => MainThread.Post(() =>
            {
                if (gen != _generation) return;
                Debug.LogWarning("[socket] error: " + message);
                ConnectError?.Invoke(message);
            });
            _socket.OnPing += (sender, e) => MainThread.Post(() =>
            {
                if (gen == _generation) _lastAliveTime = Time.realtimeSinceStartup;
            });

            foreach (var name in ServerEvents)
            {
                string eventName = name;
                _socket.On(eventName, response =>
                {
                    // Raw JSON of the argument list, parsed off-thread; handlers run on main thread.
                    JToken arg = FirstArg(response);
                    MainThread.Post(() =>
                    {
                        if (gen != _generation) return;
                        _lastAliveTime = Time.realtimeSinceStartup;
                        EventReceived?.Invoke(eventName, arg);
                    });
                });
            }

            _lastAliveTime = Time.realtimeSinceStartup;
            _socket.Connect();
        }

        /// <summary>Emits an event with a JSON payload; the ack callback runs on the main thread.</summary>
        public void Request(string eventName, JObject payload, Action<JObject> onAck)
        {
            if (_socket == null || !IsConnected)
            {
                var fail = new JObject { ["ok"] = false, ["error"] = "NOT_CONNECTED" };
                if (onAck != null) MainThread.Post(() => onAck(fail));
                return;
            }

            int id = ++_nextRequestId;
            _pending[id] = new Pending { Callback = onAck, Deadline = Time.realtimeSinceStartup + AckTimeoutSeconds };
            int gen = _generation;

            Action<SocketIOResponse> ack = response =>
            {
                JToken arg = FirstArg(response);
                MainThread.Post(() =>
                {
                    if (gen != _generation) return;
                    var obj = arg as JObject ?? new JObject { ["ok"] = false, ["error"] = "BAD_RESPONSE" };
                    Resolve(id, obj);
                });
            };
            _socket.Emit(eventName, ack, payload ?? new JObject());
        }

        /// <summary>Call every frame (from App.Update): ack timeouts and dead-connection watchdog.</summary>
        public void Tick()
        {
            if (_pending.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                List<int> expired = null;
                foreach (var kv in _pending)
                    if (kv.Value.Deadline < now) (expired ?? (expired = new List<int>())).Add(kv.Key);
                if (expired != null)
                    foreach (var id in expired)
                        Resolve(id, new JObject { ["ok"] = false, ["error"] = "TIMEOUT" });
            }

            if (_socket != null && IsConnected && Time.realtimeSinceStartup - _lastAliveTime > DeadConnectionSeconds)
            {
                Debug.LogWarning("[socket] no ping from server for a while; reconnecting");
                ForceReconnect();
            }
        }

        /// <summary>Drops the current socket and opens a fresh one (used by the watchdog and on app resume).</summary>
        public void ForceReconnect()
        {
            if (_socket == null) return;
            bool was = IsConnected;
            IsConnected = false;
            FailAllPending("NOT_CONNECTED");
            if (was) Disconnected?.Invoke("watchdog");
            Create();
        }

        /// <summary>Seconds since the server last showed signs of life.</summary>
        public float SecondsSinceAlive { get { return Time.realtimeSinceStartup - _lastAliveTime; } }

        /// <summary>Closes the connection for good (logout, session replaced).</summary>
        public void Close()
        {
            _generation++;
            IsConnected = false;
            FailAllPending("NOT_CONNECTED");
            DisposeSocket();
        }

        void DisposeSocket()
        {
            if (_socket == null) return;
            var old = _socket;
            _socket = null;
            try
            {
                old.Disconnect();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[socket] disconnect failed: " + e.Message);
            }
            // Dispose a bit later so the disconnect packet has a chance to go out.
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(t =>
            {
                try { old.Dispose(); } catch (Exception) { }
            });
        }

        void Resolve(int id, JObject result)
        {
            Pending p;
            if (!_pending.TryGetValue(id, out p)) return;
            _pending.Remove(id);
            try { p.Callback?.Invoke(result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void FailAllPending(string error)
        {
            if (_pending.Count == 0) return;
            var ids = new List<int>(_pending.Keys);
            foreach (var id in ids) Resolve(id, new JObject { ["ok"] = false, ["error"] = error });
        }

        /// <summary>First argument of an event/ack as a JToken (null if none or unparsable).</summary>
        static JToken FirstArg(SocketIOResponse response)
        {
            try
            {
                // ToString() gives the raw JSON argument array, e.g. [{"ok":true}].
                var array = JArray.Parse(response.ToString());
                return array.Count > 0 ? array[0] : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[socket] could not parse payload: " + e.Message);
                return null;
            }
        }
    }
}
