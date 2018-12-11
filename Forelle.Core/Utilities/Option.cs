using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle
{
    /// <summary>
    /// A <see cref="Nullable{T}"/>-like structure that can be used
    /// with both reference and value types
    /// </summary>
    internal struct Option<T>
    {
        private T _value;
        private bool _hasValue;

        private Option(T value)
        {
            this._value = value;
            this._hasValue = true;
        }

        public bool HasValue => this._hasValue;
        
        public T Value
        {
            get
            {
                if (!this._hasValue)
                {
                    throw new InvalidOperationException("Option must have a value");
                }

                return this._value;
            }
        }

        public bool TryGetValue(out T value)
        {
            value = this._value;
            return this._hasValue;
        }

        public static implicit operator Option<T>(T value) => new Option<T>(value);
    }
}
