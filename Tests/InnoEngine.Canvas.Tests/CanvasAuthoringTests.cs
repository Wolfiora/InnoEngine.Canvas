using System;
using System.Linq;
using System.Threading.Tasks;

using Inno.Assets;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Core.Graphs;
using Inno.Integration.MacOS.Bgfx;
using Inno.Rendering;
using Inno.Rendering.Assets;
using Inno.Rendering.Shaders;
using Xunit;
using Inno.Rendering.Assets.Authoring;

namespace Inno.Canvas.Tests;

public sealed class CanvasAuthoringTests
{
    [Fact]
    public void CanvasSourceFunctionsImportAndProduceThePremultipliedShaderGraph()
    {
        using var fixture = new CanvasFixture();
        fixture.CopyProjectAsset("Shaders/CanvasVertex.ishadersource");
        fixture.CopyProjectAsset("Shaders/CanvasFragment.ishadersource");
        using var scope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();

        ShaderFunctionAsset vertex = Assert.IsType<ShaderFunctionAsset>(loader.Load(
            AssetPath.Project("Shaders/CanvasVertex.ishadersource"), typeof(ShaderFunctionAsset)));
        ShaderFunctionAsset fragment = Assert.IsType<ShaderFunctionAsset>(loader.Load(
            AssetPath.Project("Shaders/CanvasFragment.ishadersource"), typeof(ShaderFunctionAsset)));
        Assert.Equal("Evaluate", Assert.Single(vertex.exports));
        Assert.Equal("Evaluate", Assert.Single(fragment.exports));
        GraphDocument graph = CanvasShaderTemplate.Create(
            vertex, fragment, fixture.serialization, AssetSerializationContext.Create(loader));
        ShaderDefinition definition = ShaderGraphDocument.ReadDefinition(
            graph, fixture.serialization, AssetSerializationContext.Create(loader));
        ShaderTechniqueDefinition technique = Assert.Single(definition.techniques);
        Assert.Equal(CanvasIds.materialContract, technique.contract);
        Assert.Equal(CanvasIds.premultipliedRole, Assert.Single(technique.passes).role);
        Assert.Contains(graph.nodes, node => node.id.value == "Vertex.function");
        Assert.Contains(graph.nodes, node => node.id.value == "Fragment.function");
        Assert.NotEmpty(graph.edges);
    }

    [Fact]
    public async Task BundledShaderCompilesThroughTheNativeMetalToolchain()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        using var fixture = new CanvasFixture();
        fixture.CopyProjectAsset("Shaders/CanvasVertex.ishadersource");
        fixture.CopyProjectAsset("Shaders/CanvasFragment.ishadersource");
        fixture.CopyProjectAsset("Shaders/Canvas.ishader");
        using var scope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        ShaderAsset shader = Assert.IsType<ShaderAsset>(loader.Load(
            AssetPath.Project("Shaders/Canvas.ishader"), typeof(ShaderAsset)));
        var compiler = new ShaderCompiler(new BgfxShadercToolchain(MacOSBgfxShaderProfiles.target));
        RenderTextureFormat[] formats = Enum.GetValues<RenderTextureFormat>();
        var capabilities = new GraphicsCapabilities(
            GraphicsApi.Metal,
            GraphicsCapability.None,
            new GraphicsLimits(256, 8, 8192, 16),
            formats, formats, formats, formats,
            originBottomLeft: false,
            homogeneousDepth: false);

        ShaderCompilationResult result = await compiler.CompileGraphAsync(
            shader,
            compiler.CreateTarget(capabilities),
            RenderShaderVariant.empty,
            fixture.types,
            fixture.serialization,
            AssetSerializationContext.Create(loader),
            loader);

        Assert.True(result.succeeded, string.Join("\n", result.diagnostics.Select(value => value.message)));
        Assert.Single(result.artifact!.passes);
    }

    [Fact]
    public void CanvasConfigurationKeepsLayoutInProjectSettings()
    {
        var canvas = new Canvas();
        var settings = new CanvasProjectSettings();
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.logicalPixelsPerWorldUnit = 0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.logicalPixelsPerWorldUnit = float.NaN);
        Assert.Throws<InvalidOperationException>(() => canvas.SetText("panel", "ready"));
    }
}
