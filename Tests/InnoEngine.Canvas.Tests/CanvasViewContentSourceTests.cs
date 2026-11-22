using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Inno.Assets;
using Inno.Core.Mathematics;
using Inno.Core.Identity;
using Inno.Core.Serialization;
using Inno.Core.Settings;
using Inno.References;
using Inno.Rendering;
using Inno.Scene;
using Inno.Text;
using Inno.UI;
using Xunit;
using Inno.Rendering.Runtime;

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
        UIDocumentAsset? imported = loader.Load(
            AssetPath.Project("~Samples/CanvasDemo.rml"), typeof(UIDocumentAsset)) as UIDocumentAsset;
        Assert.True(imported is not null, fixture.diagnosticSummary);
        UIDocumentAsset document = imported!;
        GameScene scene = fixture.world.LoadNewScene("Canvas Test");
        GameObject owner = scene.CreateObject("World UI");
        owner.transform.localPosition = new Vector3(0.25f, 0f, 0f);
        owner.transform.localRotation = Quaternion.CreateFromAxisAngle(Vector3.FORWARD, MathF.PI / 2f);
        owner.transform.localScale = new Vector3(2f, 1f, 0f);
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.document = document;
        var receivedEvents = new List<UIEvent>();
        using IDisposable subscription = canvas.Listen(receivedEvents.Add);

        var ui = new CapturingUi();
        using var source = new CanvasViewContentSource();
        using IDisposable uiScope = UIExecutionContext.EnterScope(ui);
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
        Assert.Equal(owner.identity.persistentID, item.owner.persistentID);
        Assert.Empty(receivedEvents);
        fixture.world.Update(0.016f);
        Assert.Equal(UIEventType.Click, Assert.Single(receivedEvents).type);
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

        var replacementUi = new CapturingUi();
        using (UIExecutionContext.EnterScope(replacementUi))
        {
            var replacementItems = new CapturingItems();
            source.Collect(new ViewContentContext(content, "game", view, 5, 0.016f), replacementItems);
            Assert.Single(replacementItems.items);
            source.CompleteFrame(5);
            Assert.Equal(1, ui.destroyed);
            Assert.Equal(4, ui.updated);
            Assert.Equal(1, replacementUi.created);
            Assert.Equal(1, replacementUi.updated);
            Assert.Equal(1, replacementUi.rendered);
        }

        owner.transform.localScale = new Vector3(0f, 1f, 1f);
        var invalidSize = new CapturingItems();
        source.Collect(new ViewContentContext(content, "game", view, 5, 0.016f), invalidSize);
        Assert.Empty(invalidSize.items);
        Assert.True(canvas.isReady);
        Assert.Equal(1, ui.destroyed);
        owner.transform.localScale = new Vector3(2f, 1f, 0f);
        source.Collect(new ViewContentContext(content, "game", view, 6, 0.016f), new CapturingItems());
        Assert.True(canvas.isReady);
        Assert.True(scene.DestroyObject(owner));
        source.Collect(new ViewContentContext(content, "game", view, 7, 0.016f), new CapturingItems());
        Assert.False(canvas.isReady);
        Assert.Equal(2, ui.destroyed);
        Assert.Equal(1, replacementUi.destroyed);
    }

    [Theory]
    [InlineData("Destroy")]
    [InlineData("Disable")]
    [InlineData("DocumentUnassigned")]
    [InlineData("Remove")]
    [InlineData("Replace")]
    [InlineData("ContextReplaced")]
    [InlineData("Unload")]
    [InlineData("Unregister")]
    [InlineData("DestroyDuringUpdate")]
    public void RetiredCanvasCannotReceiveDeferredInputOrEvents(string operation)
    {
        using var fixture = new CanvasFixture();
        using IDisposable logs = fixture.EnterLogScope();
        CopyCanvasAssets(fixture);
        using IDisposable sceneScope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        using IDisposable assets = fixture.EnterAssetScope(loader);
        using IDisposable settings = ProjectSettingsExecutionContext.EnterScope(new FixedSettings());
        var document = Assert.IsType<UIDocumentAsset>(loader.Load(AssetPath.Project("~Samples/CanvasDemo.rml"), typeof(UIDocumentAsset)));
        GameScene scene = fixture.world.LoadNewScene("Deferred Canvas");
        GameObject owner = scene.CreateObject("UI");
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.document = document;
        var ui = new CapturingUi();
        using IDisposable uiScope = UIExecutionContext.EnterScope(ui);
        using var source = new CanvasViewContentSource();
        var view = new RenderView("deferred", new RenderViewport(0, 0, 800, 450), Matrix.identity, Matrix.identity);
        using ContentReadScope content = new([scene.identity]);
        var items = new CapturingItems();
        source.Collect(new ViewContentContext(content, "game", view, 1, 0.016f), items);
        IViewPointerTarget target = Assert.Single(items.items).pointerTarget!;
        target.SetKeyboardFocus(true);
        switch (operation)
        {
            case "Destroy":
                Assert.True(scene.DestroyObject(owner));
                break;
            case "Disable":
                canvas.enabled = false;
                break;
            case "DocumentUnassigned":
                canvas.document = null;
                break;
            case "Remove":
                Assert.True(owner.RemoveComponent(canvas));
                break;
            case "Replace":
                Assert.True(owner.RemoveComponent(canvas));
                owner.AddComponent<Canvas>().document = document;
                break;
            case "ContextReplaced":
                using (UIExecutionContext.EnterScope(new CapturingUi()))
                    Assert.True(canvas.SetText("message", "Replacement context"));
                break;
            case "Unload":
                Assert.True(fixture.world.UnloadScene(scene));
                break;
            case "Unregister":
                Assert.True(IdentityAllocator.current.Unregister(canvas));
                break;
            case "DestroyDuringUpdate":
                ui.onUpdate = () => Assert.True(scene.DestroyObject(owner));
                break;
            default:
                throw new InvalidOperationException(operation);
        }
        target.Advance(RenderOutputInput.empty, default, 1);
        source.CompleteFrame(1);
        source.CompleteFrame(2);
        Assert.Equal(operation == "DestroyDuringUpdate" ? 1 : 0, ui.updated);
        Assert.Equal(0, ui.rendered);
        Assert.False(target.hasKeyboardFocus);
        Assert.False(target.hasPointerCapture);
        Assert.False(target.TryHit(view, RenderOutputInput.empty, out _));
        target.Advance(RenderOutputInput.empty, default, 3);
        source.Dispose();
        if (!owner.isDestroyed)
            Assert.True(scene.DestroyObject(owner));
        Assert.Equal(1, ui.destroyed);
    }

    [Fact]
    public void RetainedPointerTargetDoesNotKeepDestroyedCanvasAlive()
    {
        using var fixture = new CanvasFixture();
        using IDisposable logs = fixture.EnterLogScope();
        CopyCanvasAssets(fixture);
        using IDisposable sceneScope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        using IDisposable assets = fixture.EnterAssetScope(loader);
        using IDisposable settings = ProjectSettingsExecutionContext.EnterScope(new FixedSettings());
        var document = Assert.IsType<UIDocumentAsset>(loader.Load(AssetPath.Project("~Samples/CanvasDemo.rml"), typeof(UIDocumentAsset)));
        var ui = new CapturingUi();
        using IDisposable uiScope = UIExecutionContext.EnterScope(ui);
        using var source = new CanvasViewContentSource();
        (WeakReference<Canvas> weak, IViewPointerTarget target) = CollectDestroyedCanvas(fixture.world.LoadNewScene("Weak Canvas"), document, source);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(weak.TryGetTarget(out _));
        target.Advance(RenderOutputInput.empty, default, 2);
        source.CompleteFrame(2);
        Assert.Equal(0, ui.updated);
        Assert.Equal(1, ui.destroyed);
        GC.KeepAlive(target);
        GC.KeepAlive(source);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<Canvas>, IViewPointerTarget) CollectDestroyedCanvas(
        GameScene scene,
        UIDocumentAsset document,
        CanvasViewContentSource source
    ) {
        GameObject owner = scene.CreateObject("Temporary UI");
        Canvas canvas = owner.AddComponent<Canvas>();
        canvas.document = document;
        using ContentReadScope content = new([scene.identity]);
        var view = new RenderView("weak", new RenderViewport(0, 0, 800, 450), Matrix.identity, Matrix.identity);
        var items = new CapturingItems();
        source.Collect(new ViewContentContext(content, "game", view, 1, 0.016f), items);
        var weak = new WeakReference<Canvas>(canvas);
        Assert.True(scene.DestroyObject(owner));
        return (weak, Assert.Single(items.items).pointerTarget!);
    }

    private static void CopyCanvasAssets(CanvasFixture fixture)
    {
        string[] paths =
        [
            "~Samples/CanvasDemo.rml",
            "~Samples/Fonts/LatoLatin-Regular.ttf",
            "~Samples/Fonts/LatoLatin-Bold.ttf",
            "Shaders/CanvasVertex.ishadersource",
            "Shaders/CanvasFragment.ishadersource",
            "Shaders/Canvas.ishader",
            "Materials/Canvas.imaterial"
        ];
        foreach (string path in paths)
            fixture.CopyProjectAsset(path);
    }

    private sealed class CapturingItems : IViewContentSink
    {
        internal List<ViewContentItem> items { get; } = [];
        public void Submit(ViewContentItem item) => items.Add(item);
    }

    private sealed class FixedSettings : IProjectSettingsLookup
    {
        public Guid ownerID { get; } = Guid.NewGuid();
        public long revision => 1;
        public TSetting Get<TSetting>(ProjectSettingID id) where TSetting : class, ISerializable
            => (TSetting)(object)new CanvasProjectSettings();
        public bool TryGet<TSetting>(ProjectSettingID id, out TSetting? setting)
            where TSetting : class, ISerializable
        {
            setting = Get<TSetting>(id);
            return true;
        }
    }

    private sealed class CapturingUi : IUIService
    {
        internal int created { get; private set; }
        internal int destroyed { get; private set; }
        internal int loaded { get; private set; }
        internal int shown { get; private set; }
        internal int updated { get; private set; }
        internal int rendered { get; private set; }
        internal int attributesSet { get; private set; }
        internal UIInputSnapshot? lastInput { get; private set; }
        internal UIContextOptions lastOptions { get; private set; }
        internal Action? onUpdate { get; set; }

        public UIContextHandle CreateContext(UIContextOptions options)
        {
            created++;
            lastOptions = options;
            return new UIContextHandle(1);
        }
        public void DestroyContext(UIContextHandle context) => destroyed++;
        public void SetViewport(UIContextHandle context, int width, int height, float density = 1f)
            => lastOptions = new UIContextOptions(lastOptions.name, width, height, density);
        public bool HasElementAtPoint(UIContextHandle context, Vector2 position) => true;
        public UIDocumentHandle LoadDocument(UIContextHandle context, UIDocumentSource source)
        {
            loaded++;
            return new UIDocumentHandle(2);
        }
        public UIDocumentHandle LoadDocument(UIContextHandle context, UIDocumentAsset document)
        {
            loaded++;
            return new UIDocumentHandle(2);
        }
        public void ShowDocument(UIContextHandle context, UIDocumentHandle document) => shown++;
        public void HideDocument(UIContextHandle context, UIDocumentHandle document) { }
        public void CloseDocument(UIContextHandle context, UIDocumentHandle document) { }
        public bool SetText(UIContextHandle context, UIDocumentHandle document, string elementId, string text) => true;
        public bool SetContent(UIContextHandle context, UIDocumentHandle document, string elementId, UIDocumentFragment content) => true;
        public bool SetAttribute(UIContextHandle context, UIDocumentHandle document, string elementId, string name, string value)
        {
            attributesSet++;
            return true;
        }
        public bool SetClass(UIContextHandle context, UIDocumentHandle document, string elementId, string className, bool active) => true;
        public void RegisterTexture(UIContextHandle context, string source, UITextureData texture) { }
        public void Update(UIContextHandle context) => updated++;
        public void Update(
            UIContextHandle context,
            UIInputSnapshot input
        ) {
            updated++;
            lastInput = input;
            onUpdate?.Invoke();
        }
        public UIRenderFrame Render(UIContextHandle context)
        {
            rendered++;
            var builder = new UIRenderFrameBuilder();
            builder.AddTextureUpdate(new(4), 1, 1, 1, [255, 255, 255, 255]);
            return builder.Build();
        }
        public IReadOnlyList<UIEvent> DrainEvents(UIContextHandle context)
            => [new UIEvent(UIEventType.Click, new UIDocumentHandle(2), "launch")];
    }
}
