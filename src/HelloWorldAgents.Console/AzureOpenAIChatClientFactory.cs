using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace Agents.Console;

internal static class AzureOpenAIChatClientFactory
{
    public static IChatClient Create(IConfiguration configuration)
    {
        string endpoint =
            configuration["AzureOpenAI:Endpoint"]
            ?? configuration["ConnectionStrings:openai"]
            ?? configuration["AZURE_OPENAI_ENDPOINT"]
            ?? throw new InvalidOperationException("Missing Azure OpenAI endpoint. Set 'AzureOpenAI:Endpoint' or 'ConnectionStrings:openai' or env var 'AZURE_OPENAI_ENDPOINT'.");

        string deploymentName =
            configuration["AzureOpenAI:DeploymentName"]
            ?? configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
            ?? "gpt-5.2";

        string apiKey =
            configuration["AzureOpenAI:ApiKey"]
            ?? configuration["ConnectionStrings:AzureApiKey"]
            ?? configuration["AZURE_OPENAI_API_KEY"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Missing Azure OpenAI API key. Set 'AzureOpenAI:ApiKey' or 'ConnectionStrings:AzureApiKey' or env var 'AZURE_OPENAI_API_KEY'.");

        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        return client.GetChatClient(deploymentName).AsIChatClient();
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
    }
}
