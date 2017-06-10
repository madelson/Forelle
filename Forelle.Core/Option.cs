using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle
{
    internal struct Option<T>
    {
        private T _value;

        private Option(T value)
        {
            this._value = value;
            this.HasValue = true;
        }

        public bool HasValue { get; }
        
        public T Value
        {
            get
            {
                if (!this.HasValue)
                {
                    throw new InvalidOperationException("Option must have a value");
                }

                return this._value;
            }
        }

        public static implicit operator Option<T>(T value) => new Option<T>(value);
    }
}
