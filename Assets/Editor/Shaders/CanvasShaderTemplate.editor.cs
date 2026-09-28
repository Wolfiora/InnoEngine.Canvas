using System;
using System.Linq;

using InnoEngine.Assets;
using InnoEngine.Graphs;
using InnoEngine.Rendering;
using InnoEngine.Serialization;
using InnoEditor.Rendering.Assets;
using InnoEditor.Rendering.Shaders;
using AssetsApi = InnoEngine.Assets.Assets;

namespace Inno.Canvas;

/// <summary>
/// Creates the complete premultiplied Canvas material shader from source-local stage functions.
/// </summary>
[ShaderGraphTemplate(CanvasIds.shaderTemplate, "Canvas / Premultiplied UI")]
public sealed class CanvasShaderTemplate : ShaderGraphTemplate
{
    private const string C_VERTEX_PATH = "Shaders/CanvasVertex.ishadersource";
    private const string C_FRAGMENT_PATH = "Shaders/CanvasFragment.ishadersource";

    /// <inheritdoc />
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
    {
        ArgumentNullException.ThrowIfNull(serialization);
        ShaderFunctionAsset vertex = AssetsApi.Load<ShaderFunctionAsset>(AssetsApi.LocalPath(C_VERTEX_PATH));
        ShaderFunctionAsset fragment = AssetsApi.Load<ShaderFunctionAsset>(AssetsApi.LocalPath(C_FRAGMENT_PATH));
        return Create(vertex, fragment, serialization, context);
    }

    /// <summary>Creates a detached Canvas shader graph from explicitly resolved stage functions.</summary>
    /// <param name="vertex">The Canvas vertex function asset.</param>
    /// <param name="fragment">The Canvas fragment function asset.</param>
    /// <param name="serialization">The current authoring converter registry.</param>
    /// <param name="context">The complete owner reference context.</param>
    /// <returns>A complete graph implementing the Canvas material contract.</returns>
    public static GraphDocument Create(
        ShaderFunctionAsset vertex,
        ShaderFunctionAsset fragment,
        SerializationRegistry serialization,
        SerializationContext context)
    {
        Validate(vertex, nameof(vertex));
        Validate(fragment, nameof(fragment));
        ArgumentNullException.ThrowIfNull(serialization);

        var pass = new ShaderPassDefinition(
            "Premultiplied",
            ShaderProgramKind.Raster,
            renderState: new ShaderRenderState
            {
                cull = ShaderCullMode.None,
                depthCompare = ShaderCompareFunction.Always,
                depthWrite = false,
                blend = RenderBlendState.premultiplied,
                colorWriteMask = 15
            });
        var definition = new ShaderDefinition(
            "Canvas",
            [
                new ShaderPropertyDefinition(
                    new ShaderPropertyId("s_canvasTexture"),
                    "Canvas Texture",
                    ShaderPropertyType.Texture2D,
                    ShaderStage.Fragment,
                    default,
                    bindingOwner: ShaderPropertyBindingOwner.RenderPass)
            ],
            [],
            [pass],
            techniques:
            [
                new ShaderTechniqueDefinition(
                    new ShaderTechniqueId("default"),
                    CanvasIds.materialContract,
                    [new ShaderTechniquePass(CanvasIds.premultipliedRole, pass.name)])
            ]);
        GraphDocument graph = ShaderGraphDocument.Create(definition, serialization, context);

        GraphNodeRecord vertexOutput = StageOutput(
            "Vertex",
            ShaderStage.Vertex,
            [
                new ShaderGraphOutput
                {
                    id = "position",
                    kind = ShaderIrOutputKind.ClipPosition
                },
                new ShaderGraphOutput
                {
                    id = "uv",
                    kind = ShaderIrOutputKind.Varying,
                    semantic = "texcoord",
                    location = 0
                },
                new ShaderGraphOutput
                {
                    id = "color",
                    kind = ShaderIrOutputKind.Varying,
                    semantic = "color",
                    location = 0
                }
            ]);
        GraphNodeRecord vertexCall = Source("Vertex.function", ShaderStage.Vertex, vertex, vertexOutput);
        Connect(Input("Vertex.a_position", "float2", ShaderStage.Vertex, ShaderIrInputKind.VertexAttribute, "position"), "value", vertexCall, "input.a_position");
        Connect(Input("Vertex.a_texcoord0", "float2", ShaderStage.Vertex, ShaderIrInputKind.VertexAttribute, "texcoord", 0), "value", vertexCall, "input.a_texcoord0");
        Connect(Input("Vertex.a_color0", "float4", ShaderStage.Vertex, ShaderIrInputKind.VertexAttribute, "color", 0), "value", vertexCall, "input.a_color0");
        Connect(Input("Vertex.viewProjection", "float4x4", ShaderStage.Vertex, ShaderIrInputKind.Builtin, "view-projection"), "value", vertexCall, "input.viewProjection");
        Connect(vertexCall, "output.position", vertexOutput, "position");
        Connect(vertexCall, "output.uv", vertexOutput, "uv");
        Connect(vertexCall, "output.color", vertexOutput, "color");

        GraphNodeRecord fragmentOutput = StageOutput(
            "Fragment",
            ShaderStage.Fragment,
            [new ShaderGraphOutput { id = "color", kind = ShaderIrOutputKind.Color }]);
        GraphNodeRecord fragmentCall = Source("Fragment.function", ShaderStage.Fragment, fragment, fragmentOutput);
        Connect(Input("Fragment.uv", "float2", ShaderStage.Fragment, ShaderIrInputKind.Varying, "texcoord", 0), "value", fragmentCall, "input.uv");
        Connect(Input("Fragment.color", "float4", ShaderStage.Fragment, ShaderIrInputKind.Varying, "color", 0), "value", fragmentCall, "input.vertexColor");
        Connect(Input("Fragment.s_canvasTexture", "sampled-texture2d", ShaderStage.Fragment, ShaderIrInputKind.SampledTexture, location: 0), "value", fragmentCall, "input.s_canvasTexture");
        Connect(fragmentCall, "output.color", fragmentOutput, "color");

        return ShaderGraphPrograms.Bind(
            graph,
            pass.name,
            [vertexOutput.id, fragmentOutput.id],
            serialization,
            context);

        GraphNodeRecord StageOutput(
            string id,
            ShaderStage stage,
            ShaderGraphOutput[] outputs)
        {
            GraphNodeRecord node = Node(id, ShaderGraphDocument.outputDefinitionId, null);
            Set(node, ShaderGraphDocument.settingsKey, new ShaderGraphStageSettings
            {
                stage = stage,
                outputs = outputs
            });
            return node;
        }

        GraphNodeRecord Input(
            string id,
            string type,
            ShaderStage stage,
            ShaderIrInputKind kind,
            string semantic = "",
            int location = 0)
        {
            string stageId = stage.ToString();
            GraphNodeRecord node = Node(id, "inno.shader.stage-input", stageId);
            Set(node, ShaderGraphDocument.settingsKey, new ShaderGraphInputSettings
            {
                id = id[(id.IndexOf('.') + 1)..],
                type = new ShaderGraphType { id = type },
                kind = kind,
                semantic = semantic,
                location = location
            });
            return node;
        }

        GraphNodeRecord Source(
            string id,
            ShaderStage stage,
            ShaderFunctionAsset function,
            GraphNodeRecord output)
        {
            GraphNodeRecord node = Node(id, "inno.shader.source", output.id.value);
            Set(node, ShaderGraphDocument.stageKey, stage.ToString());
            Set(node, "sourceId", function.identity.persistentId);
            Set(node, "sourcePath", function.assetPath.ToString());
            Set(node, "function", function.exports.Single());
            return node;
        }

        GraphNodeRecord Node(string id, string definitionId, string? stage)
        {
            var node = new GraphNodeRecord(new GraphNodeId(id), definitionId)
            {
                position = new GraphPosition(
                    80f + (graph.nodes.Count % 4) * 300f,
                    80f + (graph.nodes.Count / 4) * 220f)
            };
            graph.AddNode(node);
            if (!string.IsNullOrWhiteSpace(stage))
                Set(node, ShaderGraphDocument.stageKey, stage);
            return node;
        }

        void Set<T>(GraphNodeRecord node, string key, T value)
            => node.SetValue(key, ShaderGraphDocument.Encode(value, serialization, context));

        void Connect(GraphNodeRecord source, string output, GraphNodeRecord target, string input)
            => graph.AddEdge(new GraphEdgeRecord(
                new GraphEdgeId($"{source.id.value}.{output}->{target.id.value}.{input}"),
                new GraphEndpoint(source.id, new GraphPortId(output)),
                new GraphEndpoint(target.id, new GraphPortId(input))));
    }

    private static void Validate(ShaderFunctionAsset function, string parameter)
    {
        ArgumentNullException.ThrowIfNull(function, parameter);
        if (function.isMissing
            || function.identity.persistentId == Guid.Empty
            || function.exports.Length != 1)
        {
            throw new ArgumentException(
                "Choose an imported Canvas source function containing exactly one exported function.",
                parameter);
        }
    }
}
