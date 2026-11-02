using GamesMayer.Lifecycle;
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Zenject.Tests.Misc
{
    // Unity destroys root objects in an unspecified order, so a scene must shut down before it unloads.
    public class TestSceneShutdown : ZenjectIntegrationTestFixture
    {
        static readonly List<string> Log = new List<string>();

        public class ViewRoot : IDeinitializable
        {
            readonly GameObject _view;

            public ViewRoot([Inject(Id = "View")] GameObject view)
            {
                _view = view;
            }

            public void Deinitialize()
            {
                Log.Add(_view != null ? "view alive" : "view destroyed");
                _view.SetActive(false);
            }
        }

        public class SceneRoot : IDeinitializable
        {
            public void Deinitialize()
            {
                Log.Add("scene");
            }
        }

        public class ChildRoot : IDeinitializable
        {
            public void Deinitialize()
            {
                Log.Add("child");
            }
        }

        public class Spawned : IPoolable<IMemoryPool>
        {
            public static int Alive;

            IMemoryPool _pool;

            public void OnSpawned(IMemoryPool pool)
            {
                _pool = pool;
                Alive++;
            }

            public void OnDespawned()
            {
                Alive--;
            }

            public void Despawn()
            {
                _pool.Despawn(this);
            }

            public class Factory : PlaceholderFactory<Spawned> { }
        }

        public class Spawner : IInitializable, IDeinitializable
        {
            readonly Spawned.Factory _factory;
            readonly List<Spawned> _spawned = new List<Spawned>();

            public Spawner(Spawned.Factory factory)
            {
                _factory = factory;
            }

            public void Initialize()
            {
                for (int i = 0; i < 3; i++)
                {
                    _spawned.Add(_factory.Create());
                }
            }

            public void Deinitialize()
            {
                _spawned.ForEach(x => x.Despawn());
                _spawned.Clear();
            }
        }

        [SetUp]
        public void ClearLog()
        {
            Log.Clear();
        }

        [UnityTest]
        public IEnumerator ShutdownSceneDeinitializesWhileItsObjectsExist()
        {
            // A root object of its own, not under the SceneContext.
            var view = new GameObject("View");

            PreInstall();
            Container.BindInstance(view).WithId("View");
            Container.BindInterfacesTo<ViewRoot>().AsSingle();
            PostInstall();

            yield return null;

            ProjectKernel.ShutdownScene(SceneManager.GetActiveScene());
            ProjectKernel.ShutdownScene(SceneManager.GetActiveScene());

            CollectionAssert.AreEqual(new[] { "view alive" }, Log);
            Assert.IsFalse(view.activeSelf);

            Object.Destroy(view);
        }

        [UnityTest]
        public IEnumerator GameObjectContextsShutDownBeforeTheirScene()
        {
            PreInstall();
            Container.BindInterfacesTo<SceneRoot>().AsSingle();
            Container.Bind<ChildRoot>()
                .FromSubContainerResolve()
                .ByNewGameObjectMethod(sub => sub.BindInterfacesAndSelfTo<ChildRoot>().AsSingle())
                .AsSingle()
                .NonLazy();
            PostInstall();

            yield return null;

            ProjectKernel.ShutdownScene(SceneManager.GetActiveScene());

            CollectionAssert.AreEqual(new[] { "child", "scene" }, Log);
        }

        [UnityTest]
        public IEnumerator AGameObjectContextDestroyedMidSceneReleasesOnce()
        {
            PreInstall();
            Container.Bind<ChildRoot>()
                .FromSubContainerResolve()
                .ByNewGameObjectMethod(sub => sub.BindInterfacesAndSelfTo<ChildRoot>().AsSingle())
                .AsSingle()
                .NonLazy();
            PostInstall();

            yield return null;

            var context = Object.FindAnyObjectByType<GameObjectContext>();
            Object.Destroy(context.gameObject);

            yield return null;

            ProjectKernel.ShutdownScene(SceneManager.GetActiveScene());

            CollectionAssert.AreEqual(new[] { "child" }, Log);
        }

        // Contexts used to stay subscribed to an editor event after being destroyed, keeping their scene alive.
        [UnityTest]
        public IEnumerator ADestroyedContextIsCollected()
        {
            PreInstall();
            PostInstall();

            yield return null;

            var context = CreateAndDestroyContext(Container);

            yield return null;
            yield return null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(context.IsAlive);
        }

        static WeakReference CreateAndDestroyContext(DiContainer container)
        {
            var gameObject = new GameObject("Context");
            gameObject.SetActive(false);
            var context = gameObject.AddComponent<GameObjectContext>();
            container.InjectGameObject(gameObject);
            gameObject.SetActive(true);
            var reference = new WeakReference(context);
            Object.Destroy(gameObject);

            return reference;
        }

        [UnityTest]
        public IEnumerator PooledSpawnsAreReleasedWhenTheSceneShutsDown()
        {
            Spawned.Alive = 0;

            PreInstall();
            Container.BindFactory<Spawned, Spawned.Factory>().FromPoolableMemoryPool();
            Container.BindInterfacesTo<Spawner>().AsSingle();
            PostInstall();

            yield return null;

            Assert.AreEqual(3, Spawned.Alive);

            ProjectKernel.ShutdownScene(SceneManager.GetActiveScene());

            Assert.AreEqual(0, Spawned.Alive);
        }
    }
}
