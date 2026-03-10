using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Agents.Console;

public class Program
{
    private static async Task Main()
    {
        var outputLogPath = Path.Combine(AppContext.BaseDirectory, "workflow-output.txt");
        await using var outputLog = new StreamWriter(outputLogPath, append: false);
        await outputLog.WriteLineAsync($"Run started (UTC): {DateTimeOffset.UtcNow:o}");
        await outputLog.WriteLineAsync();

        System.Console.WriteLine("Loading input JSON files...");
        var holeSectionsJson = await File.ReadAllTextAsync("./data/hole-sections.json");
        var ddrJson = await File.ReadAllTextAsync("./data/ddr.json");
        var experienceJson = await File.ReadAllTextAsync("./data/experience.json");
        var nptJson = await File.ReadAllTextAsync("./data/npt.json");
        var trajectoryJson = await File.ReadAllTextAsync("./data/trajectory.json");
        var wellboresJson = await File.ReadAllTextAsync("./data/wellbores.json");


        //prompts
        var userPrompt = await File.ReadAllTextAsync("./prompt/user.txt");

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        IChatClient chatClient = AzureOpenAiChatClientFactory.Create(configuration);

        var holeSectionsAgent = CreateAnalyzer(chatClient, "HoleSections", holeSectionsJson);
        var ddrAgent = CreateAnalyzer(chatClient, "DDR", ddrJson);
        var experienceAgent = CreateAnalyzer(chatClient, "Experience", experienceJson);
        var nptAgent = CreateAnalyzer(chatClient, "NPT", nptJson);
        var trajectoryAgent = CreateAnalyzer(chatClient, "Trajectory", trajectoryJson);
        var wellboresAgent = CreateAnalyzer(chatClient, "Wellbores", wellboresJson);
        var summarizerAgent = CreateFinalSummarizer(chatClient);

        var analysisWorkflow = AgentWorkflowBuilder.BuildConcurrent([holeSectionsAgent, ddrAgent, experienceAgent, nptAgent, trajectoryAgent, wellboresAgent]);

        Log("Starting concurrent analysis (HoleSections, DDR, Experience, NPT, Trajectory, Wellbores)...");
        var analysisInputMessages = new List<ChatMessage>
        {
            new(ChatRole.User, userPrompt)
        };
        await using StreamingRun analysisRun = await InProcessExecution.StreamAsync(analysisWorkflow, analysisInputMessages);
        await analysisRun.TrySendMessageAsync(new TurnToken(emitEvents: true));

        List<ChatMessage> analysisResults = [];
        await foreach (WorkflowEvent evt in analysisRun.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent { Data: List<ChatMessage> data })
            {
                analysisResults.AddRange(data);
                Log("\n--- Concurrent analysis output ---");
                if (data.Count == 0)
                {
                    Log("(no output)");
                    continue;
                }

                for (var i = 0; i < data.Count; i++)
                {
                    var msg = data[i];
                    var author = string.IsNullOrWhiteSpace(msg.AuthorName) ? "(unknown)" : msg.AuthorName;
                    var text = string.IsNullOrWhiteSpace(msg.Text) ? "(empty)" : msg.Text;
                    Log($"[{i}] {author} ({msg.Role}):\n{text}\n");
                }
            }
        }

        Log("\nStarting summarization...");
        var summarizerWorkflow = AgentWorkflowBuilder.BuildSequential(summarizerAgent);

        var summarizerInput = string.Join("\n\n", analysisResults.Select(m => m.Text));
        var summarizerPrompt = $"""
                                Create a concise technical summary from the analysis results below.

                                Requirements:
                                - Use one paragraph per source/section.
                                - Keep terminology consistent.
                                - Do not invent values.

                                Analysis results:
                                {summarizerInput}
                                """;

        await using StreamingRun summarizerRun = await InProcessExecution.StreamAsync(summarizerWorkflow, summarizerPrompt);
        await summarizerRun.TrySendMessageAsync(new TurnToken(emitEvents: true));

        await foreach (WorkflowEvent evt in summarizerRun.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is WorkflowOutputEvent { Data: List<ChatMessage> data })
            {
                Log("\n--- Final summary ---");
                if (data.Count == 0)
                {
                    Log("(no output)");
                    break;
                }

                for (var i = 0; i < data.Count; i++)
                {
                    var msg = data[i];
                    var author = string.IsNullOrWhiteSpace(msg.AuthorName) ? "(unknown)" : msg.AuthorName;
                    var text = string.IsNullOrWhiteSpace(msg.Text) ? "(empty)" : msg.Text;
                    Log($"[{i}] {author} ({msg.Role}):\n{text}\n");
                }

                await outputLog.WriteLineAsync();
                await outputLog.WriteLineAsync($"Log written to: {outputLogPath}");
                await outputLog.FlushAsync();
                break;
            }
        }

        return;

        void Log(string message)
        {
            System.Console.WriteLine(message);
            outputLog.WriteLine(message);
        }
    }

    private static ChatClientAgent CreateAnalyzer(IChatClient client, string name, string jsonData)
        => new(
            client,
            name: $"{name}-Analyzer",
            instructions:
            $"""
             You are an expert drilling data analyst.

             Task:
             - Analyze the provided JSON payload.
             - Extract key facts and metrics.
             - Report any missing/ambiguous fields.

             Output format:
             - Title: {name}
             - Bullet points of key findings
             - If applicable, include a small table-like list for depth-related values

             JSON:
             {jsonData}
             """);

    private static ChatClientAgent CreateFinalSummarizer(IChatClient client)
        => new(
            client,
            name: "Combined-Summarizer",
            instructions:
            "Combine all analysis outputs into a concise, unified technical summary. Keep one paragraph per section and do not use invent values.");
}