using GamesMayer.Lifecycle;
using System;
using System.Diagnostics;

namespace Zenject
{
    [DebuggerStepThrough]
    public class Kernel : IInitializable, IDisposable, ITickable, ILateTickable, IFixedTickable, ILateDisposable
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

        public virtual void Initialize()
        {
            _initializableManager.Initialize();
            _eventRegistrableManager.RegisterEvents();
            _lateInitializableManager.LateInitialize();
        }

        public virtual void Dispose()
        {
            _eventRegistrableManager.UnregisterEvents();
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
