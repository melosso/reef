using Reef.Core.Models;

namespace Reef.Core.Services;

public static class ProfilePipelineValidator
{
    public static string? Validate(ProfileGraph graph)
    {
        var (_, sortError) = GraphExecutor.Sort(graph);
        if (sortError != null)
            return sortError;

        var counts = graph.Nodes
            .GroupBy(n => n.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        int Count(string type) => counts.TryGetValue(type, out var c) ? c : 0;

        if (Count("source") != 1)
            return "profile requires exactly one source node";

        foreach (var singleton in new[] { "preprocess", "deltasync", "postprocess", "template", "destination", "emailexport", "splitoutput" })
        {
            if (Count(singleton) > 1)
                return $"profile cannot have more than one {singleton} node";
        }

        var hasEmail = Count("emailexport") > 0;
        var hasDestination = Count("destination") > 0;
        var hasSplit = Count("splitoutput") > 0;
        var hasTemplate = Count("template") > 0;

        if (hasEmail && hasDestination)
            return "emailexport and destination are alternate output strategies and cannot both be present";

        if (!hasEmail && !hasDestination)
            return "profile needs an output strategy, add a destination or emailexport node";

        if (hasSplit && !hasDestination)
            return "splitoutput requires a destination node";

        if (hasTemplate && hasEmail)
            return "template is not used with emailexport, email profiles carry their own template on the emailexport node";

        return null;
    }
}
