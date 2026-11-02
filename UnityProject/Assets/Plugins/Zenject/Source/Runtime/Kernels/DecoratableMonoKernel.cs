namespace Zenject
{
    public interface IDecoratableMonoKernel
    {
        bool ShouldInitializeOnStart();
        void Initialize();
        void Update();
        void FixedUpdate();
        void LateUpdate();
        // UnregisterEvents -> Deinitialize -> container Dispose and LateDispose
        void Dispose();
    }

    public class DecoratableMonoKernel : IDecoratableMonoKernel
    {
        [InjectLocal] 
        public TickableManager TickableManager { get; protected set; } = null;

        [InjectLocal]
        public InitializableManager InitializableManager { get; protected set; } = null;

        [InjectLocal]
        public LateInitializableManager LateInitializableManager { get; protected set; } = null;

        [InjectLocal]
        public DisposableManager DisposablesManager { get; protected set; } = null;

        [InjectLocal]
        public EventRegistrableManager EventRegistrableManager { get; protected set; } = null;

        [InjectLocal]
        public DeinitializableManager DeinitializableManager { get; protected set; } = null;
        
        
        public virtual bool ShouldInitializeOnStart() => true;
        
        public virtual void Initialize()
        {
            InitializableManager.Initialize();
            EventRegistrableManager.RegisterEvents();
            LateInitializableManager.LateInitialize();
        }

        public void Update()
        {
            TickableManager.Update();
        }

        public void FixedUpdate()
        {
            TickableManager.FixedUpdate();
        }

        public void LateUpdate()
        {
            TickableManager.LateUpdate();
        }

        public void Dispose()
        {
            Internal.LifecycleTeardown.Run(EventRegistrableManager, DeinitializableManager, DisposablesManager);
        }
    }

    public abstract class BaseMonoKernelDecorator : IDecoratableMonoKernel
    {
        [Inject] 
        protected IDecoratableMonoKernel DecoratedMonoKernel;

        public virtual bool ShouldInitializeOnStart() => DecoratedMonoKernel.ShouldInitializeOnStart();
        public virtual void Initialize() => DecoratedMonoKernel.Initialize();
        public virtual void Update() => DecoratedMonoKernel.Update();
        public virtual void FixedUpdate() => DecoratedMonoKernel.FixedUpdate();
        public virtual void LateUpdate() => DecoratedMonoKernel.LateUpdate();
        public virtual void Dispose() => DecoratedMonoKernel.Dispose();
    }
    
}