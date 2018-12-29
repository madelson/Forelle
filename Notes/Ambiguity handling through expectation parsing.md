12/29/2018: decided to step back from this for now due to perf concerns; this is really powerful and even lets us do the palindrome grammar, but may be overkill for handling precedence ambiguities if something simpler (e. g. grammar rewrite) will do.

We should consider expectation parsing. 

The idea of this is that we always maintain a stack of the symbols that we expect to see after parsing the current symbol. Since we always start with Start<T> -> T End<T>, then the stack will initially be populated immediately with End<T>. When we are parsing a rule like TypeDecl -> CLASS Name { Members }, then we'd eat the CLASS token, and then before parsing Name we'd push }, Members, {. After parsing name, we pop {, eat that, then pop Members and parse that, then pop } and eat that.

The benefit of keeping this stack is that at any point we can do an "expectation check" where we basically try to walk the expectation stack and see if it works. For example, this would allow us to parse the palindrome grammar, because at any point the expectation stack acts as a "count" of what's left to do and can thus guide us in shifting vs. reducing.

At an ambiguity point we are rank-ordering our preferences, but we are also checking expectation on each one. This solves cast-precedence, because we prefer reduce over shift (correct) but reduce will fail the expecation check in cases where it wouldn't produce a valid parse tree. 

We can be clever with making expectation checks efficient; for example we can remember that a given check succeeds at an index and re-use that. This ensures that expectation checks nested inside other expectation checks don't duplicate work. Either we get a failure in which case the whole check stops, or we get a success in which case the whole check succeeds. That said, I do think that expectation checks will break our O(N^2) runtime bound and may even be exponential due to nesting (depending on how much memoization we want). This might be ok since they only trigger at ambiguity points; a grammar with no such points doesn't even need to track this. Also many checks will probably fail quite quickly.

One of the keys to making expectation work is that we must always know what we are doing. For example, prefix parsing burns us. Let's say we have:
E -> T
E -> T - E
Our current approach is to parse T, then think about the two remainder rules. However, what if we need to look at the expectation stack inside T? To support this, we can rewrite common prefixes in the grammar beforehand, in this case:
E -> T P
P -> empty { PARSE AS E -> T }
P -> - E { PARSE AS E -> T - E }
With this formulation, when parsing E -> T P we just push P on the expectation stack!

Discriminator prefixes are tricky, though, because upon starting the prefix we need to do something about the rest of the current discriminator, but we don't know which symbols to push. It's not quite valid to push a new Symbol that has all suffixes, because that loses the connection between prefix and suffix rules. To fix this, we can create a suffix symbol, and parse it with a special node that needs the prefix rule output in order to decide which branch to take. We can then feed the parsed rule forward as we process the expectation stack.

Example ambiguity check with cast precedence parsing ( ID ) ID - ID:

1. Start parsing Start<E> -> E End<E>
2. Push End<E> to expectation, start parsing E
3. Parse E -> T P
4. Push P to expectation, start parsing T
5. Lookahead is (, parse T -> Cast
6. Eat (, ID, ), start parsing E
7. Parse E -> T P
8. Push P to expectation, start parsing T
9. Lookahead is ID -> parse T -> ID by eating ID
10. Pop P from expectation, startin parsing P
11. Lookahead is -, now we are at an ambiguity point between P -> empty (reduce) and P -> - E (shift). Because cast binds tighter, we prefer reduce, so we try that
12. Try parse expectation (P End<E>)
13. Lookahead is -, now we are at an ambiguity point between P -> empty (reduce) and P -> - E (shift). Because cast binds tighter, we prefer reduce, so we try that
14. Try parse expectation (End<E>) => fails; lookahead is -
15. Switch to shift, parse P -> - E
16. Eat -, start parsing E
17. Lookahead ID, push P to expectation and start parsing E -> T P
18. Lookahead ID => parse T -> ID by eating ID
19. Pop P from expectation and parse it
20. Lookahead End<E> -> parse P -> empty
21. Eat End<E> => expectation check success! => roll back to 12; we are good to go with reduce
...
Parsed as E(Cast(( ID ) E(T(ID))) - E(T(ID)))

* Note that the inner rule got preference over the outer rule at the ambiguity point, which is what we want.
* Note that in step 17 we had to push P to the expectation stack in the middle of an expectation check. That means that during these checks we need to destroy the stack as we process it and then rebuild it on the way back. One easy way to do this would be to move items from the stack to another stack on the way in and shuttle them back on the way out.