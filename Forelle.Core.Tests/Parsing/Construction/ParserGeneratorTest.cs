using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Forelle.Parsing;
using NUnit.Framework;

namespace Forelle.Tests.Parsing.Construction
{
    using Forelle.Parsing.Construction;
    using Forelle.Parsing.Preprocessing;
    using static TestGrammar;

    public class ParserGeneratorTest
    {
        [Test]
        public void TestLL1Grammar()
        {
            var rules = new Rules
            {
                { Exp, Id },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, UnOp },
                { Exp, BinOp },

                { UnOp, PlusOrMinus, Exp },

                { BinOp, Exp, TimesOrDivide, Exp },
                { BinOp, Exp, PlusOrMinus, Exp },

                { new Rule(PlusOrMinus, new[] { Plus }, ExtendedRuleInfo.Unmapped) },
                { new Rule(PlusOrMinus, new[] { Minus }, ExtendedRuleInfo.Unmapped) },

                { new Rule(TimesOrDivide, new[] { Times }, ExtendedRuleInfo.Unmapped) },
                { new Rule(TimesOrDivide, new[] { Divide }, ExtendedRuleInfo.Unmapped) },
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { LeftParen, Id, RightParen, Plus, Minus, Id, Times, Id }, Exp);
            parser.Parsed.ToString()
                .ShouldEqual("Exp(BinOp(Exp((, Exp(ID), )), +, Exp(BinOp(Exp(UnOp(-, Exp(ID))), *, Exp(ID)))))");
        }

        /// <summary>
        /// A simple grammar that is not LL(1) due to a common set of prefix symbols
        /// in rules for the same non-terminal
        /// </summary>
        [Test]
        public void TestCommonPrefixGrammar()
        {
            var rules = new Rules
            {
                { A, Id, A, B, Plus },
                { A, Id, A, B, Minus },
                { A, Times },
                { B, SemiColon }
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            parser.Parse(new[] { Id, Id, Times, SemiColon, Plus, SemiColon, Minus }, A);
            parser.Parsed.ToString()
                .ShouldEqual("A(ID, A(ID, A(*), B(;), +), B(;), -)");
        }

        [Test]
        public void TestExpressionVsStatementListConflict()
        {
            var rules = new Rules
            {
                { Stmt, Exp, SemiColon },
                { Exp, Id },
                { Exp, OpenBracket, ExpList, CloseBracket },
                { Exp, OpenBracket, Stmt, StmtList, CloseBracket },
                { ExpList },
                { ExpList, Exp, ExpList },
                { StmtList },
                { StmtList, Stmt, StmtList }
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);

            // [];
            parser.Parse(new[] { OpenBracket, CloseBracket, SemiColon }, Stmt);
            parser.Parsed.ToString().ShouldEqual("Stmt(Exp([, List<Exp>, ]), ;)");

            // [ [ id; ] [ [] id ] ];
            parser.Parse(new[] { OpenBracket, OpenBracket, Id, SemiColon, CloseBracket, OpenBracket, OpenBracket, CloseBracket, Id, CloseBracket, CloseBracket, SemiColon }, Stmt);
            parser.Parsed.ToString().ShouldEqual("Stmt(Exp([, List<Exp>(Exp([, Stmt(Exp(ID), ;), List<Stmt>, ]), List<Exp>(Exp([, List<Exp>(Exp([, List<Exp>, ]), List<Exp>(Exp(ID), List<Exp>)), ]), List<Exp>)), ]), ;)");
        }

        // test case from https://stackoverflow.com/questions/8496065/why-is-this-lr1-grammar-not-lalr1
        [Test]
        public void TestNonLalr1()
        {
            // S->aEa | bEb | aFb | bFa
            // E->e
            // F->e

            var s = new NonTerminal("S");
            var a = new Token("a");
            var b = new Token("b");
            var e = new Token("e");

            var rules = new Rules
            {
                { s, a, E, a },
                { s, b, E, b },
                { s, a, F, b },
                { s, b, F, a },
                { E, e },
                { F, e },
            };

            var (parser, errors) = CreateParser(rules);
            Assert.IsEmpty(errors);
            
            parser.Parse(new[] { a, e, a }, s);
            
            parser.Parsed.ToString()
                .ShouldEqual("S(a, E(e), a)");
        }

        // tests parsing a non-ambiguous grammar with both generics and comparison
        [Test]
        public void TestNonAmbiguousGenericsWithComparison()
        {
            var name = new NonTerminal("Name");
            var nameListOption = new NonTerminal("Opt<List<Name>>");
            var nameList = new NonTerminal("List<Name>");
            var genericParameters = new NonTerminal("Gen");
            var optionalGenericParameters = new NonTerminal("Opt<Gen>");

            var genericsRules = new Rules
            {
                { Exp, name },
                { name, Id, optionalGenericParameters },
                { optionalGenericParameters },
                { optionalGenericParameters, genericParameters },
                { genericParameters, LessThan, nameListOption, GreaterThan },
                { nameListOption },
                { nameListOption, nameList },
                { nameList, name },
                { nameList, name, Comma, nameList }
            };

            var (genericsParser, genericsErrors) = CreateParser(genericsRules);
            Assert.IsEmpty(genericsErrors);

            genericsParser.Parse(new[] { Id, LessThan, Id, Comma, Id, GreaterThan }, Exp);
            genericsParser.Parsed.Inline(optionalGenericParameters, nameListOption)
                .Flatten(nameList)
                .ToString()
                .ShouldEqual("Exp(Name(ID, Gen(<, List<Name>(Name(ID), ,, Name(ID)), >)))");

            var genericsAndComparisonRules = new Rules(genericsRules)
            {
                { Cmp, LessThan },
                { Cmp, GreaterThan },
                { Exp, Id, Cmp, Exp },
            };

            var (genericsAndComparisonParser, genericsAndComparisonErrors) = CreateParser(genericsAndComparisonRules);
            Assert.IsEmpty(genericsAndComparisonErrors);

            // id<id<id<id>> is id < id<id<id>>
            genericsAndComparisonParser.Parse(new[] { Id, LessThan, Id, LessThan, Id, LessThan, Id, GreaterThan, GreaterThan }, Exp);
            genericsAndComparisonParser.Parsed.Inline(optionalGenericParameters, nameListOption)
                .Flatten(nameList)
                .ToString()
                .ShouldEqual("Exp(ID, Cmp(<), Exp(Name(ID, Gen(<, List<Name>(Name(ID, Gen(<, List<Name>(Name(ID)), >))), >))))");
        }

        // based on https://www.gnu.org/software/bison/manual/html_node/Mysterious-Conflicts.html#Mysterious-Conflicts
        [Test]
        public void TestBisonMysteriousConflict()
        {
            //  def: param_spec return_spec ',';
            //  param_spec:
            //  type
            //| name_list ':' type
            //;
            //  return_spec:
            //  type
            //| name ':' type
            //;
            //  type: "id";

            //  name: "id";
            //  name_list:
            //  name
            //| name ',' name_list
            //;

            NonTerminal def = new NonTerminal("def"),
                paramSpec = new NonTerminal("param_spec"),
                returnSpec = new NonTerminal("return_spec"),
                type = new NonTerminal("type"),
                nameList = new NonTerminal("name_list"),
                name = new NonTerminal("name");

            var rules = new Rules
            {
                { def, paramSpec, returnSpec, Comma },
                { paramSpec, type },
                { paramSpec, nameList, Colon, type },
                { returnSpec, type },
                { returnSpec, name, Colon, type },
                { type, Id },
                { name, Id },
                { nameList, name },
                { nameList, name, Comma, nameList },
            };

            var (parser, errors) = CreateParser(rules);

            parser.Parse(new[] { Id, Comma, Id, Colon, Id, Id, Colon, Id, Comma }, def);
            parser.Parsed.Flatten(nameList)
                .ToString()
                .ShouldEqual("def(param_spec(name_list(name(ID), ,, name(ID)), :, type(ID)), return_spec(name(ID), :, type(ID)), ,)");
        }

        private static (TestingParser parser, List<string> errors) CreateParser(Rules rules)
        {
            if (!GrammarValidator.Validate(rules, out var validationErrors))
            {
                throw new ArgumentException("Invalid grammar: " + string.Join(Environment.NewLine, validationErrors));
            }

            var withoutAliases = AliasHelper.InlineAliases(rules, AliasHelper.FindAliases(rules));
            var withoutLeftRecursion = LeftRecursionRewriter.Rewrite(withoutAliases);
            var withStartSymbols = StartSymbolAdder.AddStartSymbols(withoutLeftRecursion);

            var (nodes, errors) = ParserGenerator.CreateParser(withStartSymbols);
            return (parser: nodes != null ? new TestingParser(nodes) : null, errors);
        }
    }
}
