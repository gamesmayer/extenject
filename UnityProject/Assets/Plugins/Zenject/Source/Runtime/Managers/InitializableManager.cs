using GamesMayer.Lifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;

namespace Zenject
{
    // Responsibilities:
    // - Run Initialize() on all Iinitializable's, in the order specified by InitPriority
    public class InitializableManager
    {
        List<InitializableInfo> _initializables;

        protected bool _hasInitialized;

        readonly List<IInitializable> _attempted = new List<IInitializable>();
        bool _hasCompleted;

        [Inject]
        public InitializableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IInitializable> initializables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> priorities)
        {
            _initializables = new List<InitializableInfo>();

            for (int i = 0; i < initializables.Count; i++)
            {
                var initializable = initializables[i];

                // Note that we use zero for unspecified priority
                // This is nice because you can use negative or positive for before/after unspecified
                var matches = priorities.Where(x => initializable.GetType().DerivesFromOrEqual(x.First)).Select(x => x.Second).ToList();
                int priority = matches.IsEmpty() ? 0 : matches.Distinct().Single();

                _initializables.Add(new InitializableInfo(initializable, priority));
            }
        }

        public void Add(IInitializable initializable)
        {
            Add(initializable, 0);
        }

        public void Add(IInitializable initializable, int priority)
        {
            Assert.That(!_hasInitialized);
            _initializables.Add(
                new InitializableInfo(initializable, priority));
        }

        public void Initialize()
        {
            Assert.That(!_hasInitialized);
            _hasInitialized = true;

            _initializables = _initializables.OrderBy(x => x.Priority).ToList();

            foreach (var initializable in _initializables)
            {
                // Include the current root so a partial initialization can be undone if it throws.
                _attempted.Add(initializable.Initializable);

                try
                {
#if ZEN_INTERNAL_PROFILING
                    using (ProfileTimers.CreateTimedBlock("User Code"))
#endif
#if UNITY_EDITOR
                    using (ProfileBlock.Start("{0}.Initialize()", initializable.Initializable.GetType()))
#endif
                    {
                        initializable.Initializable.Initialize();
                    }
                }
                catch (Exception e)
                {
                    throw Assert.CreateException(
                        e, "Error occurred while initializing IInitializable with type '{0}'", initializable.Initializable.GetType());
                }
            }

            _hasCompleted = true;
        }

        // True when every Initialize ran, or when this root's own Initialize was attempted before a failure.
        public bool HasReached(object root)
        {
            return _hasCompleted || _attempted.Any(x => ReferenceEquals(x, root));
        }

        class InitializableInfo
        {
            public IInitializable Initializable;
            public int Priority;

            public InitializableInfo(IInitializable initializable, int priority)
            {
                Initializable = initializable;
                Priority = priority;
            }
        }
    }
}
