using System.ComponentModel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Agents.Console;

internal static class HoleSectionAgentRunner
{
    public static async Task RunMultipleAsync(
        IChatClient client,
        IEnumerable<(string NamePrefix, string Label, string Json)> inputs)
    {
        var tasks = inputs
            .Select(input =>
            {
                Workflow workflow = CreateWorkflow(client, input.NamePrefix);
                return RunWorkflowAsync(workflow, input.Label, input.Json);
            })
            .ToArray();

        var results = await Task.WhenAll(tasks);

        foreach (var result in results)
        {
            System.Console.WriteLine(GetResultText(result));
            System.Console.WriteLine();
        }
    }

    public static Workflow CreateWorkflow(IChatClient client, string namePrefix)
    {
        AIAgent analyzer = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Name = $"{namePrefix}-Analyzer",
              
                ChatOptions = new ChatOptions
                {
                    Instructions = "Analyze the provided hole-section JSON data and extract key information about each hole section including name, depth measurements, hole size, and wellbore details.",
                    Tools = [
                        AIFunctionFactory.Create(ExtractHoleSectionInfo),
                        AIFunctionFactory.Create(FormatSummary)
                    ],
                }
            });

        AIAgent summarizer = new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Name = $"{namePrefix}-Summarizer",
               ChatOptions = new ChatOptions
                {
                    Instructions = "Combine all analyzed hole-section data and produce a concise, unified technical summary.",
                    Tools = [
                        AIFunctionFactory.Create(FormatSummary)
                    ],
               }
            });

        return AgentWorkflowBuilder.BuildSequential(analyzer, summarizer);
    }

    public static async Task<object> RunWorkflowAsync(Workflow workflow, string label, string json)
    {
        AIAgent agent = workflow.AsAgent();
        return await agent.RunAsync($"Please summarize the following hole-section JSON data from '{label}':\n\n{json}");
    }

    public static string GetResultText(object result)
    {
        var textProp = result.GetType().GetProperty("Text");
        return textProp?.GetValue(result) as string ?? result.ToString() ?? string.Empty;
    }

    [Description("Extracts key information from hole-section JSON data.")]
    private static string ExtractHoleSectionInfo() => "Hole section data has been extracted and analyzed.";

    [Description("Formats the summary for display.")]
    private static string FormatSummary(string summary) =>
        $"Hole-Section Data Summary:\n\n{summary}";
}
