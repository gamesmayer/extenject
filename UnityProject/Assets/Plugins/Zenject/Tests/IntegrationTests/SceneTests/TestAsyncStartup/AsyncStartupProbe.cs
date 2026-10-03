using UnityEngine;

namespace Zenject.Tests
{
    // Records whether it was injected before its Awake() and when Awake() ran
    public class AsyncStartupProbe : MonoBehaviour
    {
        [Inject]
        AsyncStartupMarker _marker = null;

        public bool HasAwoken { get; private set; }

        public bool WasInjectedBeforeAwake { get; private set; }

        public int AwakeFrame { get; private set; }

        public void Awake()
        {
            HasAwoken = true;
            WasInjectedBeforeAwake = _marker != null;
            AwakeFrame = Time.frameCount;
            AsyncStartupLog.Add("Probe.Awake:" + name);
        }
    }
}
