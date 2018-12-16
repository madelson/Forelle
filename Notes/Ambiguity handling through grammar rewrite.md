So far, we've been thinking of handling ambiguity via making a choice at a key parser node. However, we've found cases where this breaks down because the parser node's context may be much broader than the specific ambiguity. What if instead we specified ambiguity handling as a tweak to the grammar that Forelle can guide you through?

For example, for casts precedence we have:
Unable to distinguish between the following parse trees for the sequence of symbols [""("" ID "")"" Term - Exp]:
    Exp(Term(Cast(""("" ID "")"" Exp(Term - Exp))))
    Exp(Term(Cast(""("" ID "")"" Exp(Term))) - Exp)
	
We want the answer to be that cast binds tighter: Exp(Term(Cast(""("" ID "")"" Exp(Term))) - Exp)
This could be stated as: "In the rule "Cast -> LeftParen, ID, RightParen, Exp", Exp cannot be produced using "Exp -> Term - Exp"".

Another example:
Unable to distinguish between the following parse trees for the sequence of symbols [""("" ID "")"" - Term]:
    Exp(Term(""("" Exp(Term(ID)) "")"") - Exp(Term))
    Exp(Term(""("" ID "")"" Term(- Term)))
	
We want the answer to be that you can't cast a negative. 
This could be stated as "In the rule 'Term -> ( ID ) Term', Term cannot be produced using 'Term -> - Term'".

Another example:
Unable to distiguish between the following parse trees for the sequence of symbols [ID, "(", ID, <, ID, ",", ID, >, "(", ID, ")", ")"]:
	Exp(Call(Name(ID), ArgList(Arg(
	...
	
Exp(Name(ID < GenPar(ID , GenPar) >) "(" List<Exp> ")") 	=> ID, <, ID, Comma, GenPar, >, (, List<Exp>, )
																		  ^
List<Exp>(Exp(ID Cmp(<) Exp(ID)) , List<Exp>)				=> ID, <, ID, Comma, List<Exp>
