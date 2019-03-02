using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Forelle.Tests.TestGrammar;

namespace Forelle.Tests.Parsing.Construction
{
    public class LargeGrammarTest
    {
        [Test]
        public void TestLargeGrammar()
        {
            var num = new Token("NUM");
            var goesTo = new Token("=>");
            var var = new Token("var");
            var assign = new Token("=");
            
            var ident = new NonTerminal("Ident");
            var tuple = new NonTerminal("Tuple");
            var tupleMemberBinding = new NonTerminal("TupleMemberBinding");
            var tupleMemberBindingList = new NonTerminal("List<TupleMemberBinding>");
            var expBlock = new NonTerminal("ExpBlock");
            var lambda = new NonTerminal("Lambda");
            var lambdaParameters = new NonTerminal("LambdaArgs");
            var lambdaParameterList = new NonTerminal("List<LambdaArg>");
            var assignment = new NonTerminal("Assignment");
            var call = new NonTerminal("Call");
            var argList = new NonTerminal("List<Arg>");

            var rules = new Rules
            {
                { StmtList, Stmt, StmtList },
                { StmtList },

                { Stmt, Exp, SemiColon },
                { Stmt, Return, Exp, SemiColon },
                { Stmt, assignment },

                { Exp, ident },
                { Exp, num },
                { Exp, LeftParen, Exp, RightParen },
                { Exp, Exp, Times, Exp },
                { Exp, Exp, Plus, Exp },
                { Exp, tuple },
                { Exp, expBlock },
                { Exp, lambda },
                { Exp, call },

                { ident, Id },
                { ident, var },

                { tuple, LeftParen, tupleMemberBindingList, RightParen },

                { tupleMemberBindingList, tupleMemberBinding, Comma, tupleMemberBindingList },
                { tupleMemberBindingList, tupleMemberBinding },
                { tupleMemberBindingList },

                { tupleMemberBinding, ident, Colon, Exp },

                { expBlock, LeftParen, Stmt, StmtList, RightParen },

                { lambda, lambdaParameters, goesTo, Exp },

                { lambdaParameters, ident },
                { lambdaParameters, LeftParen, lambdaParameterList, RightParen },

                { lambdaParameterList },
                { lambdaParameterList, ident, Comma, lambdaParameterList },
                { lambdaParameterList, ident },

                { assignment, var, ident, assign, Exp, SemiColon },

                { call, Exp, LeftParen, argList, RightParen },

                { argList },
                { argList, Exp, Comma, argList },
                { argList, Exp },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);

            const string Code = @"
                var a = 2;
                var func = i => (var x = i + a; return x;);
                var t = (z: a, y: func);
                func(77);
            ";
            var tokens = Lex(Code, rules);

            Assert.DoesNotThrow(() => parser.Parse(tokens, StmtList));

            var (parser2, errors2) = ParserGeneratorTest.CreateParser2(rules);
            Assert.IsEmpty(errors2);

            parser2.Parse(tokens, StmtList)
                .ToString()
                .ShouldEqual("List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(NUM) ;)) List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(Lambda(LambdaArgs(Ident(ID)) => Exp(ExpBlock('(' Stmt(Assignment(var Ident(ID) = Exp(Exp(Ident(ID)) + Exp(Ident(ID))) ;)) List<Stmt>(Stmt(return Exp(Ident(ID)) ;) List<Stmt>()) ')')))) ;)) List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(Tuple('(' List<TupleMemberBinding>(TupleMemberBinding(Ident(ID) : Exp(Ident(ID))) , List<TupleMemberBinding>(TupleMemberBinding(Ident(ID) : Exp(Ident(ID))))) ')')) ;)) List<Stmt>(Stmt(Exp(Call(Exp(Ident(ID)) '(' List<Arg>(Exp(NUM)) ')')) ;) List<Stmt>()))))");

            var peg = new TestingGraphPegParserInterpreter(rules);

            peg.Parse(tokens, StmtList)
                .ToString()
                .ShouldEqual("List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(NUM) ;)) List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(Lambda(LambdaArgs(Ident(ID)) => Exp(ExpBlock('(' Stmt(Assignment(var Ident(ID) = Exp(Exp(Ident(ID)) + Exp(Ident(ID))) ;)) List<Stmt>(Stmt(return Exp(Ident(ID)) ;) List<Stmt>()) ')')))) ;)) List<Stmt>(Stmt(Assignment(var Ident(ID) = Exp(Tuple('(' List<TupleMemberBinding>(TupleMemberBinding(Ident(ID) : Exp(Ident(ID))) , List<TupleMemberBinding>(TupleMemberBinding(Ident(ID) : Exp(Ident(ID))))) ')')) ;)) List<Stmt>(Stmt(Exp(Call(Exp(Ident(ID)) '(' List<Arg>(Exp(NUM)) ')')) ;) List<Stmt>()))))");
        }

        private static List<Token> Lex(string code, IReadOnlyList<Rule> rules)
        {
            var allTokens = rules.SelectMany(r => r.Symbols)
                .OfType<Token>()
                .Distinct()
                .ToDictionary(t => t.Name);

            var parts = Regex.Split(code, @"\s+");
            var result = new List<Token>();
            foreach (var part in parts)
            {
                var remaining = part;
                while (remaining.Length > 0)
                {
                    var identMatch = Regex.Match(remaining, @"^[^\W\d_]\w*");
                    if (identMatch.Success)
                    {
                        result.Add(allTokens.TryGetValue(identMatch.Value, out var keyword) ? keyword : allTokens["ID"]);
                        remaining = remaining.Substring(startIndex: identMatch.Length);
                    }
                    else
                    {
                        var numMatch = Regex.Match(remaining, @"^\d+");
                        if (numMatch.Success)
                        {
                            result.Add(allTokens["NUM"]);
                            remaining = remaining.Substring(startIndex: numMatch.Length);
                        }
                        else
                        {
                            var token = allTokens.Values.Where(t => remaining.StartsWith(t.Name))
                                .OrderByDescending(t => t.Name.Length)
                                .First();
                            result.Add(token);
                            remaining = remaining.Substring(startIndex: token.Name.Length);
                        }
                    }
                }
            }

            return result;
        }
    }
}
