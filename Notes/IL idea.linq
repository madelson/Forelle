<Query Kind="Program">
  <Namespace>System.Resources</Namespace>
</Query>

#nullable enable

void Main()
{
	var program = new ILProgram(
		new ILFunc("helper", new ILParameter[] { new("a", "int"), new("b", "bool") }, new ILIf(
			new ILName("b"),
			new ILReturn(new ILBinOp(new ILName("a"), ILBinaryOperator.Multiply, new ILLiteral(10))),
			new ILReturn(new ILBinOp(new ILName("a"), ILBinaryOperator.Add, new ILLiteral(1)))
		)),
		new ILFunc("main", Array.Empty<ILParameter>(), new ILBlock(new ILStatement[] 
		{
			new ILVar("a", "int", new ILLiteral(3)), // a = 3
			new ILSet(new ILName("a"), new ILCall("helper", new ILExpression[] { new ILName("a"), new ILLiteral(true) })), // a = a * 10 = 30
			new ILSet(new ILName("a"), new ILCall("helper", new ILExpression[] { new ILName("a"), new ILLiteral(false) })), // a = a + 1 = 31
			new ILReturn(new ILName("a")),
		}))
	);
	Run(program).Dump();
}

internal static object? Run(ILProgram program)
{	
	Stack<Scope> scopes = new();
	scopes.Push(new("<ROOT>"));
	AddDeclarations(program.Declarations);
	object? lastReturnValue = null;
	bool returned = false;
	return Eval(new ILCall(new ILName("main"), Array.Empty<ILExpression>()));
	
	void AddDeclarations(IEnumerable<ILDeclaration> declarations)
	{
		foreach (var declaration in declarations)
		{
			if (declaration is ILField field)
			{
				var initialValue = Eval(field.InitialValue);
				scopes.Peek().Add(declaration.Name, initialValue);			
			}
			else
			{
				scopes.Peek().Add(declaration.Name, declaration);
			}
		}
	}
	
	T ResolveTyped<T>(string name) => (T)Resolve(name)!;
	
	object? Resolve(string name) => ResolveWithScope(name).Value;
	
	// TODO this is bugged because we can see locals from outer scopes
	(object? Value, Scope Scope) ResolveWithScope(string name)
	{
		foreach (var scope in scopes)
		{
			if (scope.TryGetValue(name, out var resolved)) { return (resolved, scope); }
		}
		throw new InvalidOperationException($"Cannot resolve name '{name}'");
	}
	
	T EvalTyped<T>(ILExpression expression) => (T)Eval(expression)!;
	
	object? Eval(ILExpression? expression)
	{
		switch (expression)
		{
			case null:
				return null;
			case ILLiteral literal:
				return literal.Value;
			case ILCall call:
				{
					var func = ResolveTyped<ILFunc>(call.Name);
					if (call.Arguments.Length != func.Parameters.Length) { throw new Exception("Bad arg count"); }
					var scope = new Scope($"CALL {call.Name.Name}");
					for (var i = 0; i < call.Arguments.Length; ++i)
					{
						scope.Add(func.Parameters[i].Name, Eval(call.Arguments[i]));
					}
					scopes.Push(scope);
					returned = false;
					lastReturnValue = null;
					try 
					{ 
						Run(func.Body); 
						return lastReturnValue;
					}
					finally
					{
						lastReturnValue = null;
						returned = false;
						scopes.Pop(); 
					}
				}
			case ILBinOp binOp:
				{
					T Left<T>() => EvalTyped<T>(binOp.Left);
					T Right<T>() => EvalTyped<T>(binOp.Right);
					switch (binOp.Operator)
					{
						case ILBinaryOperator.Add:
							return Left<int>() + Right<int>();
						case ILBinaryOperator.Subtract:
							return Left<int>() - Right<int>();
						case ILBinaryOperator.Divide:
							return Left<int>() / Right<int>();
						case ILBinaryOperator.Multiply:
							return Left<int>() * Right<int>();
						case ILBinaryOperator.LessThan:
							return Left<IComparable>().CompareTo(Right<IComparable>()) < 0;
						case ILBinaryOperator.LessThanOrEqual:
							return Left<IComparable>().CompareTo(Right<IComparable>()) <= 0;
						case ILBinaryOperator.Equal:
							return Equals(Eval(binOp.Left), Eval(binOp.Right));
						case ILBinaryOperator.NotEqual:
							return !Equals(Eval(binOp.Left), Eval(binOp.Right));
						case ILBinaryOperator.GreaterThanOrEqual:
							return Left<IComparable>().CompareTo(Right<IComparable>()) >= 0;
						case ILBinaryOperator.GreaterThan:
							return Left<IComparable>().CompareTo(Right<IComparable>()) > 0;
						case ILBinaryOperator.And:
							return Left<bool>() && Right<bool>();
						case ILBinaryOperator.Or:
							return Left<bool>() || Right<bool>();
						default:
							throw new InvalidOperationException(binOp.Operator.ToString());
					}
				}
			case ILElement element:
				return EvalTyped<Array>(element.Expression).GetValue(EvalTyped<int>(element.Index));
			case ILMember member:
				switch (Eval(member.Expression))
				{
					case ILObject @object:
						return @object[member.Member];
					case Array array when (member.Member == "length"):
						return array.Length;
					default:
						throw new NotSupportedException($"Cannot access member '{member.Member}'");
				}
			case ILName name:
				return Resolve(name);
			default:
				throw new NotSupportedException(expression.GetType().ToString());
		}
	}
	
	void Run(ILStatement? statement)
	{
		Debug.Assert(!returned);
		switch (statement)
		{
			case null:
				break;
			case ILBlock block:
				{
					scopes.Push(new("<BLOCK>"));
					try
					{
						foreach (var blockStatement in block.Body)
						{
							Run(blockStatement);
							if (returned) { break; }
						}
					}
					finally { scopes.Pop(); }
				}
				break;
			case ILVar @var:
				scopes.Peek().Add(@var.Name, Eval(@var.InitialValue));
				break;
			case ILWhile @while:
				while (!returned && EvalTyped<bool>(@while.Condition)) { Run(@while.Body); }
				break;
			case ILReturn @return:
				(lastReturnValue, returned) = (Eval(@return.Expression), true);
				break;
			case ILIf @if:
				Run(EvalTyped<bool>(@if.Condition) ? @if.IfTrue : @if.IfFalse);
				break;
			case ILSwitch @switch:
				{
					var expression = Eval(@switch.Expression);
					foreach (var @case in @switch.Cases)
					{
						if (Equals(expression, Eval(@case.Value)))
						{
							Run(@case.Statement);
							break;
						}
					}
				}
				break;
			case ILSet set:
				switch (set.Left)
				{
					case ILName name:
						{
							var (value, scope) = ResolveWithScope(name);
							if (value is not (ILObject or Array or int or bool))
							{
								throw new InvalidOperationException();
							}
							scope[name] = Eval(set.Right);
						}
						break;
					default:
						throw new InvalidOperationException();
				}
				break;
			default:
				throw new NotSupportedException(statement.GetType().ToString());
		}
	}
}

private class Scope : Dictionary<string, object?>
{
	public Scope(string name) 
	{
		this.Name = name;	
	}
	
	public string Name { get; }
}

private class ILObject : Dictionary<string, object?>
{
	public ILObject(ILType type)
	{
		this.Type = type;
	}
	
	public ILType Type { get; }
}

internal abstract record ILNode;

internal abstract record ILDeclaration(ILName Name) : ILNode;

internal abstract record ILType(ILName Name) : ILDeclaration(Name);

internal abstract record ILStatement : ILNode;

internal abstract record ILExpression : ILStatement;

internal sealed record ILProgram(params ILDeclaration[] Declarations) : ILNode;

internal sealed record ILName(string Name) : ILExpression
{ 
	public static implicit operator ILName(string name) => new(name);
	public static implicit operator string(ILName name) => name.Name;
}

internal sealed record ILField(ILName Name, ILName Type, ILExpression InitialValue) : ILDeclaration(Name);

internal sealed record ILEnum(ILName Name, List<ILName> Values) : ILType(Name);

internal sealed record ILRecord(ILName Name, List<ILField> Fields) : ILType(Name);

internal sealed record ILClass(ILName Name, ILDeclaration[] Members) : ILType(Name);

internal sealed record ILParameter(ILName Name, ILName Type) : ILNode;

internal sealed record ILFunc(ILName Name, ILParameter[] Parameters, ILStatement Body) : ILDeclaration(Name);

internal sealed record ILBlock(ILStatement[] Body) : ILStatement;

internal sealed record ILVar(ILName Name, ILName Type, ILExpression? InitialValue = null) : ILStatement;

internal sealed record ILWhile(ILExpression Condition, ILStatement Body) : ILStatement;

internal sealed record ILReturn(ILExpression Expression) : ILStatement;

internal sealed record ILSet(ILExpression Left, ILExpression Right) : ILStatement;

internal sealed record ILIf(ILExpression Condition, ILStatement IfTrue, ILStatement? IfFalse = null) : ILStatement;

internal sealed record ILCase(ILLiteral Value, ILStatement Statement) : ILNode;

internal sealed record ILSwitch(ILExpression Expression, ILCase[] Cases, ILStatement? Default = null) : ILStatement;

internal sealed record ILMember(ILExpression Expression, ILName Member) : ILExpression;

internal sealed record ILElement(ILExpression Expression, ILExpression Index) : ILExpression;

internal sealed record ILLiteral(object Value) : ILExpression;

internal enum ILBinaryOperator { Add, Subtract, Multiply, Divide, Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, And, Or }

internal sealed record ILBinOp(ILExpression Left, ILBinaryOperator Operator, ILExpression Right) : ILExpression;

internal sealed record ILCall(ILName Name, ILExpression[] Arguments) : ILExpression;