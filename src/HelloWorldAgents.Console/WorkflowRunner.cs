using Microsoft.Agents.AI.Workflows;

namespace Agents.Console;

internal static class WorkflowRunner
{
    internal static async Task RunAndWriteOutputAsync(Workflow workflow, string input = "")
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                System.Console.WriteLine($"Workflow completed with results:\n{output.Data}");
            }
        }
    }
}
