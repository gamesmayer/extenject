#if !NOT_UNITY3D

using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject.Internal;

namespace Zenject
{
    public class ProjectKernel : MonoKernel
    {
        [Inject]
        ZenjectSettings _settings = null;

        [Inject]
        SceneContextRegistry _contextRegistry = null;

        // One issue with relying on MonoKernel.OnDestroy to call IDisposable.Dispose
        // is that the order that OnDestroy is called in is difficult to predict
        // One good thing is that it does follow the heirarchy order (so root game objects
        // will have thier OnDestroy called before child objects)
        // However, the order that OnDestroy is called for the root game objects themselves
        // is largely random
        // Within an individual scene, this can be helped somewhat by placing all game objects
        // underneath the SceneContext and then also checking the 'ParentNewObjectsUnderRoot'
        // property to ensure any new game objects will also be parented underneath SceneContext
        // By doing this, we can be guaranteed to have any bound IDisposable's have their
        // Dispose called before any game object is destroyed in the scene
        // However, when using multiple scenes (each with their own SceneContext) the order
        // that these SceneContext game objects are destroyed is random
        // So to address that, we explicitly call GameObject.DestroyImmediate for all
        // SceneContext's in the reverse order that the scenes were loaded in below
        // (this works because OnApplicationQuit is always called before OnDestroy)
        // Note that this only works when stopping the app and not when changing scenes
        // When changing scenes, if you have multiple scenes loaded at once, you will have to
        // manually unload the scenes in the reverse order they were loaded before going to
        // the new scene, if you require a predictable destruction order.  Or you can always use
        // ZenjectSceneLoader which will do this for you
        // Unity destroys root objects in an unspecified order, so on quit every scene, and then the project, shuts
        // down first, while all their objects still exist. Nothing is destroyed here (DestroyImmediate on quit can
        // crash on Android); EnsureDeterministicDestructionOrderOnApplicationQuit only adds the ordered destruction.
        public void OnApplicationQuit()
        {
            try
            {
                ShutdownScenes();
                ShutdownHierarchy(new[] { gameObject }, this);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if (_settings.EnsureDeterministicDestructionOrderOnApplicationQuit)
            {
                DestroyEverythingInOrder();
            }
        }

        // Shuts down every loaded scene, in the reverse order they were loaded in, while it is still intact.
        // Call it before unloading or replacing scenes; their kernels' later OnDestroy does nothing.
        public void ShutdownScenes()
        {
            var errors = new List<Exception>();

            foreach (var sceneContext in SceneContextsInUnloadOrder())
            {
                LifecycleTeardown.Collect(errors, () => ShutdownScene(sceneContext.gameObject.scene));
            }

            LifecycleTeardown.ThrowIfAny(errors);
        }

        // GameObjectContexts first (children before parents, the reverse of their startup), then the SceneKernel.
        public static void ShutdownScene(Scene scene)
        {
            var sceneKernel = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<SceneKernel>(true))
                .FirstOrDefault();

            ShutdownHierarchy(scene.GetRootGameObjects(), sceneKernel);
        }

        static void ShutdownHierarchy(IEnumerable<GameObject> roots, MonoKernel last)
        {
            var errors = new List<Exception>();
            var kernels = roots
                .SelectMany(root => root.GetComponentsInChildren<MonoKernel>(true))
                .Where(kernel => !ReferenceEquals(kernel, last))
                .Reverse()
                .ToList();

            foreach (var kernel in kernels)
            {
                LifecycleTeardown.Collect(errors, kernel.Shutdown);
            }

            if (last != null)
            {
                LifecycleTeardown.Collect(errors, last.Shutdown);
            }

            LifecycleTeardown.ThrowIfAny(errors);
        }

        public void DestroyEverythingInOrder()
        {
            ForceUnloadAllScenes(true);

            // Destroy project context after all scenes
            DestroyImmediate(gameObject);
        }

        public void ForceUnloadAllScenes(bool immediate = false)
        {
            // Destroy the scene contexts from bottom to top
            // Since this is the reverse order that they were loaded in
            foreach (var sceneContext in SceneContextsInUnloadOrder())
            {
                if (immediate)
                {
                    DestroyImmediate(sceneContext.gameObject);
                }
                else
                {
                    Destroy(sceneContext.gameObject);
                }
            }
        }

        List<SceneContext> SceneContextsInUnloadOrder()
        {
            var sceneOrder = new List<Scene>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                sceneOrder.Add(SceneManager.GetSceneAt(i));
            }

            return _contextRegistry.SceneContexts.OrderByDescending(x => sceneOrder.IndexOf(x.gameObject.scene)).ToList();
        }
    }
}

#endif
