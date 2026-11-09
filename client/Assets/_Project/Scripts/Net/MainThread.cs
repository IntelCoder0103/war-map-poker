using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace WarPoker.Net
{
    /// <summary>
    /// Runs actions posted from background threads (Socket.IO callbacks, acks) on the Unity
    /// main thread, in the order they were posted. One queue for everything keeps socket
    /// events and acks in the order the server sent them.
    /// </summary>
    public class MainThread : MonoBehaviour
    {
        static MainThread _instance;
        static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("[MainThread]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThread>();
        }

        /// <summary>Thread-safe. The action runs on the next Update.</summary>
        public static void Post(Action action)
        {
            if (action != null) Queue.Enqueue(action);
        }

        void Update()
        {
            // Bound the work per frame so a burst can't freeze a frame for long.
            int budget = 200;
            Action action;
            while (budget-- > 0 && Queue.TryDequeue(out action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
