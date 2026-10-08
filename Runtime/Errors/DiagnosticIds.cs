namespace Ecanakli.UIDirector
{
    // Stable ids; each one needs a Troubleshooting heading in the docs.
    internal static class DiagnosticIds
    {
        public const string UnknownViewId = "UIDIRECTOR101";
        public const string ViewTypeMismatch = "UIDIRECTOR102";
        public const string MissingViewHost = "UIDIRECTOR103";
        public const string LoadFailed = "UIDIRECTOR104";
        public const string LoadTimedOut = "UIDIRECTOR105";
        public const string TransitionTimedOut = "UIDIRECTOR106";
        public const string ViewLostOutsidePackage = "UIDIRECTOR107";
        public const string DuplicateViewId = "UIDIRECTOR108";
        public const string UnknownLayer = "UIDIRECTOR109";
        public const string InputHoldTooLong = "UIDIRECTOR110";
        public const string QueueHoldTooLong = "UIDIRECTOR111";
        public const string WaitingCapReached = "UIDIRECTOR112";
        public const string OwnerCancelledOffThread = "UIDIRECTOR113";
        public const string CameraRootWithoutCamera = "UIDIRECTOR114";
        public const string SortBandOverflow = "UIDIRECTOR115";
        public const string NoEventSystem = "UIDIRECTOR116";
        public const string OnOpenThrew = "UIDIRECTOR117";
        public const string CompleteInsideOnOpen = "UIDIRECTOR118";
        public const string GateThrew = "UIDIRECTOR119";
        public const string NoProviderForAssetKey = "UIDIRECTOR120";
        public const string QueueLayerMismatch = "UIDIRECTOR126";

        // Reported by the editor validator only.
        public const string RowWithoutSource = "UIDIRECTOR121";
        public const string BackCloseOnLayerWithoutBack = "UIDIRECTOR122";
        public const string DropdownInView = "UIDIRECTOR123";
        public const string AssetKeyNotAddressable = "UIDIRECTOR124";

        // Reported by the editor validator and by the root when a catalog is registered.
        public const string RowWithEmptyId = "UIDIRECTOR125";
    }
}
