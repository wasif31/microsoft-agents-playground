using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Agents.Console;

public class Program
{
    private static async Task Main()
    {
        // Load JSON files
        var holeSectionsJson = await File.ReadAllTextAsync("./hole-sections.json");
        var dsisJson = await File.ReadAllTextAsync("./dsis-holesection.json");
        var dwpJson = await File.ReadAllTextAsync("./dwp-hole-section.json");



        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var apiKey =
            configuration["Parameters:chat-gh-apikey"]
            ?? configuration["CHAT_GH_APIKEY"]
            ?? throw new InvalidOperationException("Missing API key.");

        var endpoint = configuration["ConnectionStrings:openai"] ?? throw new Exception("AZURE_OPENAI_ENDPOINT is not set.");
        var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";
        var chatClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(configuration["ConnectionStrings:AzureApiKey"] ?? string.Empty))
            .GetChatClient(deploymentName).AsIChatClient();

        /*
        IChatClient chatClient =
            new ChatClient(
                    "gpt-4o-mini",
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri("https://models.github.ai/inference")
                    })
                .AsIChatClient();*/

        /*
        await HoleSectionAgentRunner.RunMultipleAsync(
            chatClient,
            [
                ("HoleSections", "hole-sections.json", holeSectionsJson),
                ("DSIS", "dsis-holesection.json", dsisJson),
                ("DWP", "dwp-hole-section.json", dwpJson)
            ]);
            */

        // Agents
        var holeAgent = CreateAnalyzer(chatClient, "HoleSections");
        var dsisAgent = CreateAnalyzer(chatClient, "DSIS");
        var dwpAgent = CreateAnalyzer(chatClient, "DWP");

        var summarizer = CreateFinalSummarizer(chatClient);

        var startExecutor = new ConcurrentStartExecutor(holeSectionsJson,dsisJson, dwpJson);

        var aggregationExecutor = new ConcurrentAggregationExecutor();

        var finalOutput = new FinalOutputExecutor();


        var workflow = new WorkflowBuilder(startExecutor)
            .AddFanOutEdge(startExecutor, [holeAgent, dsisAgent, dwpAgent])
            .AddFanInEdge(aggregationExecutor, sources: [holeAgent, dsisAgent, dwpAgent])
            /*.AddFanOutEdge(aggregationExecutor, [summarizer])
            .AddFanInEdge(
                sources: [summarizer],
                target: finalOutput)*/
            .WithOutputFrom(aggregationExecutor)   // 🔴 THIS is what enables WorkflowOutputEvent
            .Build();


        // var aggregator = new ParallelAggregationExecutor();

        /*// Build workflow
        var workflow = new WorkflowBuilder(start)
            .AddFanOutEdge(start, [holeAgent, dsisAgent, dwpAgent])
            .AddFanInEdge([holeAgent, dsisAgent, dwpAgent], aggregator)
            .AddSequentialEdge(aggregator, summarizer)
            .WithOutputFrom(summarizer)
            .Build();*/

        // Execute the workflow in streaming mode
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, "");
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                System.Console.WriteLine(
                    $"Workflow completed with results:\n{output.Data}");
            }
        }
    }

    private static ChatClientAgent CreateAnalyzer(IChatClient client, string name)
        => new(
            client,
            name: $"{name}-Analyzer",
            instructions:
            "Analyze the provided hole-section JSON. Extract casing name, depth, hole size, and key wellbore properties.");

    private static ChatClientAgent CreateFinalSummarizer(IChatClient client)
        => new(
            client,
            name: "Combined-Summarizer",
            instructions:
            "Combine all analyzed hole-section data and produce a concise, unified technical summary.");
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
            new ChatMessage(ChatRole.User, $"DSIS JSON:\n{dsisJson}"),cancellationToken);

        await context.SendMessageAsync(
            new ChatMessage(ChatRole.User, $"DWP JSON:\n{dwpJson}"),cancellationToken);

        // Kick off all agents in parallel
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
        this._messages.AddRange(message);

        if (_messages.Count == 3)
        {
            var formattedMessages = string.Join(Environment.NewLine,
                _messages.Select(m => $"{m.AuthorName}: {m.Text}"));
            await context.YieldOutputAsync(formattedMessages, cancellationToken);
        }
    }
}


internal sealed class FinalOutputExecutor() : Executor<ChatMessage>("FinalOutputExecutor")
{
    public override async ValueTask HandleAsync(
        ChatMessage message,
        IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            await context.YieldOutputAsync(message.Text, cancellationToken: cancellationToken);
        }
    }
}

