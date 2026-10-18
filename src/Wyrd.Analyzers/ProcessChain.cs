using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Wyrd.Analyzers;

/// <summary>A call in a process definition: its name, type arguments and arguments, as written.</summary>
internal sealed record ProcessCall(string Name, string TypeArguments, string Arguments)
{
    public override string ToString() => Name + TypeArguments + Arguments;
}

/// <summary>A step, a wait or the outcome, with the modifiers written after it (<c>.Retry(...).Undo(...)</c>).</summary>
internal sealed record ProcessStage(ProcessCall Call, IReadOnlyList<ProcessCall> Modifiers);

/// <summary>
/// A process definition read from either syntax, so it can be written back in the other one. Arguments are kept
/// verbatim: only the shape changes.
/// </summary>
internal sealed class ProcessChain
{
    private ProcessChain(ProcessCall start, IReadOnlyList<ProcessStage> stages)
    {
        Start = start;
        Stages = stages;
    }

    public ProcessCall Start { get; }

    public IReadOnlyList<ProcessStage> Stages { get; }

    /// <summary>Reads <c>Process.StartOn&lt;T&gt;(...).Then(...).Retry(...)...</c>.</summary>
    public static ProcessChain? FromFluent(ExpressionSyntax top)
    {
        var calls = new List<ProcessCall>();
        ExpressionSyntax current = top;
        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation)
        {
            var call = Call(access.Name, invocation.ArgumentList);
            if (call.Name == "StartOn" && access.Expression.ToString().EndsWith("Process", System.StringComparison.Ordinal))
            {
                calls.Reverse();
                return Group(call with { Name = "On" }, calls, stageNames: ["Then", "Await", "OnCompensated"]);
            }

            calls.Add(call);
            current = access.Expression;
        }

        return null;
    }

    /// <summary>Reads <c>Start.On&lt;T&gt;(...) &gt;&gt; Step(...).Retry(...) &gt;&gt; ...</c>.</summary>
    public static ProcessChain? FromPipe(ExpressionSyntax top)
    {
        var operands = new List<ExpressionSyntax>();
        var current = top;
        while (current is BinaryExpressionSyntax shift && shift.IsKind(SyntaxKind.RightShiftExpression))
        {
            operands.Add(shift.Right);
            current = shift.Left;
        }

        if (current is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax start } startInvocation
            || !start.Expression.ToString().EndsWith("Start", System.StringComparison.Ordinal))
        {
            return null;
        }

        operands.Reverse();
        var stages = new List<ProcessStage>();
        foreach (var operand in operands)
        {
            var calls = new List<ProcessCall>();
            var expression = operand;
            while (expression is InvocationExpressionSyntax invocation)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax access && access.Expression is InvocationExpressionSyntax)
                {
                    calls.Add(Call(access.Name, invocation.ArgumentList));
                    expression = access.Expression;
                    continue;
                }

                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax qualified => qualified.Name,
                    SimpleNameSyntax simple => simple,
                    _ => null,
                };
                if (name is null)
                {
                    return null;
                }

                calls.Reverse();
                var stage = Call(name, invocation.ArgumentList);
                stages.Add(new ProcessStage(stage.Name == "Step" ? stage with { Name = "Then" } : stage, calls));
                break;
            }
        }

        return new ProcessChain(Call(start.Name, startInvocation.ArgumentList), stages);
    }

    public string ToFluent(string indent)
    {
        var text = new StringBuilder("Process.StartOn").Append(Start.TypeArguments).Append(Start.Arguments);
        foreach (var stage in Stages)
        {
            text.Append('\n').Append(indent).Append('.').Append(stage.Call);
            foreach (var modifier in stage.Modifiers)
            {
                text.Append('.').Append(modifier);
            }
        }

        return text.ToString();
    }

    public string ToPipe(string indent)
    {
        var text = new StringBuilder("Start.On").Append(Start.TypeArguments).Append(Start.Arguments);
        foreach (var stage in Stages)
        {
            var call = stage.Call.Name == "Then" ? stage.Call with { Name = "Step" } : stage.Call;
            text.Append('\n').Append(indent).Append(">> ").Append(call);
            foreach (var modifier in stage.Modifiers)
            {
                text.Append('.').Append(modifier);
            }
        }

        return text.ToString();
    }

    private static ProcessChain Group(ProcessCall start, List<ProcessCall> calls, string[] stageNames)
    {
        var stages = new List<ProcessStage>();
        foreach (var call in calls)
        {
            if (stageNames.Contains(call.Name) || stages.Count == 0)
            {
                stages.Add(new ProcessStage(call, new List<ProcessCall>()));
            }
            else
            {
                ((List<ProcessCall>)stages[stages.Count - 1].Modifiers).Add(call);
            }
        }

        return new ProcessChain(start, stages);
    }

    private static ProcessCall Call(SimpleNameSyntax name, ArgumentListSyntax arguments) =>
        new(name.Identifier.Text, name is GenericNameSyntax generic ? generic.TypeArgumentList.ToString() : string.Empty, arguments.ToString());
}
