using Core.Calibration;
using Core.Runtime;
using System;
using System.Linq;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

bool exploratory = args.Any(argument =>
    string.Equals(argument, "--exploratory", StringComparison.OrdinalIgnoreCase));
string? bindingPath = null;
bool invalidArgs = false;
for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--exploratory", StringComparison.OrdinalIgnoreCase)) continue;
    if (string.Equals(args[i], "--model-input-binding", StringComparison.OrdinalIgnoreCase) && bindingPath is null &&
        i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
        bindingPath = args[++i];
    else invalidArgs = true;
}
if (invalidArgs)
{
    Console.Error.WriteLine("Usage: dotnet run --project Delphi -- [--exploratory] [--model-input-binding <reviewed-json-path>]");
    return 2;
}

try
{
    var options = new DelphiWorkflowOptions(
        Purpose: exploratory
            ? CalibrationRunPurpose.ExploratoryReplay
            : CalibrationRunPurpose.OfficialPaper) { ModelInputBindingPath = bindingPath };
    DelphiWorkflowRunResult result = await new DelphiWorkflow().RunAsync(
        options,
        Console.Out);
    return result.Succeeded ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Delphi failed: {ex.Message}");
    return 1;
}
