using System;
using UnityEngine;

namespace Ecanakli.UIDirector
{
    // Sends exceptions from isolated user callbacks to the game's handler; never throws.
    internal sealed class ErrorRouter
    {
        private readonly ViewErrorHandler _handler;
        private readonly Diagnostics _diagnostics;

        public ErrorRouter(ViewErrorHandler handler, Diagnostics diagnostics)
        {
            _handler = handler;
            _diagnostics = diagnostics;
        }

        public void Route(Exception exception, ViewErrorSource source, string viewId, int requestId, UnityEngine.Object target)
        {
            var context = new ViewErrorContext(source, viewId, requestId, target);
            Route(exception, in context);
        }

        public void Route(Exception exception, in ViewErrorContext context)
        {
            if (exception == null)
                return;

            try
            {
                if (_handler != null)
                    _handler(exception, in context);
                else
                    Debug.LogException(exception, context.Target);
            }
            catch (Exception inner)
            {
                _diagnostics.ReportFault("error handler", inner);
            }
        }
    }
}
