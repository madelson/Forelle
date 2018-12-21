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