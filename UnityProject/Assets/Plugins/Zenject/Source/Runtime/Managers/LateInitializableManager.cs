using GamesMayer.Lifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;

namespace Zenject
{
    // Responsibilities:
    // - Run LateInitialize() on all ILateInitializable's, in the order specified by InitPriority
    public class LateInitializableManager
    {
        List<LateInitializableInfo> _lateInitializables;

        protected bool _hasLateInitialized;

        [Inject]
        public LateInitializableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ILateInitializable> lateInitializables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> priorities)
        {
            _lateInitializables = new List<LateInitializableInfo>();

            for (int i = 0; i < lateInitializables.Count; i++)
            {
                var lateInitializable = lateInitializables[i];

                // Note that we use zero for unspecified priority
                // This is nice because you can use negative or positive for before/after unspecified
                var matches = priorities.Where(x => lateInitializable.GetType().DerivesFromOrEqual(x.First)).Select(x => x.Second).ToList();
                int priority = matches.IsEmpty() ? 0 : matches.Distinct().Single();

                _lateInitializables.Add(new LateInitializableInfo(lateInitializable, priority));
            }
        }

        public void Add(ILateInitializable lateInitializable)
        {
            Add(lateInitializable, 0);
        }

        public void Add(ILateInitializable lateInitializable, int priority)
        {
            Assert.That(!_hasLateInitialized);
            _lateInitializables.Add(
                new LateInitializableInfo(lateInitializable, priority));
        }

        public void LateInitialize()
        {
            Assert.That(!_hasLateInitialized);
            _hasLateInitialized = true;

            _lateInitializables = _lateInitializables.OrderBy(x => x.Priority).ToList();

            foreach (var lateInitializable in _lateInitializables)
            {
                try
                {
#if ZEN_INTERNAL_PROFILING
                    using (ProfileTimers.CreateTimedBlock("User Code"))
#endif
#if UNITY_EDITOR
                    using (ProfileBlock.Start("{0}.LateInitialize()", lateInitializable.LateInitializable.GetType()))
#endif
                    {
                        lateInitializable.LateInitializable.LateInitialize();
                    }
                }
                catch (Exception e)
                {
                    throw Assert.CreateException(
                        e, "Error occurred while initializing ILateInitializable with type '{0}'", lateInitializable.LateInitializable.GetType());
                }
            }
        }

        class LateInitializableInfo
        {
            public ILateInitializable LateInitializable;
            public int Priority;

            public LateInitializableInfo(ILateInitializable lateInitializable, int priority)
            {
                LateInitializable = lateInitializable;
                Priority = priority;
            }
        }
    }
}
