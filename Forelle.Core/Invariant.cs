﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle
{
    internal static class Invariant
    {
        // todo add conditional attribute
        public static void Require(bool condition, string message = null)
        {
            if (!condition)
            {
                throw new InvariantViolatedException(message ?? "invariant violated");
            }
        }
    }
    
    internal class InvariantViolatedException : Exception
    {
        public InvariantViolatedException(string message)
            : base(message)
        {
        }
    }
}
