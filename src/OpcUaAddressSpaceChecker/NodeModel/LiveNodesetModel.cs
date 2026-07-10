using Opc.Ua;
using OpcUaAddressSpaceChecker.OpcUa;

namespace OpcUaAddressSpaceChecker.NodeModel;

/// <summary>
/// Materializes the raw type model browsed from a live OPC UA server (see
/// <see cref="LiveTypeModelBrowser"/>) into SDK <see cref="NodeState"/> objects and exposes them
/// through the same <see cref="NodesetModelIndex"/> contract used for on-disk NodeSet2 files.
/// </summary>
/// <remarks>
/// Building a <see cref="NodeStateCollection"/> is the lowest-risk integration point: every validation
/// rule already depends on <see cref="NodesetModelIndex"/> unchanged, so a live-built index exposes the
/// same supertype chains, instance declarations, and namespace map as the file-loaded index.
/// </remarks>
public static class LiveNodesetModel
{
    /// <summary>
    /// Converts a browsed live type model into a queryable <see cref="NodesetModelIndex"/>.
    /// </summary>
    public static NodesetModelIndex Build(LiveTypeModel liveTypeModel)
    {
        ArgumentNullException.ThrowIfNull(liveTypeModel);

        var namespaceUris = liveTypeModel.NamespaceUris;
        var context = new SystemContext(new NodesetTelemetryContext())
        {
            NamespaceUris = namespaceUris
        };

        var states = new Dictionary<NodeId, NodeState>();
        var collection = new NodeStateCollection();

        foreach (var node in liveTypeModel.Nodes)
        {
            if (NodeId.IsNull(node.NodeId) || states.ContainsKey(node.NodeId))
            {
                continue;
            }

            var state = CreateState(node);
            states[node.NodeId] = state;
            collection.Add(state);
        }

        foreach (var node in liveTypeModel.Nodes)
        {
            if (!states.TryGetValue(node.NodeId, out var state))
            {
                continue;
            }

            ApplyTypeAttributes(node, state);
            ApplyInstanceAttributes(node, state);
            ApplyInterfaces(node, state);
        }

        foreach (var node in liveTypeModel.Nodes)
        {
            if (node.Children.Count == 0 || !states.TryGetValue(node.NodeId, out var parentState))
            {
                continue;
            }

            foreach (var childEdge in node.Children)
            {
                if (!states.TryGetValue(childEdge.ChildId, out var childState) ||
                    childState is not BaseInstanceState childInstance ||
                    childInstance.Parent != null)
                {
                    continue;
                }

                if (!NodeId.IsNull(childEdge.ReferenceTypeId))
                {
                    childInstance.ReferenceTypeId = childEdge.ReferenceTypeId;
                }

                parentState.AddChild(childInstance);
            }
        }

        return new NodesetModelIndex(collection, namespaceUris, context);
    }

    private static NodeState CreateState(LiveTypeModelNode node)
    {
        NodeState state = node.NodeClass switch
        {
            NodeClass.ObjectType => new BaseObjectTypeState(),
            NodeClass.VariableType => new BaseDataVariableTypeState(),
            NodeClass.DataType => new DataTypeState(),
            NodeClass.ReferenceType => new ReferenceTypeState(),
            NodeClass.Variable => new BaseDataVariableState(null),
            NodeClass.Method => new MethodState(null),
            _ => new BaseObjectState(null)
        };

        state.NodeId = node.NodeId;
        state.BrowseName = string.IsNullOrEmpty(node.BrowseName?.Name)
            ? new QualifiedName(node.NodeId.ToString())
            : node.BrowseName;
        state.DisplayName = string.IsNullOrEmpty(node.DisplayName?.Text)
            ? new LocalizedText(state.BrowseName.Name)
            : node.DisplayName;
        return state;
    }

    private static void ApplyTypeAttributes(LiveTypeModelNode node, NodeState state)
    {
        if (state is BaseTypeState typeState && !NodeId.IsNull(node.SuperTypeId))
        {
            typeState.SuperTypeId = node.SuperTypeId;
        }

        if (state is BaseVariableTypeState variableType)
        {
            if (!NodeId.IsNull(node.DataType))
            {
                variableType.DataType = node.DataType;
            }

            if (node.ValueRank.HasValue)
            {
                variableType.ValueRank = node.ValueRank.Value;
            }
        }
    }

    private static void ApplyInstanceAttributes(LiveTypeModelNode node, NodeState state)
    {
        if (state is BaseInstanceState instance)
        {
            if (!NodeId.IsNull(node.TypeDefinitionId))
            {
                instance.TypeDefinitionId = node.TypeDefinitionId;
            }

            if (!NodeId.IsNull(node.ModellingRuleId))
            {
                instance.ModellingRuleId = node.ModellingRuleId;
            }
        }

        if (state is BaseVariableState variable)
        {
            if (!NodeId.IsNull(node.DataType))
            {
                variable.DataType = node.DataType;
            }

            if (node.ValueRank.HasValue)
            {
                variable.ValueRank = node.ValueRank.Value;
            }

            if (node.ArrayDimensions.Count > 0)
            {
                variable.ArrayDimensions = node.ArrayDimensions.ToArray();
            }
        }
    }

    private static void ApplyInterfaces(LiveTypeModelNode node, NodeState state)
    {
        foreach (var interfaceId in node.InterfaceIds)
        {
            if (!NodeId.IsNull(interfaceId))
            {
                state.AddReference(ReferenceTypeIds.HasInterface, false, interfaceId);
            }
        }
    }
}
