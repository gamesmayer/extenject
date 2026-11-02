using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;
using Zenject.Internal;

namespace Zenject
{
    // The container's System.IDisposable pass, after every IDeinitializable. Lifecycle state belongs in Deinitialize.
    public class DisposableManager : IDisposable
    {
        readonly List<DisposableInfo> _disposables = new List<DisposableInfo>();
        readonly List<LateDisposableInfo> _lateDisposables = new List<LateDisposableInfo>();
        bool _disposed;
        bool _lateDisposed;

        [Inject]
        public DisposableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IDisposable> disposables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> priorities,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IContainerLateDisposable> lateDisposables,
            [Inject(Id = "Late", Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> latePriorities)
        {
            foreach (var disposable in disposables)
            {
                // Note that we use zero for unspecified priority
                // This is nice because you can use negative or positive for before/after unspecified
                var match = priorities.Where(x => disposable.GetType().DerivesFromOrEqual(x.First)).Select(x => (int?)x.Second).SingleOrDefault();
                int priority = match.HasValue ? match.Value : 0;

                _disposables.Add(new DisposableInfo(disposable, priority));
            }

            foreach (var lateDisposable in lateDisposables)
            {
                var match = latePriorities.Where(x => lateDisposable.GetType().DerivesFromOrEqual(x.First)).Select(x => (int?)x.Second).SingleOrDefault();
                int priority = match.HasValue ? match.Value : 0;

                _lateDisposables.Add(new LateDisposableInfo(lateDisposable, priority));
            }
        }

        public void Add(IDisposable disposable)
        {
            Add(disposable, 0);
        }

        public void Add(IDisposable disposable, int priority)
        {
            _disposables.Add(
                new DisposableInfo(disposable, priority));
        }

        public void AddLate(IContainerLateDisposable disposable)
        {
            AddLate(disposable, 0);
        }

        public void AddLate(IContainerLateDisposable disposable, int priority)
        {
            _lateDisposables.Add(
                new LateDisposableInfo(disposable, priority));
        }

        public void Remove(IDisposable disposable)
        {
            _disposables.RemoveWithConfirm(
                _disposables.Where(x => ReferenceEquals(x.Disposable, disposable)).Single());
        }

        public void LateDispose()
        {
            if (_lateDisposed)
            {
                return;
            }

            _lateDisposed = true;

            var errors = new List<Exception>();

            // Dispose in the reverse order that they are initialized in
            foreach (var disposable in _lateDisposables.OrderBy(x => x.Priority).Reverse().ToList())
            {
                try
                {
                    disposable.LateDisposable.LateDispose();
                }
                catch (Exception e)
                {
                    errors.Add(Assert.CreateException(
                        e, "Error occurred while late disposing '{0}'", disposable.LateDisposable.GetType()));
                }
            }

            LifecycleTeardown.ThrowIfAny(errors);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var errors = new List<Exception>();

            // Dispose in the reverse order that they are initialized in
            foreach (var disposable in _disposables.OrderBy(x => x.Priority).Reverse().ToList())
            {
                try
                {
                    disposable.Disposable.Dispose();
                }
                catch (Exception e)
                {
                    errors.Add(Assert.CreateException(
                        e, "Error occurred while disposing IDisposable with type '{0}'", disposable.Disposable.GetType()));
                }
            }

            LifecycleTeardown.ThrowIfAny(errors);
        }

        struct DisposableInfo
        {
            public IDisposable Disposable;
            public int Priority;

            public DisposableInfo(IDisposable disposable, int priority)
            {
                Disposable = disposable;
                Priority = priority;
            }
        }

        class LateDisposableInfo
        {
            public IContainerLateDisposable LateDisposable;
            public int Priority;

            public LateDisposableInfo(IContainerLateDisposable lateDisposable, int priority)
            {
                LateDisposable = lateDisposable;
                Priority = priority;
            }
        }
    }
}
