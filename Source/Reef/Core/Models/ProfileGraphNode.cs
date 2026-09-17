namespace Reef.Core.Models;

public class ProfileGraphNode
{
    public required string Id { get; set; }
    public required string Type { get; set; } // source, preprocess, deltasync, splitoutput, emailexport, template, destination, postprocess
    public object? Config { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
