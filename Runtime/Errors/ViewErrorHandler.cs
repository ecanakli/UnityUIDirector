using System;

namespace Ecanakli.UIDirector
{
    /// <summary>Receives every exception thrown by user code that the package isolated.</summary>
    public delegate void ViewErrorHandler(Exception exception, in ViewErrorContext context);
}
