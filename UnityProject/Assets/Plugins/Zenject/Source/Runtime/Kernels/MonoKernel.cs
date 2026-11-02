#if !NOT_UNITY3D

#pragma warning disable 649

using System;
using ModestTree;
using UnityEngine;

namespace Zenject
{
    public abstract class MonoKernel : MonoBehaviour
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

        [InjectOptional] 
        private IDecoratableMonoKernel decoratableMonoKernel;

        bool _hasInitialized;
        bool _isRunning;
        bool _isDestroyed;

        protected bool IsDestroyed
        {
            get { return _isDestroyed; }
        }

        public virtual void Start()
        {
            if (decoratableMonoKernel?.ShouldInitializeOnStart()??true)
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            // We don't put this in start in case Start is overridden
            if (!_hasInitialized)
            {
                _hasInitialized = true;

                try
                {
                    if (decoratableMonoKernel != null)
                    {
                        decoratableMonoKernel.Initialize();
                    }
                    else
                    {
                        _initializableManager.Initialize();
                        _eventRegistrableManager.RegisterEvents();
                        _lateInitializableManager.LateInitialize();
                    }

                    _isRunning = true;
                }
                catch (Exception startError)
                {
                    try
                    {
                        Shutdown();
                    }
                    catch (Exception stopError)
                    {
                        throw new AggregateException(startError, stopError);
                    }

                    throw;
                }
            }
        }

        public virtual void Update()
        {
            // Don't spam the log every frame if initialization fails and leaves it as null
            if (_isRunning && !_isDestroyed && _tickableManager != null)
            {
                if (decoratableMonoKernel != null)
                {
                    decoratableMonoKernel.Update();
                }
                else
                {
                    _tickableManager.Update();
                }
            }
        }

        public virtual void FixedUpdate()
        {
            // Don't spam the log every frame if initialization fails and leaves it as null
            if (_isRunning && !_isDestroyed && _tickableManager != null)
            {
                if (decoratableMonoKernel != null)
                {
                    decoratableMonoKernel.FixedUpdate();
                }
                else
                {
                    _tickableManager.FixedUpdate();
                }
            }
        }

        public virtual void LateUpdate()
        {
            // Don't spam the log every frame if initialization fails and leaves it as null
            if (_isRunning && !_isDestroyed && _tickableManager != null)
            {
                if (decoratableMonoKernel != null)
                {
                    decoratableMonoKernel.LateUpdate();
                }
                else
                {
                    _tickableManager.LateUpdate();
                }
            }
        }

        // Runs UnregisterEvents -> Deinitialize -> container disposal once. Loaders call it before unloading a scene
        // (ProjectKernel.ShutdownScenes) so Deinitialize still finds the scene intact; OnDestroy is only the fallback.
        public void Shutdown()
        {
            if (_isDestroyed)
            {
                return;
            }

            _isDestroyed = true;
            _isRunning = false;

            // Injection may not have completed when Unity destroys the kernel.
            if (_disposablesManager == null)
            {
                return;
            }

            if (decoratableMonoKernel != null)
            {
                decoratableMonoKernel.Dispose();
            }
            else
            {
                Internal.LifecycleTeardown.Run(_eventRegistrableManager, _deinitializableManager, _disposablesManager);
            }
        }

        public virtual void OnDestroy()
        {
            Shutdown();
        }
    }
}

#endif
