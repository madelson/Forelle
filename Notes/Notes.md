Big items currently:
* Parsing that respects ambiguity resolutions
* Faster unification that can handle the generics case
* Handling discriminator patterns that don't self-prefix

Interesting article on good parser generator properties & error handling: https://matklad.github.io/2018/06/06/modern-parser-generator.html

Scannerless parsing is an interesting application; it can power things like parsing interpolated strings or mixes of multiple languages really well. Challenges are in having to put things like whitespace and differentiating "for" and "fort" into the grammar. 
=> A solution to this could be declaring symbols explicitly as tokens/trivia. These would come along with the requisite behavior, e. g. tokens match leftmost-longest, trivia get lumped with adjacent tokens, etc.

IDEAS 12/20/18
- we should do a search approach for each discriminator hierarchy only. This means no sharing discriminators across hierarchies, but that always seemed unreliable / wouldn't add anything
	- as part of search, we should allow for considering a "lookpast" node rather than a discriminator. This can look past some symbols but won't actually differentiate which one was parsed. This basically relaxes the constraint discriminators have today where all post-token suffixes must uniquely identify with one source rule
	
IDEAS 12/27/18
- we have a generalization of precedence ambiguities E -> a E b and E -> a E c where b is nullable (a can also have nullable symbols on both sides); We can find such ambiguities even separated by layers of abstraction through an expansion approach by considering N recursive rules of a single symbol together (e. g. E -> e + +, E -> - E, E -> E + E, E -> E * E). In this case, we can find the pattern via expansions: E -> E(- E) ++, E -> - E, E -> E(- E) - E, etc.)
- I think that ambiguities of this form can be solved via rewrite just like we do for left-recursion
	- need the notion of precedence. This starts with total order, but I like the idea that it can also be specified manually in reference to another rule, for example:
		E -> Add: E + E, Subtract: E - E { Precedence Add } (unclear how easy it is to support this vs. just forcing AddOrSubtract -> + | -)
	- to support this, we can have a precedence object with a +1, -1, +1/2, and -1/2 operators to create rules just above/just below, etc.
- the other big thought here is that while we can rewrite this and left-recursive things (left-recursion at least is important for lookahead grammar; unclear if non-recursive precedence is), for our primary parse of that rule set we can use an operator precedence parser (pratt) parser instead. Similarly we need special support for list handling => fits the theme of "write the parser like a person would!"
- pratt relies on passing down minPrecedence. Sometimes, though, we want to reset this (e. g. when doing an E -> ( E ) rule). HOW DO WE DECIDE?

- pretty unrelated, but we should support generating incremental parsers. This isn't that hard with the right syntax tree metadata, since basically we just reparse using the existing tree as a "cache". This does require a separate incremental lexer with the ability to jump forward by multiple tokens in one go (although that should be easier to deal with and can use a similar approach). One tricky thing with incremental parsing is our variables for context keywords; we need to leverage these to determine a potentially broader scope in which changes take effect, as well as have an efficient way to track variable states from the cache tree.

IDEAS 1/3/19
- Based on the ExpectFailure_TestHigherLevelLookaheadRequired grammar, a transformation we can do would be to introduce 2 new symbols B' -> ; and B'' -> nothing. Each of these symbols would parse as the original rule for B. We can then expand A to A -> B' ; and A -> B'' ;. This will force a discriminator to run from A! This could be something to try any time a nested symbol in a rule is not independently parseable.

IDEAS 1/4/19
- For a long time, I've been thinking of Forelle's core parsing algorithm as O(N^2) because you can end up looking at each token for each symbol. However, after seeing how Packrat parsing performance is analyzed, I think that this might more properly be called O(NM) where M is the number of symbols. I think this is considered linear in parsing. One thing I haven't thought through is how the nesting of the recursive descent parsing gets factored in (e. g. for one token we may make M nested calls even without having to look ahead).
