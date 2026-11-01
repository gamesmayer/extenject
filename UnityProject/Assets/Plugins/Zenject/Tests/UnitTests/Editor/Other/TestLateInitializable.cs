using GamesMayer.Lifecycle;
using NUnit.Framework;
using Assert = ModestTree.Assert;

namespace Zenject.Tests.Other
{
    [TestFixture]
    public class TestLateInitializable : ZenjectUnitTestFixture
    {
        static int GlobalCallCount;

        public class Foo : ILateInitializable
        {
            public bool WasLateInitialized { get; private set; }
            public int LateInitializeCount { get; private set; }

            public void LateInitialize()
            {
                LateInitializeCount = ++GlobalCallCount;
                WasLateInitialized = true;
            }
        }

        [Test]
        public void TestBasicLateInitialize()
        {
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var foo = Container.Resolve<Foo>();
            Assert.That(!foo.WasLateInitialized);

            Container.Resolve<LateInitializableManager>().LateInitialize();
            Assert.That(foo.WasLateInitialized);
        }

        public class Bar : IInitializable
        {
            public int InitializeCount { get; private set; }

            public void Initialize()
            {
                InitializeCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestLateInitializeCalledAfterInitialize()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Bar>().AsSingle();
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Container.Resolve<InitializableManager>().Initialize();
            Container.Resolve<LateInitializableManager>().LateInitialize();

            var bar = Container.Resolve<Bar>();
            var foo = Container.Resolve<Foo>();

            Assert.IsEqual(bar.InitializeCount, 1);
            Assert.IsEqual(foo.LateInitializeCount, 2);
        }

        public class Baz : ILateInitializable
        {
            public int LateInitializeCount { get; private set; }

            public void LateInitialize()
            {
                LateInitializeCount = ++GlobalCallCount;
            }
        }

        [Test]
        public void TestPriorityOrdering()
        {
            GlobalCallCount = 0;

            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            Container.BindInterfacesAndSelfTo<Baz>().AsSingle();
            Container.BindLateInitializableExecutionOrder<Foo>(1);
            Container.BindLateInitializableExecutionOrder<Baz>(-1);
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            Container.Resolve<LateInitializableManager>().LateInitialize();

            var foo = Container.Resolve<Foo>();
            var baz = Container.Resolve<Baz>();

            // Baz has lower priority (-1) so it goes first
            Assert.IsEqual(baz.LateInitializeCount, 1);
            Assert.IsEqual(foo.LateInitializeCount, 2);
        }

        [Test]
        public void TestLateInitializeCannotBeCalledTwice()
        {
            Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
            ZenjectManagersInstaller.Install(Container);
            Container.ResolveRoots();

            var manager = Container.Resolve<LateInitializableManager>();
            manager.LateInitialize();

            Assert.Throws(() => manager.LateInitialize());
        }
    }
}
