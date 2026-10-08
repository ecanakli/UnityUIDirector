using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace Ecanakli.UIDirector.DependencyInjection.Tests
{
    // A view that records the order of its [Inject] method, Awake and OnOpen, and what each of them saw.
    // On a child of the instance it is a plain injected component; only the instance's own view is opened.
    [AddComponentMenu("")]
    public sealed class InjectedTestView : ViewBehaviour
    {
        public const string InjectEvent = "Inject";
        public const string AwakeEvent = "Awake";
        public const string OnOpenEvent = "OnOpen";

        private readonly List<string> _events = new List<string>();

        public IReadOnlyList<string> Events
        {
            get { return _events; }
        }

        public InjectedTestValue Value { get; private set; }
        public InjectedTestValue ValueSeenByAwake { get; private set; }
        public InjectedTestValue ValueSeenByOnOpen { get; private set; }
        public int InjectCount { get; private set; }

        // The object's active state when the last injection ran.
        public bool ActiveInHierarchyAtInject { get; private set; }
        public bool ActiveSelfAtInject { get; private set; }

        [Inject]
        public void Construct(InjectedTestValue value)
        {
            ActiveInHierarchyAtInject = gameObject.activeInHierarchy;
            ActiveSelfAtInject = gameObject.activeSelf;
            Value = value;
            InjectCount++;
            value.InjectCount++;
            _events.Add(InjectEvent);
        }

        private void Awake()
        {
            ValueSeenByAwake = Value;
            _events.Add(AwakeEvent);
        }

        protected override void OnOpen(NoArgs args, ViewScope open)
        {
            ValueSeenByOnOpen = Value;
            _events.Add(OnOpenEvent);
        }
    }

    // The binding the container hands to InjectedTestView; counts injections across every instance.
    public sealed class InjectedTestValue
    {
        public InjectedTestValue(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public int InjectCount { get; set; }
    }
}
