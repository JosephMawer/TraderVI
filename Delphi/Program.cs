using Core.Calibration;
using Core.Runtime;
using System;
using System.Linq;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

bool exploratory = args.Any(argument =>
    string.Equals(argument, "--exploratory", StringComparison.OrdinalIgnoreCase));
if (args.Any(argument =>
    !string.Equals(argument, "--exploratory", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: dotnet run --project Delphi -- [--exploratory]");
    return 2;
}

try
{
    var options = new DelphiWorkflowOptions(
        Purpose: exploratory
            ? CalibrationRunPurpose.ExploratoryReplay
            : CalibrationRunPurpose.OfficialPaper);
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
