using UnityEngine;

namespace WarPoker.Main
{
    /// <summary>
    /// Scene entry point: put this on a GameObject in Main.unity (WarPoker/Setup Project does it).
    /// If it's missing, the RuntimeInitializeOnLoadMethod fallback still starts the app.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        void Awake()
        {
            App.Ensure();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Fallback()
        {
            if (App.Instance == null) App.Ensure();
        }
    }
}
