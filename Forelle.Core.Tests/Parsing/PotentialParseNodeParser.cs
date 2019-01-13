using Forelle.Parsing;
using Forelle.Tests.Parsing.Construction;
using Medallion.Collections;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Forelle.Tests.Parsing
{
    /// <summary>
    /// Test helper class that can parse and bind the results of <see cref="PotentialParseNode.ToString"/>.
    /// 
    /// This class makes it easier to construct instances of <see cref="AmbiguityResolution"/>
    /// </summary>
    internal class PotentialParseNodeParser
    {
        // symbols for the grammar of node strings
        private static readonly Token LeftParen = new Token("("),
                RightParen = new Token(")"),
                RawName = new Token("RAW_NAME"),
                QuotedName = new Token("QUOTED_NAME");
        private static readonly NonTerminal Node = new NonTerminal("Node"),
                ParentNode = new NonTerminal("Parent"),
                LeafNode = new NonTerminal("Leaf"),
                Children = new NonTerminal("Children"),
                Name = new NonTerminal("Name");

        private readonly TestingParser _parser;

        private static readonly PotentialParseNodeParser Instance = new PotentialParseNodeParser();

        private PotentialParseNodeParser()
        {
            var rules = new Rules
            {
                { Node, LeafNode },
                { Node, ParentNode },

                { LeafNode, Name },
                { ParentNode, Name, LeftParen, Children, RightParen },

                { Children, Node, Children },
                { Children },

                { Name, RawName },
                { Name, QuotedName },
            };

            var (parser, errors) = ParserGeneratorTest.CreateParser(rules);
            Assert.IsEmpty(errors);
            this._parser = parser;
        }

        public static PotentialParseNode Parse(string text, IReadOnlyCollection<Rule> rules)
        {
            try
            {
                var (tokens, names) = Lex(text);

                lock (Instance._parser) // since TestingParser is not thread-safe
                {
                    Instance._parser.Parse(tokens, Node);
                    return Bind(Instance._parser.Parsed, new Queue<string>(names), rules);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse '{text}'", ex);
            }
        }

        /// <summary>
        /// Acts as a simple lexer for our language. Since <see cref="TestingParser"/> just works
        /// on <see cref="Token"/>s which have no associated text, we cheat here by just capturing
        /// the name list separately so that those names can be associated with <see cref="RawName"/>
        /// and <see cref="QuotedName"/> tokens later on
        /// </summary>
        private static (List<Token> tokens, List<string> names) Lex(string text)
        {
            var tokens = new List<Token>();
            var names = new List<string>();

            const int NoneState = 0, InNameState = 1, InQuoteNameState = 2;
            var state = NoneState;
            var currentName = new StringBuilder();
            var i = 0;
            while (i <= text.Length)
            {
                var ch = i == text.Length ? ' ' : text[i];
                switch (state)
                {
                    case NoneState:
                        switch (ch)
                        {
                            case '(': tokens.Add(LeftParen); break;
                            case ')': tokens.Add(RightParen); break;
                            case '"':
                                currentName.Clear();
                                state = InQuoteNameState;
                                break;
                            default:
                                if (!char.IsWhiteSpace(ch))
                                {
                                    currentName.Clear();
                                    currentName.Append(ch);
                                    state = InNameState;
                                }
                                break;
                        }
                        ++i;
                        break;
                    case InNameState:
                        if (ch == '(' || ch == ')' || char.IsWhiteSpace(ch))
                        {
                            names.Add(currentName.ToString());
                            tokens.Add(RawName);
                            state = NoneState;
                        }
                        else
                        {
                            currentName.Append(ch);
                            ++i;
                        }
                        break;
                    case InQuoteNameState:
                        if (ch == '"') // note: we don't currently support double quotes in names
                        {
                            names.Add(currentName.ToString());
                            tokens.Add(QuotedName);
                            state = NoneState;
                        }
                        else
                        {
                            currentName.Append(ch);
                        }
                        ++i;
                        break;
                    default:
                        throw new InvalidOperationException("should never get here");
                }
            }

            return (tokens, names);
        }

        /// <summary>
        /// Converts a <see cref="SyntaxNode"/> to a <see cref="PotentialParseNode"/> by binding 
        /// the <paramref name="names"/> to the <paramref name="rules"/>
        /// </summary>
        private static PotentialParseNode Bind(SyntaxNode syntaxNode, Queue<string> names, IReadOnlyCollection<Rule> rules)
        {
            if (syntaxNode.Symbol == Node)
            {
                return Bind(syntaxNode.Children.Single(), names, rules);
            }

            if (syntaxNode.Symbol == LeafNode)
            {
                return new PotentialParseLeafNode(BindNextName());
            }

            if (syntaxNode.Symbol == ParentNode)
            {
                var nodeType = BindNextName();
                var childrenNode = syntaxNode.Children.Single(n => n.Symbol == Children);
                var children = Traverse.Along(childrenNode, ch => ch.Children.Skip(1).SingleOrDefault())
                    .SelectMany(ch => ch.Children.Take(1))
                    .Select(n => Bind(n, names, rules))
                    .ToArray();
                return new PotentialParseParentNode(
                    rules.Single(r => r.Produced == nodeType && r.Symbols.SequenceEqual(children.Select(ch => ch.Symbol))),
                    children
                );
            }

            throw new InvalidOperationException("should never get here");

            Symbol BindNextName()
            {
                var nextName = names.Dequeue();
                return rules.SelectMany(r => new[] { r.Produced }.Concat(r.Symbols))
                    .First(r => r.Name == nextName);
            }
        }
    }
}
