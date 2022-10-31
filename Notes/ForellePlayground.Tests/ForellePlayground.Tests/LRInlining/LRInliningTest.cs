namespace ForellePlayground.Tests.LRInlining;

public class LRInliningTest
{
    private static readonly Token ID = new("ID"), SEMI = new("SEMI"), LBRACKET = new("LBRACKET"), RBRACKET = new("RBRACKET"),
        EOF = new("EOF"), EQ = new("EQ"), TIMES = new("TIMES");
    private static readonly NonTerminal Exp = new("Exp"), Stmt = new("Stmt"), ExpList = new("List<Exp>"), StmtList = new("List<Stmt>"),
        Start = new("Start");

    [Test]
    public void TestTextbookGrammar326()
    {
        Token x = new("x");
        NonTerminal S = new("S"), V = new("V"), E = new("E");
        var rules = new Rule[]
        {
            new(Start, S, EOF),
            new(S, V, EQ, E),
            new(S, E),
            new(E, V),
            new(V, x),
            new(V, TIMES, E)
        };

        var parser = CreateParser(rules);
        parser.Parse(x, EOF).ToString()
            .ShouldEqual("Start(S(E(V(x))) EOF)");

        parser.Parse(x, EQ, TIMES, TIMES, x, EOF).ToString()
            .ShouldEqual("Start(S(V(x) EQ E(V(TIMES E(V(TIMES E(V(x))))))) EOF)");
    }

    [Test]
    public void TestExpressionVsStatementListConflict()
    {
        var rules = new Rule[]
        {
            new(Start, Stmt, EOF),
            new(Stmt, Exp, SEMI),
            new(Exp, ID),
            new(Exp, LBRACKET, ExpList, RBRACKET),
            new(Exp, LBRACKET, Stmt, StmtList, RBRACKET),
            new(ExpList),
            new(ExpList, Exp, ExpList),
            new(StmtList),
            new(StmtList, Stmt, StmtList)
        };

        var parser = CreateParser(rules);

        // [];
        parser.Parse(LBRACKET, RBRACKET, SEMI, EOF).ToString()
            .ShouldEqual("Start(Stmt(Exp(LBRACKET List<Exp>() RBRACKET) SEMI) EOF)");

        // [ [ id; ] [ [] id ] ];
        parser.Parse(LBRACKET, LBRACKET, ID, SEMI, RBRACKET, LBRACKET, LBRACKET, RBRACKET, ID, RBRACKET, RBRACKET, SEMI, EOF).ToString()
            .ShouldEqual("Start(Stmt(Exp(LBRACKET List<Exp>(Exp(LBRACKET Stmt(Exp(ID) SEMI) List<Stmt>() RBRACKET) List<Exp>(Exp(LBRACKET List<Exp>(Exp(LBRACKET List<Exp>() RBRACKET) List<Exp>(Exp(ID) List<Exp>())) RBRACKET) List<Exp>())) RBRACKET) SEMI) EOF)");
    }

    [Test]
    public void TestLR2()
    {
        NonTerminal A = new("A"), B = new("B");

        var rules = new Rule[]
        {
            new(Start, Exp, EOF),
            new(Exp, A, ID, SEMI),
            new(Exp, B, ID, TIMES),
            new(A, ID),
            new(B, ID),
        };

        var parser = CreateParser(rules);

        parser.Parse(ID, ID, SEMI, EOF).ToString()
            .ShouldEqual("Start(Exp(A(ID) ID SEMI) EOF)");

        parser.Parse(ID, ID, TIMES, EOF).ToString()
            .ShouldEqual("Start(Exp(B(ID) ID TIMES) EOF)");
    }

    private static TestingParser CreateParser(Rule[] rules)
    {
        LRGenerator generator = new(rules);
        var states = generator.Generate();
        Console.WriteLine(DebuggingHelpers.CreateLRTableHtml(states));
        return new(states);
    }
}
