using UnityEngine;

namespace WarPoker.UI
{
    /// <summary>Keeps a full-stretch RectTransform inside Screen.safeArea (notches, rounded corners).</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect _applied;
        Vector2Int _screen;

        void Update()
        {
            var safe = Screen.safeArea;
            if (safe == _applied && _screen.x == Screen.width && _screen.y == Screen.height) return;
            _applied = safe;
            _screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rt = (RectTransform)transform;
            var min = safe.position;
            var max = safe.position + safe.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Uniformly scales a fixed-size design rect (e.g. 1920×1080) so it fits inside its parent.
    /// Lets the game board use fixed coordinates on any aspect ratio.
    /// </summary>
    public class FitInside : MonoBehaviour
    {
        public Vector2 DesignSize = new Vector2(1920, 1080);

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            var r = parent.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float s = Mathf.Min(r.width / DesignSize.x, r.height / DesignSize.y);
            var rt = (RectTransform)transform;
            if (rt.sizeDelta != DesignSize) rt.sizeDelta = DesignSize;
            if (Mathf.Abs(rt.localScale.x - s) > 0.0001f) rt.localScale = new Vector3(s, s, 1f);
        }
    }
}
