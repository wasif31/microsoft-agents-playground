using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

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

        IChatClient chatClient = AzureOpenAIChatClientFactory.Create(configuration);

      

        /*
        await HoleSectionAgentRunner.RunMultipleAsync(
            chatClient,
            [
                ("HoleSections", "hole-sections.json", holeSectionsJson),
                ("DSIS", "dsis-holesection.json", dsisJson),
                ("DWP", "dwp-hole-section.json", dwpJson)
            ]);
            */

        /*var workflow = HoleSectionWorkflow.Create(chatClient, holeSectionsJson, dsisJson, dwpJson);

        await WorkflowRunner.RunAndWriteOutputAsync(workflow);*/
    }
}
