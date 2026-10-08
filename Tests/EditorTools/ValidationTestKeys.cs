namespace Ecanakli.UIDirector.EditorTools.Tests
{
    // Ids shared by the key classes below and the validator tests.
    internal static class ValidationIds
    {
        public const string Plain = "validation.plain";
        public const string Args = "validation.args";
        public const string Result = "validation.result";
        public const string Orphan = "validation.orphan";
        public const string ArgsMismatch = "validation.mismatch.args";
        public const string ResultMismatch = "validation.mismatch.result";
        public const string OrderAAlpha = "validation.order.a.alpha";
        public const string OrderAZeta = "validation.order.a.zeta";
        public const string OrderBAlpha = "validation.order.b.alpha";
        public const string OrderBZeta = "validation.order.b.zeta";
        public const string Unmarked = "validation.unmarked";
        public const string Internal = "validation.internal";
    }

    // A layer declared outside the package, as a game would; the validator finds it by reflection.
    internal static class ValidationTestLayers
    {
        public static readonly ViewLayer Hud = new ViewLayer("validation-hud", 3, ViewLayerRules.None);
    }

    // A non-public layer field; the validator must still know it.
    internal static class ValidationInternalLayers
    {
        internal static readonly ViewLayer Overlay = new ViewLayer("validation-internal", 4, ViewLayerRules.None);
    }

    // The key classes below carry no [ViewKeys] on purpose: tests hand them to Validate directly, and a marked
    // class with no catalog row would make the project-wide validation of this host report errors for good.

    // The only marked fixture: discovery tests look for it. It holds no keys, so the project run stays clean.
    [ViewKeys]
    internal static class ValidationKeysMarked
    {
    }

    // Holds a key but is not marked, so discovery must skip it.
    internal static class ValidationKeysUnmarked
    {
        public static readonly ViewKey<NoArgs, NoResult> Open = ViewKey.Create(ValidationIds.Unmarked);
    }

    // A key field that is not public; the validator is expected to check it too.
    internal static class ValidationKeysInternal
    {
        internal static readonly ViewKey<NoArgs, NoResult> Open = ViewKey.Create(ValidationIds.Internal);
    }

    internal static class ValidationKeysPlain
    {
        public static readonly ViewKey<NoArgs, NoResult> Open = ViewKey.Create(ValidationIds.Plain);
    }

    internal static class ValidationKeysArgs
    {
        public static readonly ViewKey<int, NoResult> Open = ViewKey.Create<int>(ValidationIds.Args);
    }

    internal static class ValidationKeysResult
    {
        public static readonly ViewKey<string, bool> Open = ViewKey.Create<string, bool>(ValidationIds.Result);
    }

    // A key whose id no catalog row has.
    internal static class ValidationKeysOrphan
    {
        public static readonly ViewKey<NoArgs, NoResult> Open = ViewKey.Create(ValidationIds.Orphan);
    }

    // Its row gets a view without arguments, so the argument type does not match.
    internal static class ValidationKeysArgsMismatch
    {
        public static readonly ViewKey<int, NoResult> Open = ViewKey.Create<int>(ValidationIds.ArgsMismatch);
    }

    // Its row gets an IView<string, bool>, so the result type does not match.
    internal static class ValidationKeysResultMismatch
    {
        public static readonly ViewKey<string, int> Open = ViewKey.Create<string, int>(ValidationIds.ResultMismatch);
    }

    // Fields are declared out of name order on purpose: the validator sorts them.
    internal static class ValidationKeysOrderA
    {
        public static readonly ViewKey<NoArgs, NoResult> Zeta = ViewKey.Create(ValidationIds.OrderAZeta);
        public static readonly ViewKey<NoArgs, NoResult> Alpha = ViewKey.Create(ValidationIds.OrderAAlpha);
    }

    internal static class ValidationKeysOrderB
    {
        public static readonly ViewKey<NoArgs, NoResult> Zeta = ViewKey.Create(ValidationIds.OrderBZeta);
        public static readonly ViewKey<NoArgs, NoResult> Alpha = ViewKey.Create(ValidationIds.OrderBAlpha);
    }
}
