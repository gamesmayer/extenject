using GamesMayer.Lifecycle;
using NUnit.Framework;
using Assert = ModestTree.Assert;
using System;
using System.Collections.Generic;
using ModestTree.Util;

namespace Zenject.Tests.Other
{
    [TestFixture]
    public class TestEventRegistrable : ZenjectUnitTestFixture
    {
        static int GlobalCallCount;

        public class Foo : IEventRegistrable
        {
            public bool WasRegistered { get; private set; }
            public bool WasUnregistered { get; private set; }
            public int RegisterCount { get; private set; }
            public int UnregisterCount { get; private set; }

            public void RegisterEvents()
            {
                RegisterCount = ++GlobalCallCount;
                WasRegistered = true;
            }

            public void UnregisterEvents()
            {
                UnregisterCount = ++GlobalCallCount;
                WasUnregistered = true;
            }
        }

        [Test]
        public void TestBasicRegisterEvents()
        {
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var foo = Container.Resolve<Foo>();
            Assert.That(!foo.WasRegistered);

            Container.Resolve<EventRegistrableManager>().RegisterEvents();
            Assert.That(foo.WasRegistered);
        }

        public class Bar : IInitializable
        {
            public int InitializeCount { get; private set; }

            public void Initialize()
            {
                InitializeCount = ++GlobalCallCount;
            }
        }

        public class Boo : ILateInitializable
        {
            public int LateInitializeCount { get; private set; }

            public void LateInitialize()
            {
                LateInitializeCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestRegisterEventsCalledAfterInitializeAndBeforeLateInitialize()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Bar>().AsSingle();
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Boo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Container.Resolve<InitializableManager>().Initialize();
            Container.Resolve<EventRegistrableManager>().RegisterEvents();
            Container.Resolve<LateInitializableManager>().LateInitialize();

            var bar = Container.Resolve<Bar>();
            var foo = Container.Resolve<Foo>();
            var boo = Container.Resolve<Boo>();

            Assert.IsEqual(bar.InitializeCount, 1);
            Assert.IsEqual(foo.RegisterCount, 2);
            Assert.IsEqual(boo.LateInitializeCount, 3);
        }

        public class Baz : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestUnregisterEventsCalledBeforeDispose()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Baz>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Container.Resolve<EventRegistrableManager>().RegisterEvents();
            Container.Resolve<EventRegistrableManager>().UnregisterEvents();
            Container.Resolve<DisposableManager>().Dispose();

            var foo = Container.Resolve<Foo>();
            var baz = Container.Resolve<Baz>();

            Assert.IsEqual(foo.UnregisterCount, 2);
            Assert.IsEqual(baz.DisposeCount, 3);
        }

        public class Qux : IEventRegistrable
        {
            public int RegisterCount { get; private set; }
            public int UnregisterCount { get; private set; }

            public void RegisterEvents()
            {
                RegisterCount = ++GlobalCallCount;
            }

            public void UnregisterEvents()
            {
                UnregisterCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestPriorityOrdering()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Qux>().AsSingle();
            Container.BindEventRegistrableExecutionOrder<Foo>(1);
            Container.BindEventRegistrableExecutionOrder<Qux>(-1);
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Container.Resolve<EventRegistrableManager>().RegisterEvents();

            var foo = Container.Resolve<Foo>();
            var qux = Container.Resolve<Qux>();

            // Qux has lower priority (-1) so it goes first
            Assert.IsEqual(qux.RegisterCount, 1);
            Assert.IsEqual(foo.RegisterCount, 2);
        }

        [Test]
        public void TestRegisterEventsCannotBeCalledTwice()
        {
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var manager = Container.Resolve<EventRegistrableManager>();
            manager.RegisterEvents();

            Assert.Throws(() => manager.RegisterEvents());
        }

        [Test]
        public void TestUnregisterEventsCalledInReverseOrder()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Qux>().AsSingle();
            Container.BindEventRegistrableExecutionOrder<Foo>(1);
            Container.BindEventRegistrableExecutionOrder<Qux>(-1);
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var manager = Container.Resolve<EventRegistrableManager>();
            manager.RegisterEvents();

            GlobalCallCount = 0;
            manager.UnregisterEvents();

            var foo = Container.Resolve<Foo>();
            var qux = Container.Resolve<Qux>();

            // Unregister is in reverse: Foo (priority 1) goes first, then Qux (priority -1)
            Assert.IsEqual(foo.UnregisterCount, 1);
            Assert.IsEqual(qux.UnregisterCount, 2);
        }
        public class PartialRoot : IEventRegistrable
        {
            public bool FailRegistration;
            public bool FailUnregistration;
            public int Registrations;
            public int Unregistrations;

            public void RegisterEvents()
            {
                Registrations++;
                if (FailRegistration) throw new InvalidOperationException("register");
            }

            public void UnregisterEvents()
            {
                Unregistrations++;
                if (FailUnregistration) throw new InvalidOperationException("unregister");
            }
        }

        [Test]
        public void PartialStartupCleansOnlyAttemptedRootsAndShutdownIsIdempotent()
        {
            var first = new PartialRoot();
            var failed = new PartialRoot { FailRegistration = true };
            var untouched = new PartialRoot();
            var manager = new EventRegistrableManager(
                new List<IEventRegistrable> { first, failed, untouched }, new List<ValuePair<Type, int>>());
            Assert.Throws(() => manager.RegisterEvents());
            manager.UnregisterEvents();
            manager.UnregisterEvents();
            Assert.IsEqual(first.Unregistrations, 1);
            Assert.IsEqual(failed.Unregistrations, 1);
            Assert.IsEqual(untouched.Unregistrations, 0);
        }

        [Test]
        public void ShutdownContinuesWhenOneRootFails()
        {
            var first = new PartialRoot();
            var failed = new PartialRoot { FailUnregistration = true };
            var manager = new EventRegistrableManager(
                new List<IEventRegistrable> { first, failed }, new List<ValuePair<Type, int>>());
            manager.RegisterEvents();
            NUnit.Framework.Assert.Throws<AggregateException>(() => manager.UnregisterEvents());
            Assert.IsEqual(first.Unregistrations, 1);
            Assert.IsEqual(failed.Unregistrations, 1);
        }

        public class KernelProbe : IInitializable, IEventRegistrable, ITickable, IDeinitializable, IDisposable
        {
            public bool FailInitialize;
            public bool FailRegister;
            public int Ticks;
            public int Unregistrations;
            public int Disposals;
            public int Deinitializations;

            public void Initialize() { if (FailInitialize) throw new InvalidOperationException("initialize"); }
            public void RegisterEvents() { if (FailRegister) throw new InvalidOperationException("register"); }
            public void UnregisterEvents() { Unregistrations++; }
            public void Tick() { Ticks++; }
            public void Dispose() { Disposals++; }
            public void Deinitialize() { Deinitializations++; }
        }

        public class TestKernel : MonoKernel { }

        [TestCase(true)]
        [TestCase(false)]
        public void FailedKernelStartupStopsTicksAndCleansExactlyOnce(bool failInitialize)
        {
            var probe = new KernelProbe { FailInitialize = failInitialize, FailRegister = !failInitialize };
            Container.BindInterfacesAndSelfTo<KernelProbe>().FromInstance(probe);
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();
            var kernel = Container.InstantiateComponentOnNewGameObject<TestKernel>();
            try
            {
                Assert.Throws(() => kernel.Initialize());
                kernel.Update();
                kernel.FixedUpdate();
                kernel.LateUpdate();
                kernel.OnDestroy();
                Assert.IsEqual(probe.Ticks, 0);
                Assert.IsEqual(probe.Unregistrations, failInitialize ? 0 : 1);
                Assert.IsEqual(probe.Disposals, 1);
                Assert.IsEqual(probe.Deinitializations, 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(kernel.gameObject);
            }
        }

    }
}
