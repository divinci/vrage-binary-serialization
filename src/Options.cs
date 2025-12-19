using CommandLine;

namespace vrb;

public class Options
{
    [Option("toJson", Required = false, HelpText = "Path to the .vrb file to convert to JSON.")]
    public string? ToJsonPath { get; set; }

    [Option("fromJson", Required = false, HelpText = "Path to the .json file to convert back to .vrb.")]
    public string? FromJsonPath { get; set; }

    [Option("validate", Required = false, HelpText = "When converting to JSON, verify that the object can be re-serialized to the exact original binary.")]
    public bool Validate { get; set; }
}

