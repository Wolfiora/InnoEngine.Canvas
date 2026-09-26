using System;
using System.Collections.Generic;

using Inno.Assets;
using Inno.Core.Mathematics;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.References;
using Inno.Rendering;
using Inno.Scene;
using Inno.Text;
using Inno.UI;
using Xunit;

namespace Inno.Canvas.Tests;

public sealed class CanvasViewContentSourceTests
{
    [Fact]
    public void WorldCanvasPublishesOneViewItemAndReleasesItsContextWhenDestroyed()
    {
        using var fixture = new CanvasFixture();
        using IDisposable logScope = fixture.EnterLogScope();
        fixture.CopyProjectAsset("~Samples/CanvasDemo.rml");
        fixture.CopyProjectAsset("~Samples/Fonts/LatoLatin-Regular.ttf");
        fixture.CopyProjectAsset("~Samples/Fonts/LatoLatin-Bold.ttf");
        fixture.CopyProjectAsset("Shaders/CanvasVertex.ishadersource");
        fixture.CopyProjectAsset("Shaders/CanvasFragment.ishadersource");
        fixture.CopyProjectAsset("Shaders/Canvas.ishader");
        fixture.CopyProjectAsset("Materials/Canvas.imaterial");
        using IDisposable sceneScope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        using IDisposable assetScope = fixture.EnterAssetScope(loader);
        using IDisposable settingsScope = ProjectSettingsExecutionContext.EnterScope(new FixedSettings());
        UiDocumentAsset? imported = loader.Load(
            AssetPath.Project("~Samples/CanvasDemo.rml"), typeof(UiDocumentAsset)) as UiDocumentAsset;
        Assert.True(imported is not null, fixture.diagnosticSummary);
        UiDocumentAsset document = imported!;
        GameScene scene = fixture.world.LoadNewScene("Canvas Test");
        GameObject owner = scene.CreateObject("World UI");
        owner.transform.localPosition = new Vector3(0.25f, 0f, 0f);
        owner.transform.localRotation = Quaternion.CreateFromAxisAngle(Vector3.FORWARD, MathF.PI / 2f);
        owner.transform.localScale = new Vector3(2f, 1f, 0f);
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.document = document;

        var ui = new CapturingUi();
        using var source = new CanvasViewContentSource();
        using IDisposable uiScope = UiExecutionContext.EnterScope(ui);
        Assert.False(canvas.isReady);
        Assert.True(canvas.SetText("message", "Updated without a rendering model"));
        Assert.True(canvas.isReady);
        using ContentReadScope content = new([scene.identity]);
        var view = new RenderView("camera", new RenderViewport(0, 0, 800, 600),
            Matrix.identity, Matrix.identity);
        var zoomedView = new RenderView("camera-zoomed", new RenderViewport(0, 0, 800, 600),
            Matrix.identity, Matrix.CreateScale(1.5f, 1.5f, 1f));
        var sink = new CapturingItems();
        source.Collect(new ViewContentContext(content, "game", view, 1, 0.016f,
            views: [view, zoomedView]), sink);
        ViewContentItem item = Assert.Single(sink.items);
        Assert.Equal(800f, ui.lastOptions.width / ui.lastOptions.density, 2);
        Assert.Equal(450f, ui.lastOptions.height / ui.lastOptions.density, 2);
        var pointer = new RenderOutputInput(new Vector2(500f, 300f), true, default,
            Inno.Core.Input.KeyModifier.None, [], [], [], [], []);
        Assert.True(item.pointerTarget!.TryHit(view, pointer, out Vector2 local));
        Assert.Equal(ui.lastOptions.width * 0.5f, local.x, 2);
        Assert.Equal(ui.lastOptions.height * 0.5f, local.y, 2);
        Assert.False(item.pointerTarget.TryHit(view, new RenderOutputInput(new Vector2(0f, 0f), false,
            default, Inno.Core.Input.KeyModifier.None, [], [], [], [], []), out _));
        item.pointerTarget!.Advance(RenderOutputInput.empty, default, 1);
        source.CompleteFrame(1);
        Assert.True(canvas.isReady);
        Assert.Equal(1, ui.created);
        Assert.Equal(1, ui.loaded);
        Assert.Equal(1, ui.shown);
        Assert.Equal(1, ui.updated);
        Assert.Equal(1, ui.rendered);
        Assert.Equal(owner.identity.persistentId, item.owner.persistentId);
        Assert.Equal(UiEventType.Click, Assert.Single(canvas.DrainEvents()).type);
        Assert.Empty(canvas.DrainEvents());
        Assert.True(canvas.SetAttribute("launch", "disabled", "true"));
        Assert.Equal(1, ui.attributesSet);

        var second = new CapturingItems();
        source.Collect(new ViewContentContext(content, "scene", zoomedView, 2, 0.016f,
            views: [view, zoomedView]), second);
        Assert.Single(second.items).pointerTarget!.Advance(RenderOutputInput.empty, default, 2);
        Assert.Equal(1, ui.updated);

        var press = new RenderOutputInput(new Vector2(500f, 300f), true, default,
            Inno.Core.Input.KeyModifier.None, [], [], [Inno.Core.Input.MouseButton.Left], [], []);
        Assert.Single(second.items).pointerTarget!.Advance(press, local, 2);
        var secondViewKey = new RenderOutputInput(default, false, default,
            Inno.Core.Input.KeyModifier.None, [Inno.Core.Input.KeyCode.B], [], [], [], ["b"]);
        item.pointerTarget.Advance(secondViewKey, default, 2);
        Assert.Equal(1, ui.updated);
        Assert.True(item.pointerTarget.hasPointerCapture);
        source.CompleteFrame(2);
        Assert.Equal(2, ui.updated);
        Assert.Equal(local.x, ui.lastInput!.mousePosition.x, 2);
        Assert.Single(ui.lastInput.buttonsPressed);
        Assert.Single(ui.lastInput.keysPressed);
        Assert.Equal("b", Assert.Single(ui.lastInput.textInput));
        var releaseOutside = new RenderOutputInput(new Vector2(0f, 0f), false, default,
            Inno.Core.Input.KeyModifier.None, [], [], [], [Inno.Core.Input.MouseButton.Left], []);
        Assert.True(item.pointerTarget.TryHit(view, releaseOutside, out Vector2 capturedPosition));
        item.pointerTarget.Advance(releaseOutside, capturedPosition, 3);
        Assert.Equal(2, ui.updated);
        source.CompleteFrame(3);
        Assert.False(item.pointerTarget.hasPointerCapture);
        Assert.Equal(3, ui.updated);
        Assert.Single(ui.lastInput!.buttonsReleased);
        item.pointerTarget.SetKeyboardFocus(true);
        Assert.True(item.pointerTarget.hasKeyboardFocus);
        var keyboard = new RenderOutputInput(default, false, default,
            Inno.Core.Input.KeyModifier.None, [Inno.Core.Input.KeyCode.A], [], [], [], ["a"]);
        item.pointerTarget.Advance(keyboard, default, 4);
        source.CompleteFrame(4);
        Assert.Equal(4, ui.updated);
        Assert.Single(ui.lastInput!.keysPressed);
        Assert.Equal(-10000f, ui.lastInput.mousePosition.x);
        owner.transform.localScale = new Vector3(0f, 1f, 1f);
        var invalidSize = new CapturingItems();
        source.Collect(new ViewContentContext(content, "game", view, 5, 0.016f), invalidSize);
        Assert.Empty(invalidSize.items);
        Assert.True(canvas.isReady);
        Assert.Equal(0, ui.destroyed);
        owner.transform.localScale = new Vector3(2f, 1f, 0f);
        source.Collect(new ViewContentContext(content, "game", view, 6, 0.016f), new CapturingItems());
        Assert.True(canvas.isReady);
        Assert.True(scene.DestroyObject(owner));
        source.Collect(new ViewContentContext(content, "game", view, 7, 0.016f), new CapturingItems());
        Assert.False(canvas.isReady);
        Assert.Equal(1, ui.destroyed);
    }

    private sealed class CapturingItems : IViewContentSink
    {
        internal List<ViewContentItem> items { get; } = [];
        public void Submit(ViewContentItem item) => items.Add(item);
    }

    private sealed class FixedSettings : IProjectSettingsLookup
    {
        public Guid ownerId { get; } = Guid.NewGuid();
        public long revision => 1;
        public TSetting Get<TSetting>(ProjectSettingId id) where TSetting : class, ISerializable
            => (TSetting)(object)new CanvasProjectSettings();
        public bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
            where TSetting : class, ISerializable
        {
            setting = Get<TSetting>(id);
            return true;
        }
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
        internal UiInputSnapshot? lastInput { get; private set; }
        internal UiContextOptions lastOptions { get; private set; }

        public UiContextHandle CreateContext(UiContextOptions options)
        {
            created++;
            lastOptions = options;
            return new UiContextHandle(1);
        }
        public void DestroyContext(UiContextHandle context) => destroyed++;
        public void SetViewport(UiContextHandle context, int width, int height, float density = 1f)
            => lastOptions = new UiContextOptions(lastOptions.name, width, height, density);
        public bool HasElementAtPoint(UiContextHandle context, Vector2 position) => true;
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
        public void RegisterTexture(UiContextHandle context, string source, UiTextureData texture) { }
        public void Update(UiContextHandle context) => updated++;
        public void Update(UiContextHandle context, UiInputSnapshot input)
        {
            updated++;
            lastInput = input;
        }
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
