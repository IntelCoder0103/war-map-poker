using UnityEngine;

namespace WarPoker.Main
{
    /// <summary>A full-screen panel (Login, Lobby, Game). Screens are switched by App, not by scenes.</summary>
    public abstract class ScreenBase : MonoBehaviour
    {
        protected App App;
        public RectTransform Root { get; private set; }

        public void Init(App app)
        {
            App = app;
            Root = (RectTransform)transform;
            Build();
        }

        /// <summary>Create the UI hierarchy (called once).</summary>
        protected abstract void Build();

        public virtual void OnShow() { }
        public virtual void OnHide() { }
    }
}
