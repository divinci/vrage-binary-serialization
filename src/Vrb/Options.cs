using CommandLine;
using System.Collections.Generic;

namespace Vrb;

public class Options
{
    [Value(0, MetaName = "files", Required = true, HelpText = "Input and optional output file paths.")]
    public IEnumerable<string> FilePaths { get; set; } = new List<string>();
}

