using System.Linq;
using NUnit.Framework;
using Assert = ModestTree.Assert;

namespace Zenject.Tests.Bindings
{
    [TestFixture]
    public class TestPoolDisposal : ZenjectUnitTestFixture
    {
        public class Foo : IPoolable<IMemoryPool>
        {
            public IMemoryPool Pool { get; private set; }

            public void OnDespawned()
            {
                Pool = null;
            }

            public void OnSpawned(IMemoryPool pool)
            {
                Pool = pool;
            }

            public class Factory : PlaceholderFactory<Foo>
            {
            }
        }

        [Test]
        public void TestPoolBoundWithIdIsDisposedWithItsContainer()
        {
            Container.Bind<DisposableManager>().AsSingle();
            Container.BindFactory<Foo, Foo.Factory>().FromPoolableMemoryPool(x => x.WithInitialSize(1).FromNew());

            IMemoryPool pool = Container.Resolve<Foo.Factory>().Create().Pool;

            Assert.That(StaticMemoryPoolRegistry.Pools.Contains(pool));

            Container.Resolve<DisposableManager>().Dispose();

            Assert.That(!StaticMemoryPoolRegistry.Pools.Contains(pool));
        }

        [Test]
        public void TestPoolDisposedTwiceLeavesTheRegistryOnce()
        {
            Container.Bind<DisposableManager>().AsSingle();
            Container.BindFactory<Foo, Foo.Factory>().FromPoolableMemoryPool(x => x.WithInitialSize(1).FromNew());

            IMemoryPool pool = Container.Resolve<Foo.Factory>().Create().Pool;

            ((System.IDisposable)pool).Dispose();
            Container.Resolve<DisposableManager>().Dispose();

            Assert.That(!StaticMemoryPoolRegistry.Pools.Contains(pool));
        }
    }
}
