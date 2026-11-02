using GamesMayer.Lifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;

namespace Zenject
{
    // Responsibilities:
    // - Run Deinitialize() on the IDeinitializable's that startup reached, in the reverse order of priority
    // - Keep going when one throws and report every error together
    public class DeinitializableManager
    {
        readonly List<DeinitializableInfo> _deinitializables = new List<DeinitializableInfo>();
        readonly InitializableManager _initializableManager;

        bool _hasDeinitialized;

        [Inject]
        public DeinitializableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IDeinitializable> deinitializables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> priorities,
            [Inject(Source = InjectSources.Local)]
            InitializableManager initializableManager)
        {
            _initializableManager = initializableManager;

            foreach (var deinitializable in deinitializables)
            {
                var matches = priorities.Where(x => deinitializable.GetType().DerivesFromOrEqual(x.First)).Select(x => x.Second).ToList();
                int priority = matches.IsEmpty() ? 0 : matches.Distinct().Single();

                _deinitializables.Add(new DeinitializableInfo(deinitializable, priority));
            }
        }

        public void Add(IDeinitializable deinitializable)
        {
            Add(deinitializable, 0);
        }

        public void Add(IDeinitializable deinitializable, int priority)
        {
            Assert.That(!_hasDeinitialized);
            _deinitializables.Add(new DeinitializableInfo(deinitializable, priority));
        }

        public void Deinitialize()
        {
            if (_hasDeinitialized)
            {
                return;
            }

            _hasDeinitialized = true;

            var errors = new List<Exception>();

            // Reverse of the startup order: priority descending, then reverse binding order
            foreach (var info in _deinitializables.OrderBy(x => x.Priority).Reverse().ToList())
            {
                if (!_initializableManager.HasReached(info.Deinitializable))
                {
                    continue;
                }

                try
                {
#if UNITY_EDITOR
                    using (ProfileBlock.Start("{0}.Deinitialize()", info.Deinitializable.GetType()))
#endif
                    {
                        info.Deinitializable.Deinitialize();
                    }
                }
                catch (Exception e)
                {
                    errors.Add(Assert.CreateException(
                        e, "Error occurred while deinitializing IDeinitializable with type '{0}'", info.Deinitializable.GetType()));
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException(errors);
            }
        }

        class DeinitializableInfo
        {
            public IDeinitializable Deinitializable;
            public int Priority;

            public DeinitializableInfo(IDeinitializable deinitializable, int priority)
            {
                Deinitializable = deinitializable;
                Priority = priority;
            }
        }
    }
}
