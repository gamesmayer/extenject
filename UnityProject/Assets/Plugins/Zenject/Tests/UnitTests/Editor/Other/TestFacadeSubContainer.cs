using GamesMayer.Lifecycle;
using System;
using NUnit.Framework;
using Assert = ModestTree.Assert;

namespace Zenject.Tests.Other
{
    [TestFixture]
    public class TestFacadeSubContainer
    {
        static int NumInstalls;

        [Test]
        public void Test1()
        {
            NumInstalls = 0;
            InitTest.WasRun = false;
            RegisterEventsTest.WasRegistered = false;
            RegisterEventsTest.WasUnregistered = false;
            LateInitTest.WasRun = false;
            TickTest.WasRun = false;
            DisposeTest.WasRun = false;

            var container = new DiContainer();

            container.Bind(typeof(TickableManager), typeof(InitializableManager), typeof(DisposableManager), typeof(EventRegistrableManager), typeof(LateInitializableManager), typeof(DeinitializableManager))
                .ToSelf().AsSingle().CopyIntoAllSubContainers();

            // This is how you add ITickables / etc. within sub containers
            container.BindInterfacesAndSelfTo<FooKernel>()
                .FromSubContainerResolve().ByMethod(InstallFoo).AsSingle();

            var tickManager = container.Resolve<TickableManager>();
            var initManager = container.Resolve<InitializableManager>();
            var eventRegistrableManager = container.Resolve<EventRegistrableManager>();
            var lateInitManager = container.Resolve<LateInitializableManager>();
            var disposeManager = container.Resolve<DisposableManager>();
            var deinitManager = container.Resolve<DeinitializableManager>();

            Assert.That(!InitTest.WasRun);
            Assert.That(!RegisterEventsTest.WasRegistered);
            Assert.That(!RegisterEventsTest.WasUnregistered);
            Assert.That(!LateInitTest.WasRun);
            Assert.That(!TickTest.WasRun);
            Assert.That(!DisposeTest.WasRun);

            initManager.Initialize();
            eventRegistrableManager.RegisterEvents();
            lateInitManager.LateInitialize();
            tickManager.Update();
            eventRegistrableManager.UnregisterEvents();
            deinitManager.Deinitialize();
            disposeManager.Dispose();

            Assert.That(InitTest.WasRun);
            Assert.That(RegisterEventsTest.WasRegistered);
            Assert.That(RegisterEventsTest.WasUnregistered);
            Assert.That(LateInitTest.WasRun);
            Assert.That(TickTest.WasRun);
            Assert.That(DisposeTest.WasRun);
        }

        public void InstallFoo(DiContainer subContainer)
        {
            NumInstalls++;

            subContainer.Bind<FooKernel>().AsSingle();

            subContainer.Bind<IInitializable>().To<InitTest>().AsSingle();
            subContainer.Bind<IEventRegistrable>().To<RegisterEventsTest>().AsSingle();
            subContainer.Bind<ILateInitializable>().To<LateInitTest>().AsSingle();
            subContainer.Bind<ITickable>().To<TickTest>().AsSingle();
            subContainer.Bind<IDisposable>().To<DisposeTest>().AsSingle();
        }

        public class FooKernel : Kernel
        {
        }

        public class InitTest : IInitializable
        {
            public static bool WasRun;

            public void Initialize()
            {
                WasRun = true;
            }
        }

        public class RegisterEventsTest : IEventRegistrable
        {
            public static bool WasRegistered;
            public static bool WasUnregistered;

            public void RegisterEvents()
            {
                WasRegistered = true;
            }

            public void UnregisterEvents()
            {
                WasUnregistered = true;
            }
        }

        public class LateInitTest : ILateInitializable
        {
            public static bool WasRun;

            public void LateInitialize()
            {
                WasRun = true;
            }
        }

        public class TickTest : ITickable
        {
            public static bool WasRun;

            public void Tick()
            {
                WasRun = true;
            }
        }

        public class DisposeTest : IDisposable
        {
            public static bool WasRun;

            public void Dispose()
            {
                WasRun = true;
            }
        }
    }
}
