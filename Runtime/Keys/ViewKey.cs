using System;

namespace Ecanakli.UIDirector
{
    /// <summary>Typed identity of a view: a string id plus the argument and result types.</summary>
    public readonly struct ViewKey<TArgs, TResult>
    {
        /// <summary>Creates a key for the given view id; the id must not be null, empty or whitespace only.</summary>
        public ViewKey(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("The view id must not be empty or whitespace.", nameof(id));

            Id = id;
        }

        /// <summary>The view id as written in the catalog.</summary>
        public string Id { get; }
    }

    /// <summary>Factory shortcuts for keys.</summary>
    public static class ViewKey
    {
        /// <summary>Creates a key for a view without arguments and without a result.</summary>
        public static ViewKey<NoArgs, NoResult> Create(string id)
        {
            return new ViewKey<NoArgs, NoResult>(id);
        }

        /// <summary>Creates a key for a view that takes arguments and returns no value.</summary>
        public static ViewKey<TArgs, NoResult> Create<TArgs>(string id)
        {
            return new ViewKey<TArgs, NoResult>(id);
        }

        /// <summary>Creates a key for a view that takes arguments and returns a value.</summary>
        public static ViewKey<TArgs, TResult> Create<TArgs, TResult>(string id)
        {
            return new ViewKey<TArgs, TResult>(id);
        }
    }
}
