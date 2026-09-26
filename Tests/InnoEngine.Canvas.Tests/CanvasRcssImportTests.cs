using System;
using System.Collections.Generic;
using Inno.Adapter.UI.RmlUi.Authoring;
using Inno.Assets;
using Inno.UI.Assets;
using Xunit;

namespace Inno.Canvas.Tests;

public sealed class CanvasRcssImportTests
{
    [Fact]
    public void ExternalStylesAndFontPathsStayInsideTheirAssetSource()
    {
        var reads = new List<AssetPath>();
        var frontend = new RmlUiDocumentFrontend();
        UiDocumentAnalysis result = frontend.Analyze(new UiDocumentSourceFile(
            "~Samples/Menu.rml",
            "<rml><head><link type='text/rcss' href='Styles/Menu.rcss' /></head><body /></rml>",
            path =>
            {
                reads.Add(path);
                return path.localPath.EndsWith("Menu.rcss", StringComparison.Ordinal)
                    ? "@import '../Common.rcss'; body { font-family: Interface; }"
                    : "@font-face { font-family: Interface; src: url('Fonts/Lato.ttf'); font-weight: 700; }";
            }));

        Assert.True(result.succeeded);
        Assert.Equal([AssetPath.Project("~Samples/Styles/Menu.rcss"),
            AssetPath.Project("~Samples/Common.rcss")], reads);
        Assert.Contains("<style>", result.text);
        Assert.Equal("~Samples/Fonts/Lato.ttf", Assert.Single(result.fonts).assetPath);
        Assert.Equal(700, Assert.Single(result.fonts).weight);
    }
}
