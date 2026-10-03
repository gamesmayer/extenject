#if !NOT_UNITY3D

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using ModestTree.Util;
using UnityEngine;
using UnityEngine.Serialization;
using Zenject.Internal;
using UnityEngine.Events;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zenject
{
    public class SceneContext : RunnableContext
    {
        public event Action PreInstall;
        public event Action PostInstall;
        public event Action PreResolve;
        public event Action PostResolve;

        public UnityEvent OnPreInstall;
        public UnityEvent OnPostInstall;
        public UnityEvent OnPreResolve;
        public UnityEvent OnPostResolve;

        public static Action<DiContainer> ExtraBindingsInstallMethod;
        public static Action<DiContainer> ExtraBindingsLateInstallMethod;

        public static IEnumerable<DiContainer> ParentContainers;

        public static Action<SceneContext> PreInstallMethod;

        // Single-use asynchronous hook that runs after PreInstallMethod and before Install().
        // It is cleared as soon as it is read. See "Async startup" in the README.
        public static Func<SceneContext, IEnumerator> PreInstallRoutine;

        [FormerlySerializedAs("ParentNewObjectsUnderRoot")]
        [FormerlySerializedAs("_parentNewObjectsUnderRoot")]
        [Tooltip("When true, objects that are created at runtime will be parented to the SceneContext")]
        [SerializeField]
        bool _parentNewObjectsUnderSceneContext;

        [Tooltip("Optional contract names for this SceneContext, allowing contexts in subsequently loaded scenes to depend on it and be parented to it, and also for previously loaded decorators to be included")]
        [SerializeField]
        List<string> _contractNames = new List<string>();

        [Tooltip("Optional contract names of SceneContexts in previously loaded scenes that this context depends on and to which it should be parented")]
        [SerializeField]
        List<string> _parentContractNames = new List<string>();

        DiContainer _container;

        readonly List<SceneDecoratorContext> _decoratorContexts = new List<SceneDecoratorContext>();

        bool _hasInstalled;
        bool _hasResolved;

        // Async startup: pending enumerators (innermost on top) and the objects deactivated while waiting
        readonly Stack<IEnumerator> _runSteps = new Stack<IEnumerator>();
        readonly List<GameObject> _deactivatedObjects = new List<GameObject>();

        public override DiContainer Container
        {
            get { return _container; }
        }

        public bool HasResolved
        {
            get { return _hasResolved; }
        }

        public bool HasInstalled
        {
            get { return _hasInstalled; }
        }

        public bool IsValidating
        {
            get
            {
                return ProjectContext.Instance.Container.IsValidating;
            }
        }

        public IEnumerable<string> ContractNames
        {
            get { return _contractNames; }
            set
            {
                _contractNames.Clear();
                _contractNames.AddRange(value);
            }
        }

        public IEnumerable<string> ParentContractNames
        {
            get
            {
                var result = new List<string>();
                result.AddRange(_parentContractNames);
                return result;
            }
            set
            {
                _parentContractNames = value.ToList();
            }
        }

        public bool ParentNewObjectsUnderSceneContext
        {
            get { return _parentNewObjectsUnderSceneContext; }
            set { _parentNewObjectsUnderSceneContext = value; }
        }

#if UNITY_EDITOR
        // Required for disabling domain reload in enter the play mode feature. See: https://docs.unity3d.com/Manual/DomainReloading.html
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticValues()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return;
            }

            ExtraBindingsInstallMethod = null;
            ParentContainers = null;
            ExtraBindingsLateInstallMethod = null;
        }
#endif

#if UNITY_EDITOR
        protected override void ResetInstanceFields()
        {
            base.ResetInstanceFields();

            _container = null;
            _decoratorContexts.Clear();
            _hasInstalled = false;
            _hasResolved = false;
            PreInstall = null;
            PostInstall = null;
            PreResolve = null;
            PostResolve = null;
            PreInstallMethod = null;
            PreInstallRoutine = null;
            _runSteps.Clear();
            _deactivatedObjects.Clear();
        }
#endif
        protected override void Awake()
        {
            base.Awake();

#if ZEN_INTERNAL_PROFILING
            ProfileTimers.ResetAll();
            using (ProfileTimers.CreateTimedBlock("Other"))
#endif
            {
                Initialize();
            }
        }

        protected virtual void OnDestroy()
        {
            DisposeRunSteps();

            // The container doesn't exist yet if the scene is destroyed during an async startup
            if (_container != null)
            {
                _container.UnbindAll();
            }
        }

        public void Validate()
        {
            Assert.That(IsValidating);

            Install();
            Resolve();
        }

        protected override void RunInternal()
        {
            // The steps run synchronously, exactly as before, unless ProjectContext.PreResolveRoutine or
            // PreInstallRoutine is registered. Then the scene's objects are deactivated before the routine is
            // invoked (so they are still injected before their Awake()) and the run continues in a coroutine
            // for as long as the routines need to wait.
            _runSteps.Clear();
            _runSteps.Push(RunSteps());

            object yieldInstruction;
            bool isWaiting;

            try
            {
                isWaiting = AdvanceRunSteps(out yieldInstruction);
            }
            catch (Exception exception)
            {
                if (!IsInitializing)
                {
                    throw;
                }

                FailAsyncStartup(exception);
                return;
            }

            if (isWaiting)
            {
                StartCoroutine(ContinueRunAsync(yieldInstruction));
            }
        }

        IEnumerator RunSteps()
        {
            // We always want to initialize ProjectContext as early as possible
            yield return ProjectContext.EnsureIsInitializedRoutine(BeginAsyncStartup);

            if (PreInstallMethod != null)
            {
                PreInstallMethod(this);
            }

            var preInstallRoutine = PreInstallRoutine;
            PreInstallRoutine = null;

            if (preInstallRoutine != null)
            {
                BeginAsyncStartup();

                yield return preInstallRoutine(this);
            }

#if UNITY_EDITOR
            using (ProfileBlock.Start("Zenject.SceneContext.Install"))
#endif
            {
                Install();
            }

#if UNITY_EDITOR
            using (ProfileBlock.Start("Zenject.SceneContext.Resolve"))
#endif
            {
                Resolve();
            }

            if (IsInitializing)
            {
                ReactivateSceneObjects();
                CompleteDeferredRun();
            }
        }

        void BeginAsyncStartup()
        {
            if (!IsInitializing)
            {
                DeactivateSceneObjects();
                DeferRun();
            }
        }

        void FailAsyncStartup(Exception exception)
        {
            // The scene stays deactivated and the context uninitialized: it is never run half-installed.
            DisposeRunSteps();
            FailDeferredRun();
            Debug.LogException(exception, this);
        }

        // Runs the pending steps (flattening nested enumerators) until one yields something to wait for.
        // Returns false once every step has finished.
        bool AdvanceRunSteps(out object yieldInstruction)
        {
            while (_runSteps.Count > 0)
            {
                var step = _runSteps.Peek();

                if (!step.MoveNext())
                {
                    _runSteps.Pop();
                }
                else if (step.Current is IEnumerator nestedStep)
                {
                    _runSteps.Push(nestedStep);
                }
                else
                {
                    yieldInstruction = step.Current;
                    return true;
                }
            }

            yieldInstruction = null;
            return false;
        }

        IEnumerator ContinueRunAsync(object yieldInstruction)
        {
            var isWaiting = true;

            while (isWaiting)
            {
                yield return yieldInstruction;

                try
                {
                    isWaiting = AdvanceRunSteps(out yieldInstruction);
                }
                catch (Exception exception)
                {
                    FailAsyncStartup(exception);
                    yield break;
                }
            }
        }

        // Disposing the pending enumerators runs their finally blocks (e.g. ProjectContext's async state)
        void DisposeRunSteps()
        {
            while (_runSteps.Count > 0)
            {
                (_runSteps.Pop() as IDisposable)?.Dispose();
            }
        }

        void DeactivateSceneObjects()
        {
            var contextRoot = transform.root.gameObject;

            foreach (var root in ZenUtilInternal.GetRootGameObjects(gameObject.scene))
            {
                if (root != contextRoot && root.activeSelf)
                {
                    _deactivatedObjects.Add(root);
                }
            }

            foreach (Transform child in transform)
            {
                if (child.gameObject.activeSelf)
                {
                    _deactivatedObjects.Add(child.gameObject);
                }
            }

            foreach (var deactivatedObject in _deactivatedObjects)
            {
                deactivatedObject.SetActive(false);
            }
        }

        void ReactivateSceneObjects()
        {
            foreach (var deactivatedObject in _deactivatedObjects)
            {
                if (deactivatedObject != null)
                {
                    deactivatedObject.SetActive(true);
                }
            }

            _deactivatedObjects.Clear();
        }

        public override IEnumerable<GameObject> GetRootGameObjects()
        {
            return ZenUtilInternal.GetRootGameObjects(gameObject.scene);
        }

        IEnumerable<DiContainer> GetParentContainers()
        {
            var parentContractNames = ParentContractNames;

            if (parentContractNames.IsEmpty())
            {
                if (ParentContainers != null)
                {
                    var tempParentContainer = ParentContainers;

                    // Always reset after using it - it is only used to pass the reference
                    // between scenes via ZenjectSceneLoader
                    ParentContainers = null;

                    return tempParentContainer;
                }

                return new[] { ProjectContext.Instance.Container };
            }

            Assert.IsNull(ParentContainers,
                "Scene cannot have both a parent scene context name set and also an explicit parent container given");

            var parentContainers = UnityUtil.AllLoadedScenes
                .Except(gameObject.scene)
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<SceneContext>())
                .Where(sceneContext => sceneContext.ContractNames.Where(x => parentContractNames.Contains(x)).Any())
                .Select(x => x.Container)
                .ToList();

            if (!parentContainers.Any())
            {
                throw Assert.CreateException(
                    "SceneContext on object {0} of scene {1} requires at least one of contracts '{2}', but none of the loaded SceneContexts implements that contract.",
                    gameObject.name,
                    gameObject.scene.name,
                    parentContractNames.Join(", "));
            }

            return parentContainers;
        }

        List<SceneDecoratorContext> LookupDecoratorContexts()
        {
            if (_contractNames.IsEmpty())
            {
                return new List<SceneDecoratorContext>();
            }

            return UnityUtil.AllLoadedScenes
                .Except(gameObject.scene)
                .SelectMany(scene => scene.GetRootGameObjects())
                .SelectMany(root => root.GetComponentsInChildren<SceneDecoratorContext>())
                .Where(decoratorContext => _contractNames.Contains(decoratorContext.DecoratedContractName))
                .ToList();
        }

        public void Install()
        {
            Assert.That(!_hasInstalled);
            _hasInstalled = true;

            Assert.IsNull(_container);

            var parents = GetParentContainers();
            Assert.That(!parents.IsEmpty());
            Assert.That(parents.All(x => x.IsValidating == parents.First().IsValidating));

            _container = new DiContainer(parents, parents.First().IsValidating);

            // Do this after creating DiContainer in case it's needed by the pre install logic
            if (PreInstall != null)
            {
                PreInstall();
            }

            if (OnPreInstall != null)
            {
                OnPreInstall.Invoke();
            }

            Assert.That(_decoratorContexts.IsEmpty());
            _decoratorContexts.AddRange(LookupDecoratorContexts());

            if (_parentNewObjectsUnderSceneContext)
            {
                _container.DefaultParent = transform;
            }
            else
            {
                _container.DefaultParent = null;
            }

            // Record all the injectable components in the scene BEFORE installing the installers
            // This is nice for cases where the user calls InstantiatePrefab<>, etc. in their installer
            // so that it doesn't inject on the game object twice
            // InitialComponentsInjecter will also guarantee that any component that is injected into
            // another component has itself been injected
            var injectableMonoBehaviours = new List<MonoBehaviour>();
            GetInjectableMonoBehaviours(injectableMonoBehaviours);
            foreach (var instance in injectableMonoBehaviours)
            {
                _container.QueueForInject(instance);
            }

            // UIDocument is defined in the UI Elements package. In Unity 2021.1 the package was deprecated
            // in favor of including it in the UI Elements module. Earlier Unity versions already had that
            // module, but it does not include UIDocument.
#if (USE_UI_ELEMENTS_PACKAGE  && !ZENJECT_DISABLE_DEFAULT_UI_ELEMENTS_INJECTION) || (USE_UI_ELEMENTS_MODULE && UNITY_2021_1_OR_NEWER && !ZENJECT_DISABLE_DEFAULT_UI_ELEMENTS_INJECTION)
            List<GameObject> rootObjectsInScene = new List<GameObject>();
            gameObject.scene.GetRootGameObjects(rootObjectsInScene);
            for (int i = 0; i < rootObjectsInScene.Count; i++)
            {
                UIDocument[] uiDocuments = rootObjectsInScene[i].GetComponentsInChildren<UIDocument>(true);
                for (int j = 0; j < uiDocuments.Length; j++)
                {
                    uiDocuments[j].rootVisualElement.Query().ForEach(x => _container.QueueForInject(x));
                }
            }
#endif

            foreach (var decoratorContext in _decoratorContexts)
            {
                decoratorContext.Initialize(_container);
            }

            _container.IsInstalling = true;

            try
            {
                InstallBindings(injectableMonoBehaviours);
            }
            finally
            {
                _container.IsInstalling = false;
            }

            if (PostInstall != null)
            {
                PostInstall();
            }

            if (OnPostInstall != null)
            {
                OnPostInstall.Invoke();
            }
        }

        public void Resolve()
        {
            if (PreResolve != null)
            {
                PreResolve();
            }

            if (OnPreResolve != null)
            {
                OnPreResolve.Invoke();
            }

            Assert.That(_hasInstalled);
            Assert.That(!_hasResolved);
            _hasResolved = true;

            _container.ResolveRoots();

            if (PostResolve != null)
            {
                PostResolve();
            }

            if (OnPostResolve != null)
            {
                OnPostResolve.Invoke();
            }
        }

        void InstallBindings(List<MonoBehaviour> injectableMonoBehaviours)
        {
            _container.Bind(typeof(Context), typeof(SceneContext)).To<SceneContext>().FromInstance(this);
            _container.BindInterfacesTo<SceneContextRegistryAdderAndRemover>().AsSingle();

            // Add to registry first and remove from registry last
            _container.BindExecutionOrder<SceneContextRegistryAdderAndRemover>(-1);

            foreach (var decoratorContext in _decoratorContexts)
            {
                decoratorContext.InstallDecoratorSceneBindings();
            }

            InstallSceneBindings(injectableMonoBehaviours);

            _container.Bind(typeof(SceneKernel), typeof(MonoKernel))
                .To<SceneKernel>().FromNewComponentOn(gameObject).AsSingle().NonLazy();

            _container.Bind<ZenjectSceneLoader>().AsSingle();

            if (ExtraBindingsInstallMethod != null)
            {
                ExtraBindingsInstallMethod(_container);
                // Reset extra bindings for next time we change scenes
                ExtraBindingsInstallMethod = null;
            }

            // Always install the installers last so they can be injected with
            // everything above
            foreach (var decoratorContext in _decoratorContexts)
            {
                decoratorContext.InstallDecoratorInstallers();
            }

            InstallInstallers();

            foreach (var decoratorContext in _decoratorContexts)
            {
                decoratorContext.InstallLateDecoratorInstallers();
            }

            if (ExtraBindingsLateInstallMethod != null)
            {
                ExtraBindingsLateInstallMethod(_container);
                // Reset extra bindings for next time we change scenes
                ExtraBindingsLateInstallMethod = null;
            }
        }

        protected override void GetInjectableMonoBehaviours(List<MonoBehaviour> monoBehaviours)
        {
            var scene = gameObject.scene;

            ZenUtilInternal.AddStateMachineBehaviourAutoInjectersInScene(scene);
            ZenUtilInternal.GetInjectableMonoBehavioursInScene(scene, monoBehaviours);
        }

        // These methods can be used for cases where you need to create the SceneContext entirely in code
        // Note that if you use these methods that you have to call Run() yourself
        // This is useful because it allows you to create a SceneContext and configure it how you want
        // and add what installers you want before kicking off the Install/Resolve
        public static SceneContext Create()
        {
            return CreateComponent<SceneContext>(
                new GameObject("SceneContext"));
        }
    }
}

#endif
