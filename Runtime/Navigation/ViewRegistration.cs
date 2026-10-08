namespace Ecanakli.UIDirector
{
    /// <summary>A registration that ends with its owner; Remove() ends it earlier.</summary>
    public readonly struct ViewRegistration
    {
        private readonly RegistrationEntry _entry;

        internal ViewRegistration(RegistrationEntry entry)
        {
            _entry = entry;
        }

        /// <summary>True until the registration is removed or its owner ends.</summary>
        public bool IsActive
        {
            get { return _entry != null && _entry.IsActive; }
        }

        /// <summary>Ends the registration now; safe to call twice, from any thread, and never throws.</summary>
        public void Remove()
        {
            if (_entry != null)
                _entry.Remove();
        }
    }
}
