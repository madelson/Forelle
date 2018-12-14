1. 1 rule? => Just do it
2. Group by single-token lookahead => Use this if it simplifies
3. Is there a common prefix? => Parse the prefix, then consider the rest
	* What if the prefix is all tokens? Can we chose not to use the prefix or to use a shorter version of it?
4. Are we parsing a discriminator AND another discriminator is a prefix? => Use the discriminator to figure out which rule, then parse that rule
	* Why only do this for discriminators?
	* What do we do if there are multiple valid prefix options?
5. Can we create a discriminator by looking past the token (uses an existing one if available)? => Use that
6. Can we leverage an ambiguity resolution provided by the caller? => Use that

Other potential techniques:
* Sometimes we may not have an existing discriminator to serve as a prefix; can we create one artificially?
	* How do we know when to do this?
	* Do we need to try all possible combinations of prefix symbols or can we be smarter about it?
* We could try multi-token lookahead
* We could try special-case nodes with known follow (e. g. if I'm parsing S -> A b and FOLLOW(A) is a proper superset of { b }, then potentially we can gain ground by parsing a version of A with restricted follow set { b } (NOTE: our discriminators may already handle this)

Idea: more of a "search" approach where we are much more reluctant to curtail the search if there are multiple go-forward options

A challenge here is that we are often looking to re-use things we've already created, which gets tricky in a world of forked parallel search paths. We could handle this by using a notion of "heritage" for any given point along a path, and saying that you can't re-use something from a forked branch of your own heritage. More complicated is when you want to use something from one of the forks of an unrelated heritage. Perhaps the answer here is that when you do this you now have both sets of heritage which further constrains your future cross-usage. In this world, the search ends only when all symbols have a solution with compatible heritages.

Another big question is how we move the search along. For example, you may be at a decision point where you would like to re-use something existing but it doesn't exist yet; you'd like to be able to stop there and come back if that thing comes into existance (or perhaps you just create it and someone will grab it from you later). We also need the ability to decide which of the many parallel search paths to advance next. This would require some type of scoring function so that we find the simplest overall solutions (suggesting something like A* search). We may also want the ability to mark a certain heritage as "dead" and clean up everything that depends on it.

Another challenge here is to make sure our search progresses steadily towards completion instead of exploring tons of unproductive branches. We need to be good at determining which branches are most promising and exploring those first.

===============================

Example alg:

Start with a global PQ of search states (statesQueue).

Each search state has:
* Set of created synthetic symbols (discriminators)
* Mapping of node contexts => nodes
* PQ of node contexts to solve for (does this need to be a PQ? Might be better as a stack so that when we do something potentially bad we're forced to keep going with that until we fail)
* Score # indicating how many "undesirable" things we did to reach that state (e. g. not using a full prefix, introducing a prefix discriminator)
* Action => next thing to do in this state

Populate statesQueue with one initial state. The initial state's pq is populated with one node context for each start symbol
- NOTE: as part of doing this, we'll attempt a simple solve for the context (try to make an LL0 or LL1 node). The reason for this is that we want to solve for all the easy stuff BEFORE we consider forking anything

while (true)
{
	get highest priority state from statesQueue
		
	if it has an action, process that action
	
	get highest priority context from the state pq	
		
	foreach (potential action)
	{
		create new state with that action (score updated appropriately). We don't need to fully expand all actions; for example if we detect a prefix we can just create the result of using that prefix vs.
		doing something else (not using or using a partial)
		
		add new state to statesQueue
	}
}