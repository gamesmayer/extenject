using GamesMayer.Lifecycle;
using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;

namespace Zenject
{
    // Responsibilities:
    // - Run RegisterEvents() on all IEventRegistrable's, in the order specified by priority
    // - Run UnregisterEvents() on all IEventRegistrable's, in the reverse order
    public class EventRegistrableManager
    {
        List<EventRegistrableInfo> _eventRegistrables;

        bool _hasRegistered;
        bool _hasUnregistered;

        [Inject]
        public EventRegistrableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IEventRegistrable> eventRegistrables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ValuePair<Type, int>> priorities)
        {
            _eventRegistrables = new List<EventRegistrableInfo>();

            for (int i = 0; i < eventRegistrables.Count; i++)
            {
                var eventRegistrable = eventRegistrables[i];

                // Note that we use zero for unspecified priority
                // This is nice because you can use negative or positive for before/after unspecified
                var matches = priorities.Where(x => eventRegistrable.GetType().DerivesFromOrEqual(x.First)).Select(x => x.Second).ToList();
                int priority = matches.IsEmpty() ? 0 : matches.Distinct().Single();

                _eventRegistrables.Add(new EventRegistrableInfo(eventRegistrable, priority));
            }
        }

        public void Add(IEventRegistrable eventRegistrable)
        {
            Add(eventRegistrable, 0);
        }

        public void Add(IEventRegistrable eventRegistrable, int priority)
        {
            Assert.That(!_hasRegistered);
            _eventRegistrables.Add(new EventRegistrableInfo(eventRegistrable, priority));
        }

        public void RegisterEvents()
        {
            Assert.That(!_hasRegistered);
            _hasRegistered = true;

            _eventRegistrables = _eventRegistrables.OrderBy(x => x.Priority).ToList();

#if UNITY_EDITOR
            foreach (var eventRegistrable in _eventRegistrables.Select(x => x.EventRegistrable).GetDuplicates())
            {
                Assert.That(false, "Found duplicate IEventRegistrable with type '{0}'".Fmt(eventRegistrable.GetType()));
            }
#endif

            foreach (var eventRegistrable in _eventRegistrables)
            {
                try
                {
#if ZEN_INTERNAL_PROFILING
                    using (ProfileTimers.CreateTimedBlock("User Code"))
#endif
#if UNITY_EDITOR
                    using (ProfileBlock.Start("{0}.RegisterEvents()", eventRegistrable.EventRegistrable.GetType()))
#endif
                    {
                        eventRegistrable.EventRegistrable.RegisterEvents();
                    }
                }
                catch (Exception e)
                {
                    throw Assert.CreateException(
                        e, "Error occurred while registering events on IEventRegistrable with type '{0}'", eventRegistrable.EventRegistrable.GetType());
                }
            }
        }

        public void UnregisterEvents()
        {
            Assert.That(_hasRegistered, "Tried to call UnregisterEvents before RegisterEvents was called!");
            Assert.That(!_hasUnregistered, "Tried to call UnregisterEvents twice!");
            _hasUnregistered = true;

            // Unregister in the reverse order that they were registered
            var eventRegistrablesOrdered = _eventRegistrables.OrderBy(x => x.Priority).Reverse().ToList();

            foreach (var eventRegistrable in eventRegistrablesOrdered)
            {
                try
                {
#if ZEN_INTERNAL_PROFILING
                    using (ProfileTimers.CreateTimedBlock("User Code"))
#endif
#if UNITY_EDITOR
                    using (ProfileBlock.Start("{0}.UnregisterEvents()", eventRegistrable.EventRegistrable.GetType()))
#endif
                    {
                        eventRegistrable.EventRegistrable.UnregisterEvents();
                    }
                }
                catch (Exception e)
                {
                    throw Assert.CreateException(
                        e, "Error occurred while unregistering events on IEventRegistrable with type '{0}'", eventRegistrable.EventRegistrable.GetType());
                }
            }
        }

        class EventRegistrableInfo
        {
            public IEventRegistrable EventRegistrable;
            public int Priority;

            public EventRegistrableInfo(IEventRegistrable eventRegistrable, int priority)
            {
                EventRegistrable = eventRegistrable;
                Priority = priority;
            }
        }
    }
}
