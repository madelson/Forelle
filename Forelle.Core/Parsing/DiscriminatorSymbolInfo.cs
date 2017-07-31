using System;
using System.Collections.Generic;
using System.Text;

namespace Forelle.Parsing
{
    internal sealed class DiscriminatorSymbolInfo : SyntheticSymbolInfo
    {
        public DiscriminatorSymbolInfo(NonTerminal parentDiscriminator = null)
        {
            if (parentDiscriminator != null)
            {
                if (!(parentDiscriminator.SyntheticInfo is DiscriminatorSymbolInfo parentDiscriminatorInfo))
                {
                    throw new ArgumentException("must be a discriminator", nameof(parentDiscriminator));
                }
                if (parentDiscriminatorInfo.ParentDiscriminator != null)
                {
                    throw new ArgumentException("must be a root discriminator", nameof(parentDiscriminator));
                }
            }

            this.ParentDiscriminator = parentDiscriminator;
        }

        public NonTerminal ParentDiscriminator { get; }
    }
}
