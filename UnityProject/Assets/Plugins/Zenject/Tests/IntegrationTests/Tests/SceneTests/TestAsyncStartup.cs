using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Assert = NUnit.Framework.Assert;

namespace Zenject.Tests
{
    public class TestAsyncStartup : SceneTestFixture
    {
        const string SceneName = "TestAsyncStartup";

        public override void SetUp()
        {
            base.SetUp();
            AsyncStartupLog.Clear();

            ProjectContext.PostInstall += OnProjectPostInstall;
            ProjectContext.PreResolve += OnProjectPreResolve;
            ProjectContext.PostResolve += OnProjectPostResolve;
        }

        public override void Teardown()
        {
            ProjectContext.PreInstall -= BindProjectInitializable;
            ProjectContext.PostInstall -= OnProjectPostInstall;
            ProjectContext.PreResolve -= OnProjectPreResolve;
            ProjectContext.PostResolve -= OnProjectPostResolve;

            ProjectContext.PreResolveRoutine = null;
            SceneContext.PreInstallMethod = null;
            SceneContext.PreInstallRoutine = null;
            AsyncStartupLog.Clear();

            base.Teardown();
        }

        [UnityTest]
        public IEnumerator TestPreResolveRoutineRunsBetweenInstallAndResolve()
        {
            ProjectContext.PreResolveRoutine = () => WaitFrames("PreResolveRoutine", 3);

            yield return LoadScene(SceneName);

            AssertOrder(
                "ProjectContext.PostInstall",
                "ProjectContext.PreResolve",
                "PreResolveRoutine.Start",
                "PreResolveRoutine.End",
                "ProjectContext.PostResolve",
                "SceneInstaller.InstallBindings");
        }

        [UnityTest]
        public IEnumerator TestPreInstallRoutineRunsBeforeSceneInstall()
        {
            SceneContext.PreInstallMethod = context =>
            {
                AsyncStartupLog.Add("PreInstallMethod");
                context.PostResolve += () => AsyncStartupLog.Add("SceneContext.PostResolve");
            };
            SceneContext.PreInstallRoutine = context => WaitFrames("PreInstallRoutine", 3);

            yield return LoadScene(SceneName);

            AssertOrder(
                "ProjectContext.PostResolve",
                "PreInstallMethod",
                "PreInstallRoutine.Start",
                "PreInstallRoutine.End",
                "SceneInstaller.InstallBindings",
                "SceneContext.PostResolve",
                "Probe.Awake:ActiveRoot");
        }

        // The ProjectContext is resolved and activated before the first scene's PreInstallRoutine runs, so its kernel
        // initializes project-level objects while that routine is still waiting
        [UnityTest]
        public IEnumerator TestProjectInitializablesRunWhileFirstScenePreInstallRoutineWaits()
        {
            ProjectContext.PreInstall += BindProjectInitializable;
            ProjectContext.PreResolveRoutine = () => WaitFrames("PreResolveRoutine", 3);
            SceneContext.PreInstallRoutine = context => WaitFrames("PreInstallRoutine", 5);

            yield return LoadScene(SceneName);

            AssertOrder(
                "PreResolveRoutine.End",
                "ProjectContext.PostResolve",
                "PreInstallRoutine.Start",
                "ProjectInitializable.Initialize",
                "PreInstallRoutine.End",
                "SceneInstaller.InstallBindings");
        }

        // Scene-level objects are only created once the scene installs, so they never initialize before
        // PreInstallRoutine has finished
        [UnityTest]
        public IEnumerator TestSceneInitializablesRunAfterPreInstallRoutine()
        {
            ProjectContext.PreInstall += BindProjectInitializable;
            SceneContext.PreInstallMethod = context =>
            {
                context.PreInstall += () => context.Container.BindInterfacesTo<SceneInitializableProbe>().AsSingle();
            };
            SceneContext.PreInstallRoutine = context => WaitFrames("PreInstallRoutine", 5);

            yield return LoadScene(SceneName);

            // The scene kernel initializes on the frame after the scene has been installed
            yield return null;
            yield return null;

            AssertOrder(
                "PreInstallRoutine.Start",
                "ProjectInitializable.Initialize",
                "PreInstallRoutine.End",
                "SceneInstaller.InstallBindings",
                "SceneInitializable.Initialize");
        }

        [UnityTest]
        public IEnumerator TestSceneObjectsStayInactiveUntilInjected()
        {
            bool activeRootWasActive = true;
            bool contextChildWasActive = true;
            bool probeHadAwoken = true;

            SceneContext.PreInstallRoutine = context =>
            {
                GameObject activeRoot = FindRoot(context.gameObject.scene, "ActiveRoot");
                activeRootWasActive = activeRoot.activeSelf;
                contextChildWasActive = context.transform.Find("ContextChild").gameObject.activeSelf;
                probeHadAwoken = activeRoot.GetComponent<AsyncStartupProbe>().HasAwoken;

                return WaitFrames("PreInstallRoutine", 2);
            };

            yield return LoadScene(SceneName);

            Assert.That(!activeRootWasActive, "Scene roots must be inactive while the startup routines run");
            Assert.That(!contextChildWasActive, "Children of the SceneContext must be inactive while the startup routines run");
            Assert.That(!probeHadAwoken, "Scene objects must not awake before the scene is injected");

            Scene scene = SceneManager.GetSceneByName(SceneName);

            foreach (string probeName in new[] { "ActiveRoot", "ContextChild" })
            {
                AsyncStartupProbe probe = FindProbe(scene, probeName);

                Assert.That(probe.gameObject.activeSelf, probeName + " must be active again after startup");
                Assert.That(probe.HasAwoken, probeName + " never awoke");
                Assert.That(probe.WasInjectedBeforeAwake, probeName + " awoke before being injected");
            }
        }

        [UnityTest]
        public IEnumerator TestInactiveRootStaysInactive()
        {
            SceneContext.PreInstallRoutine = context => WaitFrames("PreInstallRoutine", 2);

            yield return LoadScene(SceneName);

            AsyncStartupProbe probe = FindProbe(SceneManager.GetSceneByName(SceneName), "InactiveRoot");

            Assert.That(!probe.gameObject.activeSelf, "A root that was inactive in the scene must stay inactive");
            Assert.That(!probe.HasAwoken);
        }

        [UnityTest]
        public IEnumerator TestContextIsNotInitializedWhileRoutinesRun()
        {
            bool wasInitialized = true;
            bool hadResolved = true;
            bool wasInitializing = false;

            SceneContext.PreInstallRoutine = context =>
            {
                wasInitialized = context.Initialized;
                hadResolved = context.HasResolved;
                wasInitializing = context.IsInitializing;

                return WaitFrames("PreInstallRoutine", 2);
            };

            yield return LoadScene(SceneName);

            SceneContext sceneContext = FindSceneContext();

            Assert.That(!wasInitialized);
            Assert.That(!hadResolved);
            Assert.That(wasInitializing);
            Assert.That(sceneContext.Initialized);
            Assert.That(sceneContext.HasResolved);
            Assert.That(!sceneContext.IsInitializing);
        }

        [UnityTest]
        public IEnumerator TestProjectContextInstanceThrowsWhileWaitingForPreResolveRoutine()
        {
            bool threw = false;

            ProjectContext.PreResolveRoutine = () =>
            {
                try
                {
                    ProjectContext unused = ProjectContext.Instance;
                }
                catch (ZenjectException)
                {
                    threw = true;
                }

                return WaitFrames("PreResolveRoutine", 1);
            };

            // ProjectContext invokes the delegate once it has started waiting for it
            yield return LoadScene(SceneName);

            Assert.That(threw, "ProjectContext.Instance must throw while waiting for PreResolveRoutine");
            Assert.IsNotNull(ProjectContext.Instance);
        }

        [UnityTest]
        public IEnumerator TestRoutinesAreSingleUse()
        {
            int preResolveCalls = 0;
            int preInstallCalls = 0;

            ProjectContext.PreResolveRoutine = () =>
            {
                preResolveCalls++;

                return WaitFrames("PreResolveRoutine", 1);
            };
            SceneContext.PreInstallRoutine = context =>
            {
                preInstallCalls++;

                return WaitFrames("PreInstallRoutine", 1);
            };

            yield return LoadScene(SceneName);

            Assert.AreEqual(1, preResolveCalls);
            Assert.AreEqual(1, preInstallCalls);
            Assert.IsNull(ProjectContext.PreResolveRoutine);
            Assert.IsNull(SceneContext.PreInstallRoutine);
        }

        [UnityTest]
        public IEnumerator TestWithoutRoutinesSceneRunsSynchronously()
        {
            int preInstallFrame = -1;
            bool activeRootWasActive = false;

            SceneContext.PreInstallMethod = context =>
            {
                preInstallFrame = Time.frameCount;
                activeRootWasActive = FindRoot(context.gameObject.scene, "ActiveRoot").activeSelf;
            };

            yield return LoadScene(SceneName);

            SceneContext sceneContext = FindSceneContext();
            AsyncStartupProbe probe = FindProbe(SceneManager.GetSceneByName(SceneName), "ActiveRoot");

            Assert.That(activeRootWasActive, "Without startup routines no scene object is deactivated");
            Assert.That(sceneContext.Initialized);
            Assert.That(probe.WasInjectedBeforeAwake);
            Assert.AreEqual(preInstallFrame, probe.AwakeFrame, "Without startup routines the scene must initialize in the frame it awakes");
        }

        [UnityTest]
        public IEnumerator TestPreInstallRoutineExceptionLeavesSceneUninitialized()
        {
            SceneContext.PreInstallRoutine = context => ThrowAfterOneFrame();
            LogAssert.Expect(LogType.Exception, new Regex("Async startup test failure"));

            yield return LoadScene(SceneName);

            SceneContext sceneContext = FindSceneContext();
            AsyncStartupProbe probe = FindProbe(SceneManager.GetSceneByName(SceneName), "ActiveRoot");

            Assert.That(!sceneContext.Initialized);
            Assert.That(!sceneContext.IsInitializing);
            Assert.That(!sceneContext.HasInstalled, "The scene must not be installed after a failed startup routine");
            Assert.That(!probe.gameObject.activeSelf, "The scene must stay inactive after a failed startup routine");
            Assert.That(!probe.HasAwoken);
        }

        [UnityTest]
        public IEnumerator TestPreResolveRoutineExceptionResetsProjectContextState()
        {
            ProjectContext.PreResolveRoutine = ThrowAfterOneFrame;
            LogAssert.Expect(LogType.Exception, new Regex("Async startup test failure"));

            yield return LoadScene(SceneName);

            Assert.That(!FindSceneContext().Initialized);
            Assert.DoesNotThrow(() =>
            {
                ProjectContext unused = ProjectContext.Instance;
            }, "ProjectContext must not report that it is still waiting after its routine failed");
        }

        static IEnumerator WaitFrames(string name, int frames)
        {
            AsyncStartupLog.Add(name + ".Start");

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            AsyncStartupLog.Add(name + ".End");
        }

        static IEnumerator ThrowAfterOneFrame()
        {
            yield return null;

            throw new InvalidOperationException("Async startup test failure");
        }

        static void AssertOrder(params string[] expectedEvents)
        {
            int previousIndex = -1;

            foreach (string expectedEvent in expectedEvents)
            {
                int index = AsyncStartupLog.Events.IndexOf(expectedEvent);

                Assert.That(index >= 0, "Missing event '" + expectedEvent + "' in: " + string.Join(", ", AsyncStartupLog.Events));
                Assert.That(index > previousIndex, "Event '" + expectedEvent + "' is out of order in: " + string.Join(", ", AsyncStartupLog.Events));

                previousIndex = index;
            }
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            return scene.GetRootGameObjects().Single(root => root.name == name);
        }

        static AsyncStartupProbe FindProbe(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<AsyncStartupProbe>(true))
                .Single(probe => probe.name == name);
        }

        static SceneContext FindSceneContext()
        {
            return SceneManager.GetSceneByName(SceneName)
                .GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<SceneContext>(true))
                .Single();
        }

        static void BindProjectInitializable()
        {
            ProjectContext.Instance.Container.BindInterfacesTo<ProjectInitializableProbe>().AsSingle();
        }

        static void OnProjectPostInstall()
        {
            AsyncStartupLog.Add("ProjectContext.PostInstall");
        }

        static void OnProjectPreResolve()
        {
            AsyncStartupLog.Add("ProjectContext.PreResolve");
        }

        static void OnProjectPostResolve()
        {
            AsyncStartupLog.Add("ProjectContext.PostResolve");
        }
    }
}

namespace Zenject.Tests
{
    public class ProjectInitializableProbe : IInitializable
    {
        public void Initialize()
        {
            AsyncStartupLog.Add("ProjectInitializable.Initialize");
        }
    }

    public class SceneInitializableProbe : IInitializable
    {
        public void Initialize()
        {
            AsyncStartupLog.Add("SceneInitializable.Initialize");
        }
    }
}
