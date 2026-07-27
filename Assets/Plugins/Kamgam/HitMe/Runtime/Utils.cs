using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kamgam.HitMe
{
    public static class Utils
    {
        /// <summary>
        /// Thanks Unity for breaking such a fundamental API, you MORONS!!!
        /// </summary>
        /// <param name="rb"></param>
        /// <returns></returns>
        public static Vector3 GetVelocity(this Rigidbody rb)
        {
#if UNITY_2023_3_0 || UNITY_2023_3_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif 
        }

        /// <summary>
        /// Thanks Unity for breaking such a fundamental API, you MORONS!!!
        /// </summary>
        /// <param name="rb"></param>
        /// <returns></returns>
        public static void SetVelocity(this Rigidbody rb, Vector3 newVelocity)
        {
#if UNITY_2023_3_0 || UNITY_2023_3_OR_NEWER
            rb.linearVelocity = newVelocity;
#else
            rb.velocity = newVelocity;
#endif 
        }

        public static void SmartDestroy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject.DestroyImmediate(obj);
            }
            else
#endif
            {
                GameObject.Destroy(obj);
            }
        }

        public static void SmartDontDestroyOnLoad(GameObject go)
        {
            if (go == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                GameObject.DontDestroyOnLoad(go);
            }
#else
            GameObject.DontDestroyOnLoad(go);
#endif
        }

        private static List<GameObject> _tmpSceneObjects = new List<GameObject>();

        public static List<T> FindRootObjectsByType<T>(bool includeInactive) where T : Component
        {
            var results = new List<T>();
            FindRootObjectsByType(includeInactive, results);
            return results;
        }

        /// <summary>
        /// A simple replacement for GameObject.FindObjectsOfType<T>. It checks the ROOT objects in ALL opened or loaded scenes.
        /// </summary>
        /// <param name="includeInactive"></param>
        /// <param name="results">A list that will be cleared and then filled with the results.</param>
        /// <returns></returns>
        public static void FindRootObjectsByType<T>(bool includeInactive, IList<T> results) where T : Component
        {
            if (results == null)
            {
                results = new List<T>();
            }
            else
            {
                results.Clear();
            }

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid())
                    continue;

                scene.GetRootGameObjects(_tmpSceneObjects);

                foreach (var obj in _tmpSceneObjects)
                {
                    var comp = obj.GetComponent<T>();
                    if (comp == null)
                        continue;

                    if (!includeInactive && !comp.gameObject.activeInHierarchy)
                        continue;

                    results.Add(comp);
                }
            }
        }

        /// <summary>
        /// A simple replacement for GameObject.FindObjectsOfType<T>. It checks the ROOT objects in ALL opened or loaded scenes.
        /// </summary>
        /// <param name="includeInactive"></param>
        /// <returns></returns>
        public static T FindRootObjectByType<T>(bool includeInactive) where T : Component
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid())
                    continue;

                if (!scene.isLoaded)
                    continue;

                scene.GetRootGameObjects(_tmpSceneObjects);

                foreach (var obj in _tmpSceneObjects)
                {
                    var comp = obj.GetComponent<T>();
                    if (comp == null)
                        continue;

                    if (!includeInactive && !comp.gameObject.activeInHierarchy)
                        continue;

                    return comp;
                }
            }

            return default;
        }

        public static T[] FindObjectsOfTypeFast<T>(bool includeInactive = false) where T : Object
        {
            // Thanks Unity for this mess of an API.
#if UNITY_2023_1_OR_NEWER
#if UNITY_6000_5_OR_NEWER
            return GameObject.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return GameObject.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
#else
            return GameObject.FindObjectsOfType<T>(includeInactive);
#endif
        }

        public static T FindObjectOfTypeFast<T>(bool includeInactive = false) where T : Object
        {
            // Thanks Unity for this mess of an API.
#if UNITY_2023_1_OR_NEWER
#if UNITY_6000_5_OR_NEWER
            return GameObject.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
            return GameObject.FindFirstObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#endif
#else
            return GameObject.FindObjectOfType<T>(includeInactive);
#endif
        }
    }
}