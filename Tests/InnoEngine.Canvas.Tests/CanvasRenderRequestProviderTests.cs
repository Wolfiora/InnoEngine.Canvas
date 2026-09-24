using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.References;
using Inno.Rendering;
using Inno.Scene;
using Inno.Text;
using Inno.UI;
using Xunit;

namespace Inno.Canvas.Tests;

public sealed class CanvasRenderRequestProviderTests
{
    [Fact]
    public void SceneRetirementReleasesUiContextAndTextureAfterTheObjectIsDestroyed()
    {
        using var fixture = new CanvasFixture();
        fixture.CopyProjectAsset("~Samples/CanvasDemo.rml");
        fixture.CopyProjectAsset("Shaders/CanvasVertex.ishadersource");
        fixture.CopyProjectAsset("Shaders/CanvasFragment.ishadersource");
        fixture.CopyProjectAsset("Shaders/Canvas.ishader");
        fixture.CopyProjectAsset("Materials/Canvas.imaterial");
        fixture.CopyProjectAsset("Pipelines/Canvas.irenderpipeline");
        using IDisposable sceneScope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        using IDisposable assetScope = fixture.EnterAssetScope(loader);
        AssetObject? loadedDocument = loader.Load(
            AssetPath.Project("~Samples/CanvasDemo.rml"), typeof(UiDocumentAsset));
        _ = loader.TryGetInfo(AssetPath.Project("~Samples/CanvasDemo.rml"), out AssetInfo? documentInfo);
        string importDetails = documentInfo is null
            ? "The RML source was not cataloged."
            : $"Status: {documentInfo.status}; Importer: {documentInfo.importerId}; "
              + string.Join(Environment.NewLine, documentInfo.diagnostics);
        Assert.True(loadedDocument is UiDocumentAsset,
            string.Join(Environment.NewLine, importDetails, fixture.diagnosticSummary));
        UiDocumentAsset document = (UiDocumentAsset)loadedDocument!;
        GameScene scene = fixture.world.LoadNewScene("Canvas Test");
        GameObject owner = scene.CreateObject("Overlay");
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.document = document;
        Assert.Equal(CanvasIds.pipeline, canvas.pipeline?.pipelineTypeId);
        Assert.NotNull(canvas.material?.shader);

        var ui = new CapturingUi();
        var requests = new CapturingRequests();
        using var provider = new CanvasRenderRequestProvider();
        using IDisposable uiScope = UiExecutionContext.EnterScope(ui);
        using ContentReadScope content = new([scene.identity]);
        var context = new RenderRequestProviderContext(
            requests,
            content,
            new GraphicsCapabilities(
                GraphicsApi.Noop,
                GraphicsCapability.Index32,
                new GraphicsLimits(64, 1, 4096, 0),
                [RenderTextureFormat.RGBA8],
                [RenderTextureFormat.RGBA8],
                [],
                [],
                originBottomLeft: false,
                homogeneousDepth: false),
            new RenderPresentationSize(800, 600),
            new RenderViewport(0, 0, 800, 600),
            1,
            0.016f);

        provider.Submit(context);
        Assert.True(canvas.isReady);
        Assert.Equal(1, ui.created);
        Assert.Equal(1, ui.loaded);
        Assert.Equal(1, ui.shown);
        Assert.Equal(1, ui.updated);
        Assert.Equal(1, ui.rendered);
        Assert.Equal(UiEventType.Click, Assert.Single(canvas.events).type);
        Assert.True(canvas.SetAttribute("launch", "disabled", "true"));
        Assert.Equal(1, ui.attributesSet);
        RenderRequest draw = Assert.Single(requests.values);
        Assert.Equal("Canvas/Overlay", draw.name);
        Assert.Same(canvas.pipeline, draw.pipeline);
        Assert.Equal(new RenderViewport(0, 0, 800, 600), draw.viewport);

        Assert.True(scene.DestroyObject(owner));
        provider.Submit(context);
        Assert.False(canvas.isReady);
        Assert.Equal(1, ui.destroyed);
        Assert.Equal("Canvas/Overlay/Retire", requests.values[1].name);
        Assert.Equal(2, requests.values.Count);

        GameObject replacement = scene.CreateObject("Replacement");
        Canvas replacementCanvas = replacement.AddComponent<Canvas>();
        replacementCanvas.document = document;
        provider.Submit(context);
        Assert.True(replacementCanvas.isReady);
        Assert.Equal(2, ui.created);

        uiScope.Dispose();
        provider.Dispose();
        Assert.Equal(2, ui.destroyed);
    }

    private sealed class CapturingRequests : IRenderRequestSink
    {
        internal List<RenderRequest> values { get; } = [];

        public void Submit(RenderRequest request) => values.Add(request);
    }

    private sealed class CapturingUi : IUiService
    {
        internal int created { get; private set; }
        internal int destroyed { get; private set; }
        internal int loaded { get; private set; }
        internal int shown { get; private set; }
        internal int updated { get; private set; }
        internal int rendered { get; private set; }
        internal int attributesSet { get; private set; }

        public UiContextHandle CreateContext(UiContextOptions options)
        {
            created++;
            return new UiContextHandle(1);
        }

        public void DestroyContext(UiContextHandle context) => destroyed++;
        public void SetViewport(UiContextHandle context, int width, int height, float density = 1f) { }
        public UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentSource source)
        {
            loaded++;
            return new UiDocumentHandle(2);
        }

        public UiDocumentHandle LoadDocument(UiContextHandle context, UiDocumentAsset document)
        {
            loaded++;
            return new UiDocumentHandle(2);
        }

        public void ShowDocument(UiContextHandle context, UiDocumentHandle document) => shown++;
        public void HideDocument(UiContextHandle context, UiDocumentHandle document) { }
        public void CloseDocument(UiContextHandle context, UiDocumentHandle document) { }
        public bool SetText(UiContextHandle context, UiDocumentHandle document, string elementId, string text) => true;
        public bool SetContent(UiContextHandle context, UiDocumentHandle document, string elementId, UiDocumentFragment content) => true;
        public bool SetAttribute(UiContextHandle context, UiDocumentHandle document, string elementId, string name, string value)
        {
            attributesSet++;
            return true;
        }

        public bool SetClass(UiContextHandle context, UiDocumentHandle document, string elementId, string className, bool active) => true;
        public void RegisterFont(FontAsset font, string family, int faceIndex = 0, TextFontStyle style = TextFontStyle.Normal,
            int weight = 400, bool fallback = false) { }
        public void RegisterTexture(UiContextHandle context, string source, UiTextureData texture) { }
        public void Update(UiContextHandle context) => updated++;
        public UiRenderFrame Render(UiContextHandle context)
        {
            rendered++;
            var builder = new UiRenderFrameBuilder();
            builder.AddTextureUpdate(new(4), 1, 1, 1, [255, 255, 255, 255]);
            return builder.Build();
        }

        public IReadOnlyList<UiEvent> DrainEvents(UiContextHandle context)
            => [new UiEvent(UiEventType.Click, new UiDocumentHandle(2), "launch")];
    }
}
