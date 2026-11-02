namespace Zenject.Internal
{
    // Container bookkeeping that must run after every IDisposable, such as SignalBus and pool checks.
    // It is not a lifecycle stage: game code releases its state in GamesMayer.Lifecycle.IDeinitializable.
    public interface IContainerLateDisposable
    {
        void LateDispose();
    }
}
