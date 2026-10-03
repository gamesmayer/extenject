using System.Collections.Generic;

namespace Zenject.Tests
{
    // Ordered record of the startup events observed by the async startup tests
    public static class AsyncStartupLog
    {
        public static readonly List<string> Events = new List<string>();

        public static void Add(string eventName)
        {
            Events.Add(eventName);
        }

        public static void Clear()
        {
            Events.Clear();
        }
    }

    public class AsyncStartupMarker
    {
    }
}
