namespace Ecanakli.UIDirector
{
    /// <summary>Why a request ended without being shown.</summary>
    public enum RejectReason
    {
        /// <summary>The view was shown, so nothing was rejected.</summary>
        None,

        /// <summary>No catalog row has the requested id.</summary>
        UnknownKey,

        /// <summary>The prefab has no view for the key's argument and result types, or no view host.</summary>
        TypeMismatch,

        /// <summary>The id is already in flight or open on a layer that does not allow it.</summary>
        Duplicate,

        /// <summary>The navigator was busy and the call refused to wait, or a waiting or sort-band limit was reached.</summary>
        Busy,

        /// <summary>A gate denied the request.</summary>
        Denied,

        /// <summary>The asset could not be loaded.</summary>
        LoadFailed,

        /// <summary>The asset did not load within the load timeout.</summary>
        LoadTimedOut,

        /// <summary>The request was withdrawn before it was shown.</summary>
        Cancelled,

        /// <summary>The view declined to open in its open hook.</summary>
        Declined,

        /// <summary>The view threw in its open hook.</summary>
        Faulted,

        /// <summary>The owner token ended before the view was shown.</summary>
        OwnerEnded,

        /// <summary>The root no longer exists.</summary>
        RootGone,

        /// <summary>The row is on another layer than the queue that presented the request.</summary>
        WrongLayer
    }
}
