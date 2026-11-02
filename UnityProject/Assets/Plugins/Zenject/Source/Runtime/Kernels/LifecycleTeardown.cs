using System;
using System.Collections.Generic;

namespace Zenject.Internal
{
    // UnregisterEvents -> Deinitialize -> container Dispose -> container LateDispose.
    // Every step runs even if an earlier one throws; the errors are reported together.
    public static class LifecycleTeardown
    {
        public static void Run(
            EventRegistrableManager eventRegistrableManager,
            DeinitializableManager deinitializableManager,
            DisposableManager disposableManager)
        {
            var errors = new List<Exception>();

            Collect(errors, () => eventRegistrableManager?.UnregisterEvents());
            Collect(errors, () => deinitializableManager?.Deinitialize());
            Collect(errors, () => disposableManager?.Dispose());
            Collect(errors, () => disposableManager?.LateDispose());

            ThrowIfAny(errors);
        }

        public static void Collect(List<Exception> errors, Action step)
        {
            try
            {
                step();
            }
            catch (AggregateException e)
            {
                errors.AddRange(e.InnerExceptions);
            }
            catch (Exception e)
            {
                errors.Add(e);
            }
        }

        public static void ThrowIfAny(List<Exception> errors)
        {
            if (errors.Count > 0)
            {
                throw new AggregateException(errors);
            }
        }
    }
}
