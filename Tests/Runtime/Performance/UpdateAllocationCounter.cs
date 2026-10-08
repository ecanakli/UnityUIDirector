using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;

namespace Ecanakli.UIDirector.Tests
{
    // Counts GC.Alloc samples on the main thread from the start of the Update phase to the end of ScriptRunBehaviourUpdate.
    // That covers UniTask's Update runners (where the built-in transitions step) and every MonoBehaviour.Update, including
    // the root's clock tick, and leaves out coroutines, so the test runner's own frame work is not measured.
    // Same recorder as Is.Not.AllocatingGCMemory.
    internal sealed class UpdateAllocationCounter : IDisposable
    {
        private readonly PlayerLoopSystem _original;
        private readonly Recorder _recorder;
        private bool _measuring;
        private bool _disposed;

        public UpdateAllocationCounter()
        {
            _recorder = Recorder.Get("GC.Alloc");
            _original = PlayerLoop.GetCurrentPlayerLoop();

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var phases = loop.subSystemList;
            var phaseIndex = Array.FindIndex(phases, phase => phase.type == typeof(UnityEngine.PlayerLoop.Update));
            if (phaseIndex < 0)
                throw new InvalidOperationException("The player loop has no Update phase.");

            var systems = new List<PlayerLoopSystem>(phases[phaseIndex].subSystemList);
            var behaviourIndex = systems.FindIndex(system => system.type == typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate));
            if (behaviourIndex < 0)
                throw new InvalidOperationException("The Update phase has no ScriptRunBehaviourUpdate.");

            systems.Insert(behaviourIndex + 1, new PlayerLoopSystem { type = typeof(EndMarker), updateDelegate = End });
            systems.Insert(0, new PlayerLoopSystem { type = typeof(BeginMarker), updateDelegate = Begin });
            phases[phaseIndex].subSystemList = systems.ToArray();
            loop.subSystemList = phases;
            PlayerLoop.SetPlayerLoop(loop);
        }

        public int Frames { get; private set; }
        public int Allocations { get; private set; }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            PlayerLoop.SetPlayerLoop(_original);
            _recorder.enabled = false;
            _measuring = false;
        }

        private void Begin()
        {
            if (_disposed)
                return;

            _recorder.enabled = false;
            _recorder.FilterToCurrentThread();
            _recorder.enabled = true;
            _measuring = true;
        }

        private void End()
        {
            if (!_measuring)
                return;

            _recorder.enabled = false;
            _recorder.CollectFromAllThreads();
            Allocations += _recorder.sampleBlockCount;
            Frames++;
            _measuring = false;
        }

        private struct BeginMarker
        {
        }

        private struct EndMarker
        {
        }
    }
}
