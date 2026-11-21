using Reef.Core.Nodes;
using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows;

public sealed class WorkflowIds(NodeFieldIds ids, WorkflowSecrets secrets)
{
    public WorkflowDocument ToPublic(WorkflowDocument document) => Rewrite(secrets.Mask(document), toPublic: true);

    public WorkflowDocument ToInternal(WorkflowDocument document) => Rewrite(document, toPublic: false);

    private WorkflowDocument Rewrite(WorkflowDocument document, bool toPublic)
    {
        foreach (var node in document.Nodes)
        {
            if (node.Config is { } config && WorkflowNodeCatalog.Catalog.Find(node.Type) is { } definition)
                ids.Rewrite(definition.Fields, config, toPublic);
        }
        return document;
    }
}
