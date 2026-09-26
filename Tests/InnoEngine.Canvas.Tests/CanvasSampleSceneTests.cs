using System;
using System.Linq;
using Inno.Assets;
using Inno.Canvas.Samples;
using Inno.Scene;
using Inno.UI;
using Xunit;

namespace Inno.Canvas.Tests;

public sealed class CanvasSampleSceneTests
{
    [Fact]
    public void SampleDeclaresCanvasWithoutAConcreteRenderModel()
    {
        using var fixture = new CanvasFixture();
        fixture.CopyProjectAsset("~Samples/CanvasDemo.rml");
        fixture.CopyProjectAsset("~Samples/Fonts/LatoLatin-Regular.ttf");
        fixture.CopyProjectAsset("~Samples/Fonts/LatoLatin-Bold.ttf");
        fixture.CopyProjectAsset("~Samples/SampleScene.iscene");
        using var scope = fixture.world.EnterScope();
        using var loader = fixture.CreateLoader();
        SceneAsset asset = Assert.IsType<SceneAsset>(loader.Load(
            AssetPath.Project("~Samples/SampleScene.iscene"), typeof(SceneAsset)));
        GameScene scene = asset.Instantiate(fixture.serialization, loader);
        GameObject panel = Assert.Single(scene.GetObjects(), owner => owner.name == "Canvas Demo");
        Canvas canvas = Assert.IsType<Canvas>(panel.GetComponents().Single(component => component is Canvas));
        Assert.IsType<CanvasDemoController>(panel.GetComponents().Single(component => component is CanvasDemoController));
        Assert.IsType<UiDocumentAsset>(canvas.document);
        Assert.Equal(1f, panel.transform.localScale.x);
        Assert.Equal(1f, panel.transform.localScale.y);
        Assert.Equal(800, canvas.referenceWidth);
        Assert.Equal(450, canvas.referenceHeight);
        Assert.Single(scene.GetObjects());
        Assert.Empty(scene.GetSystems());
    }

}
