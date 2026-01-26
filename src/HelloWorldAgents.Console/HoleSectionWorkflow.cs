using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Agents.Console;

internal static class HoleSectionWorkflow
{
    internal static Workflow Create(
        IChatClient chatClient,
        string holeSectionsJson,
        string dsisJson,
        string dwpJson)
    {
        var holeAgent = CreateAnalyzer(chatClient, "HoleSections");
        var dsisAgent = CreateAnalyzer(chatClient, "DSIS");
        var dwpAgent = CreateAnalyzer(chatClient, "DWP");

        var startExecutor = new ConcurrentStartExecutor(holeSectionsJson, dsisJson, dwpJson);
        var aggregationExecutor = new ConcurrentAggregationExecutor();

        return new WorkflowBuilder(startExecutor)
            .AddFanOutEdge(startExecutor, [holeAgent, dsisAgent, dwpAgent])
            .AddFanInEdge(aggregationExecutor, sources: [holeAgent, dsisAgent, dwpAgent])
            .WithOutputFrom(aggregationExecutor)
            .Build();
    }

    private static ChatClientAgent CreateAnalyzer(IChatClient client, string name)
        => new(
            client,
            name: $"{name}-Analyzer",
            instructions:
            "Analyze the provided hole-section JSON. Extract casing name, depth, hole size, and key wellbore properties.");
}

/// <summary>
/// Executor that starts the concurrent processing by sending messages to the agents.
/// </summary>
internal sealed class ConcurrentStartExecutor(
    string holeJson,
    string dsisJson,
    string dwpJson)
    : Executor<string>("ConcurrentStartExecutor")
{
    public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, $"Informatiq JSON:\n{holeJson}"), cancellationToken);

        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, $"DSIS JSON:\n{dsisJson}"), cancellationToken);

        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, $"DWP JSON:\n{dwpJson}"), cancellationToken);

        await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Executor that aggregates the results from the concurrent agents.
/// </summary>
internal sealed class ConcurrentAggregationExecutor() : Executor<List<ChatMessage>>("ConcurrentAggregationExecutor")
{
    private readonly List<ChatMessage> _messages = [];

    public override async ValueTask HandleAsync(
        List<ChatMessage> message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _messages.AddRange(message);

        if (_messages.Count == 3)
        {
            var formattedMessages = string.Join(Environment.NewLine,
                _messages.Select(m => $"{m.AuthorName}: {m.Text}"));
            await context.YieldOutputAsync(formattedMessages, cancellationToken);
        }
    }
}
