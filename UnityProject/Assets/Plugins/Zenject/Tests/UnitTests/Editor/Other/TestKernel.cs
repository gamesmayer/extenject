using GamesMayer.Lifecycle;
using System;
using NUnit.Framework;
using Assert = ModestTree.Assert;

namespace Zenject.Tests.Other
{
    [TestFixture]
    public class TestKernel : ZenjectUnitTestFixture
    {
        static int GlobalCallCount;

        public class Foo : IEventRegistrable
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

        public class Baz : IDisposable
        {
            public int DisposeCount { get; private set; }

            public void Dispose()
            {
                DisposeCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestRegisterEventsCalledOnInitialize()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.Bind<Kernel>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var foo = Container.Resolve<Foo>();
            Assert.IsEqual(foo.RegisterCount, 0);

            Start(Container.Resolve<Kernel>());
            Assert.IsEqual(foo.RegisterCount, 1);
        }

        [Test]
        public void TestUnregisterEventsCalledOnDispose()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.Bind<Kernel>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var kernel = Container.Resolve<Kernel>();
            Start(kernel);

            GlobalCallCount = 0;

            var foo = Container.Resolve<Foo>();
            Assert.IsEqual(foo.UnregisterCount, 0);

            Stop(kernel);
            Assert.IsEqual(foo.UnregisterCount, 1);
        }

        [Test]
        public void TestLifecycleOrder()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Bar>().AsSingle();
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Boo>().AsSingle();
            Container.Bind<Kernel>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Start(Container.Resolve<Kernel>());

            var bar = Container.Resolve<Bar>();
            var foo = Container.Resolve<Foo>();
            var boo = Container.Resolve<Boo>();

            // Initialize → RegisterEvents → LateInitialize
            Assert.IsEqual(bar.InitializeCount, 1);
            Assert.IsEqual(foo.RegisterCount, 2);
            Assert.IsEqual(boo.LateInitializeCount, 3);
        }

        [Test]
        public void TestDisposeOrder()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Baz>().AsSingle();
            Container.Bind<Kernel>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var kernel = Container.Resolve<Kernel>();
            Start(kernel);

            GlobalCallCount = 0;
            Stop(kernel);

            var foo = Container.Resolve<Foo>();
            var baz = Container.Resolve<Baz>();

            // UnregisterEvents → container Dispose
            Assert.IsEqual(foo.UnregisterCount, 1);
            Assert.IsEqual(baz.DisposeCount, 2);
        }

        // A parent drives a sub-container's Kernel one stage at a time, inside its own passes.
        static void Start(Kernel kernel)
        {
            kernel.Initialize();
            kernel.RegisterEvents();
            kernel.LateInitialize();
        }

        static void Stop(Kernel kernel)
        {
            kernel.UnregisterEvents();
            kernel.Deinitialize();
            kernel.Dispose();
            kernel.LateDispose();
        }
    }
}
