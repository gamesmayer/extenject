using GamesMayer.Lifecycle;
using System;
using System.Diagnostics;
using Zenject.Internal;

namespace Zenject
{
    // Drives a sub-container (WithKernel) from its parent. Each stage runs inside the parent's pass for that
    // same stage, so the sub-container's roots respect the parent's barriers and priorities.
    [DebuggerStepThrough]
    public class Kernel : IInitializable, IEventRegistrable, ILateInitializable, ITickable, ILateTickable, IFixedTickable, IDeinitializable, IDisposable, IContainerLateDisposable
    {
        [InjectLocal]
        TickableManager _tickableManager = null;

        [InjectLocal]
        InitializableManager _initializableManager = null;

        [InjectLocal]
        LateInitializableManager _lateInitializableManager = null;

        [InjectLocal]
        DisposableManager _disposablesManager = null;

        [InjectLocal]
        EventRegistrableManager _eventRegistrableManager = null;

        [InjectLocal]
        DeinitializableManager _deinitializableManager = null;

        public virtual void Initialize()
        {
            _initializableManager.Initialize();
        }

        public virtual void RegisterEvents()
        {
            _eventRegistrableManager.RegisterEvents();
        }

        public virtual void LateInitialize()
        {
            _lateInitializableManager.LateInitialize();
        }

        public virtual void UnregisterEvents()
        {
            _eventRegistrableManager.UnregisterEvents();
        }

        public virtual void Deinitialize()
        {
            _deinitializableManager.Deinitialize();
        }

        public virtual void Dispose()
        {
            _disposablesManager.Dispose();
        }

        public virtual void LateDispose()
        {
            _disposablesManager.LateDispose();
        }

        public virtual void Tick()
        {
            _tickableManager.Update();
        }

        public virtual void LateTick()
        {
            _tickableManager.LateUpdate();
        }

        public virtual void FixedTick()
        {
            _tickableManager.FixedUpdate();
        }
    }
}
