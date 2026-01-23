using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using Microsoft.Agents.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Microsoft.Extensions.Configuration;

// Load JSON data
string jsonFilePath = "./hole-sections.json";
string jsonContent = File.ReadAllText(jsonFilePath);

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

string apiKey =
    configuration["Parameters:chat-gh-apikey"]
    ?? configuration["CHAT_GH_APIKEY"]
    ?? throw new InvalidOperationException(
        "Missing GitHub Models API key. Set UserSecrets key 'Parameters:chat-gh-apikey' or environment variable 'CHAT_GH_APIKEY'.");

IChatClient chatClient =
    new ChatClient(
            "gpt-4o-mini",      
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri("https://models.github.ai/inference") })
        .AsIChatClient();

AIAgent analyzer = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "Analyzer",
        Instructions = "Analyze the provided hole-section JSON data and extract key information about each hole section including name, depth measurements, hole size, and wellbore details.",
        ChatOptions = new ChatOptions
        {
            Tools = [
                AIFunctionFactory.Create(ExtractHoleSectionInfo),
                AIFunctionFactory.Create(FormatSummary)
            ],
        }
    });

AIAgent summarizer = new ChatClientAgent(
    chatClient,
    new ChatClientAgentOptions
    {
        Name = "Summarizer",
        Instructions = "Create a clear and concise summary of the hole-section data, highlighting important measurements and specifications for each casing."
    });

// Create a workflow that connects analyzer to summarizer
Workflow workflow =
    AgentWorkflowBuilder
        .BuildSequential(analyzer, summarizer);

AIAgent workflowAgent = await workflow.AsAgentAsync();

var workflowResponse =
    await workflowAgent.RunAsync($"Please summarize the following hole-section JSON data:\n\n{jsonContent}");

Console.WriteLine(workflowResponse.Text);

[Description("Extracts key information from hole-section JSON data.")]
string ExtractHoleSectionInfo() => "Hole section data has been extracted and analyzed.";

[Description("Formats the summary for display.")]
string FormatSummary(string summary) =>
    $"Hole-Section Data Summary:\n\n{summary}";