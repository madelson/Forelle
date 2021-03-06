﻿using Medallion.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Forelle.Parsing
{
    public sealed class AmbiguityResolution
    {
        public AmbiguityResolution(IEnumerable<PotentialParseNode> orderedParses)
        {
            this.OrderedParses = new ReadOnlyCollection<PotentialParseNode>(Guard.NotNullOrContainsNullAndDefensiveCopy(orderedParses, nameof(orderedParses)));
            if (this.OrderedParses.Count == 0) { throw new ArgumentException("must not be empty", nameof(orderedParses)); }
        }

        public AmbiguityResolution(PotentialParseNode preferedParse, PotentialParseNode alternateParse, params PotentialParseNode[] alternateParses)
            : this(new[] { preferedParse, alternateParse }.Concat(alternateParses ?? throw new ArgumentNullException(nameof(alternateParses))))
        {
        }

        public ReadOnlyCollection<PotentialParseNode> OrderedParses { get; }

        internal PotentialParseNode PreferredParse => this.OrderedParses[0];
    }
}
