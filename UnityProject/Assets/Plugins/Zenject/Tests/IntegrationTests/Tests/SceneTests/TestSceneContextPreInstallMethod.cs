using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Zenject.Tests
{
    public class TestSceneContextPreInstallMethod : SceneTestFixture
    {
        [UnityTest]
        public IEnumerator TestPreInstallMethodIsCalledBeforeInstall()
        {
            bool hookCalled = false;
            bool hasInstalledWhenHookCalled = false;

            SceneContext.PreInstallMethod = (ctx) =>
            {
                hookCalled = true;
                hasInstalledWhenHookCalled = ctx.HasInstalled;
            };

            yield return LoadScene("TestSceneContextPreInstallMethod");

            Assert.That(hookCalled, "PreInstallMethod was never called");
            Assert.That(!hasInstalledWhenHookCalled, "PreInstallMethod must run before Install()");
        }
    }
}
