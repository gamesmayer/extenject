using ModestTree;
#if !NOT_UNITY3D
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zenject
{
    public abstract class RunnableContext : Context
    {
        [Tooltip("When false, wait until run method is explicitly called. Otherwise run on initialize")]
        [SerializeField]
        bool _autoRun = true;

        static bool _staticAutoRun = true;

        public bool Initialized { get; private set; }

        bool _isRunDeferred;

#if UNITY_EDITOR
        // Required for disabling domain reload in enter the play mode feature. See: https://docs.unity3d.com/Manual/DomainReloading.html
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticValues()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return;
            }
            
            _staticAutoRun = true;
        }
#endif

#if UNITY_EDITOR
        protected override void ResetInstanceFields()
        {
            base.ResetInstanceFields();
            
            Initialized = false;
            _isRunDeferred = false;
        }
#endif

        protected void Initialize()
        {
            if (_staticAutoRun && _autoRun)
            {
                Run();
            }
            else
            {
                // True should always be default
                _staticAutoRun = true;
            }
        }

        public void Run()
        {
            Assert.That(!Initialized && !_isRunDeferred,
                "The context already has been initialized!");

            RunInternal();

            if (!_isRunDeferred)
            {
                Initialized = true;
            }
        }

        protected abstract void RunInternal();

        // True while a run continues asynchronously (see SceneContext async startup)
        public bool IsInitializing
        {
            get { return _isRunDeferred; }
        }

        // Called from RunInternal when the run continues asynchronously.
        // Initialized stays false until CompleteDeferredRun is called.
        protected void DeferRun()
        {
            _isRunDeferred = true;
        }

        protected void CompleteDeferredRun()
        {
            Assert.That(_isRunDeferred);
            _isRunDeferred = false;
            Initialized = true;
        }

        // The asynchronous run failed: the context stays uninitialized
        protected void FailDeferredRun()
        {
            _isRunDeferred = false;
        }

        public static T CreateComponent<T>(GameObject gameObject) where T : RunnableContext
        {
            _staticAutoRun = false;

            var result = gameObject.AddComponent<T>();
            Assert.That(_staticAutoRun); // Should be reset
            return result;
        }
    }
}

#endif
