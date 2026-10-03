namespace Zenject.Tests
{
    public class AsyncStartupInstaller : MonoInstaller
    {
        public override void InstallBindings()
        {
            AsyncStartupLog.Add("SceneInstaller.InstallBindings");
            Container.Bind<AsyncStartupMarker>().AsSingle();
        }
    }
}
