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

IDEAS 1/8/19
- Dealing with hidden left recursion through rewriting: When we have hidden left recursion (e. g. E -> A E x where A is nullable), we'd like to rewrite to remove it as something like E -> E' x { Parse as E -> A x }, E -> A' x { Parse as E -> A x }, E' -> E { Parse as A() }, A' -> (all non-nullable derivations of A) { Parse as ... }. The question is, how do we know nullable and non-nullable derivations of A? We can do it as follows: first, if A is multiple symbols, combine to one symbol. Then construct a potential parse node for each rule of A. Then repeat: if multiple nodes are nullable, there are multiple null derivations => error (this is an ambiguity error which I think we can resolve later via
rewrite if we have a resolution). If any node is null, remove it and return the rest as the non-null derivations. Else, take the one nullable node and expand it by
blowing out each symbol's rules to get a new node set. Repeat until it terminates (it eventually will because it will find the null derivation).

IDEAS 1/13/19
- We can deal with hidden left recursion at the expense of fully-ordered event-driven parsing. Let's say we have E -> N E x where N is nullable. We can split into two rules E -> E x | N! E x, where we no longer have hidden left recursion but we've lost the fact that we should be parsing an N() before the E in E -> E x. For a parser that generates an AST, this isn't an issue so long as we can annotate the rule with something that says to just pop in the empty N() (this won't mess with trivia assignment since trivia attach to tokens, not nullables). For event driven parsing it is messier we don't know we're parsing N until after we've moved on. Potentially we could solve this via a lookahead (for x). The lookahead approach for right associative is:
	- Start with E -> N E ^ E
	- Transform to E -> N() T (^ E)? where (^ E) parses as E -> N E ^ E
	- Split out to E -> N() T ^ E parse as E -> N E ^ E | T parse as {}
Left-associative and non-binary left-recursive (e. g. E -> E ++) can't use this technique (as far as I can tell) because the N() symbols need to get parsed before any other symbols.
	
- When we eventually get to our text-representation of grammars, we should have named everything. Like:
Expression: 
	Add: Expression Left Operand Plus Expression Right
This will allow us to generate a nicely-named AST
- For search-based parsing, when we've narrowed it down to just one rule in a context we shouldn't necessarily declare victory. For example, let's say we have A -> x B C D. We could immediately request solutions for B and C, but this misses the opportunity to lift. Instead, we might first look at x (it's a token so we're good), then B (which maybe we've already solved for or assumed), then C (which would be a new dependency). In that case, we can say that we have a solution that handles parsing x B and then points to a context for A -> ... C D using a rule remainder. When solving that context we could consider both lifting C and solving C.

IDEAS 1/16/19
- We could introduce lookahead predicates like what PEGs have (!e and &e) in particular. The risk is that these make grammar rewrites more challenging. We can deal with them when constructing discriminators by just passing over them (like nullable symbols) but adding them to a list to be checked at the start of the lookahead.
An advantage of these is that they let you express some disambiguations like how C# deals with generics (looking ahead for a subset of tokens)

- After our initial parser generation, we should have an optimization phase where we make improvements via transformations. For example, Pratt parsing is a very efficient way to parse arithmetic operators. Our generator should end up with something like E -> Parse E2, then (+ E)?, E2 -> Parse E1, then (* E)?, E1 -> ID | ... this should be recognizable by the pattern of (a) first action is parse another symbol, then look at the next token and take action based on that. So long as all the next tokens are distinct, then we can collapse this into a single pratt-style parsing node. Another simpler optimization is just inlining partial rule parses and other cases where there is only one solution. Another one is de-duplicating contexts that turn out to have equivalent solutions.

IDEAS 1/20/2019
- A different approach to taking on async methods is to just force a grammatical solution. The way to do this would require parallel structures for everything that can appear under a method body. This sounds terrible, but could be made less bad with generic symbols. The idea is that I can define Stmt<TExpr>. Then I can have AsyncExpr which is just "await Expr | Expr". The generic will propagate all the way up through lots of other symbols, so it is still not wonderful, but not as bad as having to declare all of expresion syntax twice.
- Another approach would just be to always parse as an await expression and then post-process awaits to calls outside the generic context

IDEAS 1/26/2019
- The hard part about error handling is deciding when to skip a token vs. insert a token. Here's a potential way of doing it: if we are looking for exactly one token and we get a mismatch => insert. If we are looking for one of a set of tokens and we get a mismatch, skip. There is also the idea of synchronizing tokens to prevent things from going too off the rails. I think this could be done by annotating points in the parser where a specific token is expected next. Whenever we hit those points we push that sync token onto a stack. When skipping, if we hit a sync token then we switch to inserting instead (until we synchronize). Since we have a stack of sync tokens, we could potentially also get a hit on a sync token higher up the stack. One thing that's unclear is whether sync tokens need to be the NEXT token vs just a token that MUST appear in the remainder of the context. Another idea is that when deciding whether to skip vs. insert we could look ahead one more token and see if the next token could follow the missing token. E. g. if we have "int a = 1 int b = 2;", then after seeing "1" we expect "," or ";". However, looking ahead one more token rules out ",". In theory we could look ahead up to N more tokens with this strategy.
- In parser searching, we sometimes try the same unsolvable context twice and do all the work again. Should we cache failed contexts in state?