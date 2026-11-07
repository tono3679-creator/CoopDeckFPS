using System.Collections.Generic;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Pools effect prefabs so shooting never instantiates at runtime after warm-up.
    /// Gameplay code only says "play this prefab here"; how it looks lives in the prefab.
    /// </summary>
    public static class FxPool
    {
        static readonly Dictionary<GameObject, Stack<PooledFx>> Pools = new Dictionary<GameObject, Stack<PooledFx>>();
        static Transform root;

        public static PooledFx Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, int layer = -1)
        {
            if (prefab == null)
            {
                return null;
            }

            PooledFx fx = null;
            if (Pools.TryGetValue(prefab, out Stack<PooledFx> stack))
            {
                while (stack.Count > 0 && fx == null)
                {
                    fx = stack.Pop();
                }
            }

            if (fx == null)
            {
                GameObject instance = Object.Instantiate(prefab, Root);
                instance.name = prefab.name;
                fx = instance.GetComponent<PooledFx>();
                if (fx == null)
                {
                    fx = instance.AddComponent<PooledFx>();
                }

                fx.SourcePrefab = prefab;
            }

            Transform t = fx.transform;
            t.SetParent(parent != null ? parent : Root, false);
            t.SetPositionAndRotation(position, rotation);
            if (layer >= 0)
            {
                SetLayerRecursively(fx.gameObject, layer);
            }

            fx.gameObject.SetActive(true);
            fx.Play();
            return fx;
        }

        /// <summary>Instantiates a few copies up front so the first shot does not hitch.</summary>
        public static void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null)
            {
                return;
            }

            var warmed = new List<PooledFx>(count);
            for (int i = 0; i < count; i++)
            {
                PooledFx fx = Spawn(prefab, Vector3.zero, Quaternion.identity);
                if (fx != null)
                {
                    warmed.Add(fx);
                }
            }

            foreach (PooledFx fx in warmed)
            {
                Release(fx);
            }
        }

        internal static void Release(PooledFx fx)
        {
            if (fx == null)
            {
                return;
            }

            fx.gameObject.SetActive(false);
            if (fx.SourcePrefab == null)
            {
                Object.Destroy(fx.gameObject);
                return;
            }

            fx.transform.SetParent(Root, false);
            if (!Pools.TryGetValue(fx.SourcePrefab, out Stack<PooledFx> stack))
            {
                stack = new Stack<PooledFx>();
                Pools.Add(fx.SourcePrefab, stack);
            }

            stack.Push(fx);
        }

        static Transform Root
        {
            get
            {
                if (root == null)
                {
                    var go = new GameObject("[FxPool]");
                    Object.DontDestroyOnLoad(go);
                    root = go.transform;
                }

                return root;
            }
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Pools.Clear();
            root = null;
        }
    }
}
