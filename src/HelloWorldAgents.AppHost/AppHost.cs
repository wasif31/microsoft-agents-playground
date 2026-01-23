using Aspire.Hosting.GitHub;

var builder = DistributedApplication.CreateBuilder(args);

var model = GitHubModel.OpenAI.OpenAIGPT4oMini;

var chatDeployment = builder.AddGitHubModel("chat", model);

var api =
    builder.AddProject<Projects.Agents_API>("api")
        .WithIconName("BrainSparkle")
        .WithEnvironment("MODEL_NAME", model.Id)
        .WithReference(chatDeployment);

// Expose the sample Chat UI during dev time
if (builder.ExecutionContext.IsRunMode)
{
    api.WithUrl("/index.html", "ChatUI");
}

builder.Build().Run();