using GamesMayer.Lifecycle;
using GamesMayer.Lifecycle.Conformance;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.TestTools;

namespace Zenject.Tests.Other
{
    // The same scenarios the shared executor and the server adapter pass (GamesMayer.Lifecycle.Conformance).
    [TestFixture]
    public class TestLifecycleConformance
    {
        readonly List<GameObject> _objects = new List<GameObject>();

        public static IEnumerable<string> Scenarios => LifecycleConformance.Scenarios.Select(x => x.Name);

        public class ConformanceKernel : MonoKernel { }

        [TearDown]
        public void TearDown()
        {
            foreach (var gameObject in _objects)
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }

            _objects.Clear();
        }

        [TestCaseSource(nameof(Scenarios))]
        public void MonoKernelConforms(string name)
        {
            LifecycleConformance.Verify(LifecycleConformance.Scenarios.Single(x => x.Name == name), (scenario, trace) =>
            {
                var container = new DiContainer();
                container.BindInstance(trace);

                foreach (var (probe, priority) in scenario.Roots)
                {
                    container.BindInterfacesAndSelfTo(probe).AsSingle();
                    container.BindExecutionOrder(probe, priority);
                }

                return new KernelHost(CreateKernel(container));
            });
        }

        [Test]
        public void SubContainerStagesJoinTheParentBarriers()
        {
            var trace = new ConformanceTrace(new (Type, ConformanceStage)[0]);
            var container = new DiContainer();
            container.BindInstance(trace);
            container.BindInterfacesAndSelfTo<ConformanceProbeA>().AsSingle();
            container.Bind<ConformanceProbeB>()
                .FromSubContainerResolve()
                .ByMethod(sub => sub.BindInterfacesAndSelfTo<ConformanceProbeB>().AsSingle())
                .WithKernel()
                .AsSingle()
                .NonLazy();
            var host = new KernelHost(CreateKernel(container));

            host.Start();
            host.Stop();

            // Kernel is bound after A, so with equal priorities it runs after A and tears down before it.
            CollectionAssert.AreEqual(new[]
            {
                "A.Initialize", "B.Initialize",
                "A.RegisterEvents", "B.RegisterEvents",
                "A.LateInitialize", "B.LateInitialize",
                "B.UnregisterEvents", "A.UnregisterEvents",
                "B.Deinitialize", "A.Deinitialize"
            }, trace.Entries);
            Assert.AreEqual(0, trace.ListenerCount);
            Assert.AreEqual(0, trace.OpenResourceCount);
        }

        public class ContainerResource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }

        [Test]
        public void AFailingDeinitializeStillReleasesTheOtherRootsAndContainerResources()
        {
            var trace = new ConformanceTrace(new[] { (typeof(ConformanceProbeA), ConformanceStage.Deinitialize) });
            var resource = new ContainerResource();
            var container = new DiContainer();
            container.BindInstance(trace);
            container.BindInterfacesAndSelfTo<ConformanceProbeA>().AsSingle();
            container.BindInterfacesAndSelfTo<ConformanceProbeB>().AsSingle();
            container.Bind<IDisposable>().FromInstance(resource);
            var kernel = CreateKernel(container);

            kernel.Initialize();
            Assert.Throws<AggregateException>(kernel.Shutdown);
            kernel.Shutdown();

            Assert.AreEqual(0, trace.OpenResourceCount);
            Assert.AreEqual(0, trace.ListenerCount);
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void AContextShutDownBeforeStartingOnlyReleasesContainerResources()
        {
            var trace = new ConformanceTrace(new (Type, ConformanceStage)[0]);
            var resource = new ContainerResource();
            var container = new DiContainer();
            container.BindInstance(trace);
            container.BindInterfacesAndSelfTo<ConformanceProbeA>().AsSingle();
            container.Bind<IDisposable>().FromInstance(resource);
            var kernel = CreateKernel(container);

            kernel.Shutdown();
            kernel.Update();

            CollectionAssert.IsEmpty(trace.Entries);
            Assert.AreEqual(1, resource.Disposals);
        }

        // Collected a frame later: Mono's conservative stack scan can keep the last frame's locals alive.
        [UnityTest]
        public IEnumerator ShutDownContainersReleaseTheirRoots()
        {
            var trace = new ConformanceTrace(new (Type, ConformanceStage)[0]);
            var root = RunAndShutDown(trace);

            yield return null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.IsFalse(root.IsAlive);
            Assert.AreEqual(0, trace.ListenerCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        WeakReference RunAndShutDown(ConformanceTrace trace)
        {
            var container = new DiContainer();
            container.BindInstance(trace);
            container.BindInterfacesAndSelfTo<ConformanceProbeA>().AsSingle();
            var kernel = CreateKernel(container);
            var root = new WeakReference(container.Resolve<ConformanceProbeA>());

            var gameObject = kernel.gameObject;

            kernel.Initialize();
            kernel.Shutdown();
            _objects.Remove(gameObject);
            UnityEngine.Object.DestroyImmediate(gameObject);

            return root;
        }

        MonoKernel CreateKernel(DiContainer container)
        {
            ZenjectManagersInstaller.Install(container);
            container.ResolveRoots();
            var kernel = container.InstantiateComponentOnNewGameObject<ConformanceKernel>();
            _objects.Add(kernel.gameObject);

            return kernel;
        }

        class KernelHost : ILifecycleConformanceHost
        {
            readonly MonoKernel _kernel;

            public KernelHost(MonoKernel kernel)
            {
                _kernel = kernel;
            }

            public void Start()
            {
                _kernel.Initialize();
            }

            // Unity runs FixedUpdate before Update within a frame.
            public void Step()
            {
                _kernel.FixedUpdate();
                _kernel.Update();
                _kernel.LateUpdate();
            }

            public void Stop()
            {
                _kernel.Shutdown();
            }
        }
    }
}
