using System.Text;
using Microsoft.AspNetCore.Http;
using Kypelon.Pdf.AspNetCore;
using Kypelon.Pdf.Core;
using Kypelon.Pdf.Graphics;
using Kypelon.Pdf.Layout;
using Kypelon.Pdf.Text;
using Xunit;

namespace Kypelon.Pdf.Tests;

public class TextPipelineTests
{
    internal const string ThaiCorpus = "ภาษาไทย\nรายงานผลการตรวจสอบ\nข้อมูลพนักงาน\nปีงบประมาณ 2569\nกิ กี กึ กื\nกุ กู\nก่ ก้ ก๊ ก๋\nกำ\nน้ำ\nผู้\nปู่\nญู\nฐุ";
    internal static FontFace PortableFont() => FontFace.Load(FontFixture.Create(codePoints: Enumerable.Range(32, 95).Concat(Enumerable.Range(0x0E00, 128)).Concat([0x0301, 0x00A0, 0x2007, 0x202F, 0xFEFF, 0x1F600]).Order().Select(i => (uint)i).ToArray()));
    internal sealed class DelegateShaper(Func<TextRun, GlyphRun> shape) : ITextShaper
    {
        public int Calls;
        public GlyphRun Shape(TextRun input) { Calls++; return shape(input); }
    }
    private static PreparedGlyphRun Prepare(string text, ITextShaper shaper) => LineBreaker.Prepare(text, PortableFont(), 10, shaper).Run;

    [Theory]
    [InlineData("A\tB", "A    B", 1, 4, 1, 1)]
    [InlineData("A\r\nB", "A\nB", 1, 1, 1, 2)]
    [InlineData("A\rB", "A\nB", 2, 1, 2, 1)]
    [InlineData("😀A", "😀A", 0, 2, 0, 2)]
    public void NormalizationRetainsOriginalOwnership(string raw, string normalized, int start, int length, int originalStart, int originalLength)
    {
        var source = TextSource.Normalize(raw);
        Assert.Equal(raw, source.OriginalText); Assert.Equal(normalized, source.Text);
        Assert.Equal(new TextRange(originalStart, originalLength), source.GetOriginalRange(start, length));
    }
    [Theory]
    [InlineData(-1, 1)] [InlineData(0, -1)] [InlineData(2, 1)] [InlineData(0, int.MaxValue)]
    public void InvalidInputRangesFail(int start, int length) => Assert.Throws<PdfFontException>(() => new TextRun(PortableFont(), TextSource.Normalize("A"), start, length));
    [Theory]
    [InlineData("😀", 0, 1)] [InlineData("😀", 1, 1)] [InlineData("Á", 0, 1)] [InlineData("กิ", 1, 1)]
    public void InputRangesCannotBisectGraphemes(string text, int start, int length) => Assert.Throws<PdfFontException>(() => new TextRun(PortableFont(), TextSource.Normalize(text), start, length));

    [Theory]
    [InlineData("😀", 2, 1)] [InlineData("Á", 2, 2)] [InlineData("กิ", 2, 2)]
    [InlineData("กี", 2, 2)] [InlineData("กึ", 2, 2)] [InlineData("กื", 2, 2)]
    [InlineData("กุ", 2, 2)] [InlineData("กู", 2, 2)] [InlineData("ก่", 2, 2)]
    [InlineData("ก้", 2, 2)] [InlineData("ก๊", 2, 2)] [InlineData("ก๋", 2, 2)]
    [InlineData("กำ", 2, 2)] [InlineData("น้ำ", 3, 3)] [InlineData("ผู้", 3, 3)]
    [InlineData("ปู่", 3, 3)] [InlineData("ญู", 2, 2)] [InlineData("ฐุ", 2, 2)]
    public void CombiningClustersOwnAllSourceAndCannotSplit(string text, int length, int glyphCount)
    {
        var font = PortableFont();
        var run = LineBreaker.Prepare(text, font, 10).Run;
        Assert.Equal(new TextCluster(0, length), Assert.Single(run.Clusters));
        Assert.Equal(glyphCount, run.Glyphs.Count); Assert.All(run.Glyphs, g => Assert.Equal(0, g.Cluster));
        Assert.Equal(text, string.Concat(Enumerable.Range(0, glyphCount).Select(run.GetUnicodeMapping)));
        Assert.Throws<PdfFontException>(() => LineBreaker.Wrap(text, font, 10, glyphCount * 6 - 1));
    }
    [Fact]
    public void WrappedRangesRemainAbsoluteInNormalizedSource()
    {
        var lines = LineBreaker.Wrap("A\r\nBC", PortableFont(), 10, 6);
        Assert.Equal(new[] { 0, 2, 3 }, lines.Select(l => l.Run.Input.SourceStart));
        Assert.Equal(new[] { 0, 3, 4 }, lines.Select(l => l.Run.Input.Source.GetOriginalRange(l.Run.Input.SourceStart, l.Run.Input.SourceLength).Start));
        Assert.All(lines, l => Assert.Equal(l.Run.Input.SourceStart, Assert.Single(l.Run.Clusters).SourceStart));
    }
    [Fact]
    public void LigatureOwnsSeveralSourceCharacters()
    {
        var run = Prepare("fi", new DelegateShaper(i => new([new(i.SourceStart, 2)], [new(1, 0, 900)])));
        Assert.Single(run.Glyphs); Assert.Equal(new TextCluster(0, 2), Assert.Single(run.Clusters));
        Assert.Equal("fi", run.GetUnicodeMapping(0)); Assert.Equal(900, run.Advance);
    }
    [Fact]
    public void OneSourceClusterCanOwnSeveralGlyphsWithoutDuplicateMapping()
    {
        var run = Prepare("A", new DelegateShaper(i => new([new(i.SourceStart, 1)], [new(1, 0, 300), new(1, 0, 300)])));
        Assert.Equal(2, run.Glyphs.Count); Assert.Single(run.Clusters);
        Assert.Equal("A", run.GetUnicodeMapping(0)); Assert.Null(run.GetUnicodeMapping(1)); Assert.True(run.RequiresActualText);
    }
    [Fact]
    public void ReorderedGlyphsKeepLogicalOwnership()
    {
        var run = Prepare("AB", new DelegateShaper(i => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)], [new(1, 1, 600), new(1, 0, 600)])));
        Assert.Equal("AB", run.Text); Assert.Equal("B", run.GetUnicodeMapping(0)); Assert.Equal("A", run.GetUnicodeMapping(1)); Assert.True(run.RequiresActualText);
    }
    [Fact]
    public void InterleavedClusterGlyphsAreRepresentable()
    {
        var run = Prepare("AB", new DelegateShaper(i => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)], [new(1, 0, 200), new(1, 1, 200), new(1, 0, 200)])));
        Assert.Equal(new[] { 0, 1, 0 }, run.Glyphs.Select(g => g.Cluster));
        Assert.True(run.RequiresActualText); Assert.Null(run.GetUnicodeMapping(2));
    }
    [Theory]
    [InlineData("glyph")] [InlineData("cluster-negative")] [InlineData("cluster-outside")]
    [InlineData("range-negative")] [InlineData("range-outside")] [InlineData("range-overflow")]
    [InlineData("range-gap")] [InlineData("range-overlap")] [InlineData("range-empty")]
    [InlineData("unowned-source")] [InlineData("unowned-cluster")] [InlineData("null-run")]
    [InlineData("null-glyphs")] [InlineData("null-clusters")] [InlineData("negative-total")]
    [InlineData("advance-nan")] [InlineData("advance-infinity")] [InlineData("advance-negative-infinity")]
    [InlineData("advance-y-nan")] [InlineData("advance-y-infinity")]
    [InlineData("offset-x-nan")] [InlineData("offset-x-infinity")]
    [InlineData("offset-y-nan")] [InlineData("offset-y-infinity")]
    [InlineData("advance-huge")] [InlineData("offset-huge")]
    public async Task InvalidCustomResultFailsDuringPlanAndBeforeSyncAsyncOrHttpOutput(string kind)
    {
        var shaper = new DelegateShaper(input => Invalid(kind, input));
        var d = Pdf.Document(b => { b.DefaultFont(PortableFont()).TextShaper(shaper); b.Page(p => p.Content(c => c.Text("AB"))); });
        Assert.Throws<PdfFontException>(() => d.Plan());
        using var stream = new MemoryStream();
        Assert.Throws<PdfFontException>(() => d.Write(stream)); Assert.Equal(0, stream.Length);
        await Assert.ThrowsAsync<PdfFontException>(() => d.WriteAsync(stream)); Assert.Equal(0, stream.Length);
        var http = new DefaultHttpContext(); http.Response.Body = stream;
        await Assert.ThrowsAsync<PdfFontException>(() => d.PdfFile().ExecuteAsync(http)); Assert.Equal(0, stream.Length);
    }
    private static GlyphRun Invalid(string kind, TextRun input)
    {
        var c = new TextCluster[] { new(input.SourceStart, 1), new(input.SourceStart + 1, 1) };
        var g = new PositionedGlyph[] { new(1, 0, 600), new(1, 1, 600) };
        switch (kind)
        {
            case "glyph": g[0] = g[0] with { GlyphId = 999 }; break;
            case "cluster-negative": g[0] = g[0] with { Cluster = -1 }; break;
            case "cluster-outside": g[0] = g[0] with { Cluster = 2 }; break;
            case "range-negative": c[0] = new(-1, 1); break;
            case "range-outside": c[1] = new(1, 2); break;
            case "range-overflow": c[0] = new(0, int.MaxValue); break;
            case "range-gap": c[0] = new(1, 1); break;
            case "range-overlap": c[1] = new(0, 1); break;
            case "range-empty": c[0] = new(0, 0); break;
            case "unowned-source": return new([c[0]], [g[0]]);
            case "unowned-cluster": return new(c, [g[0]]);
            case "null-run": return null!;
            case "null-glyphs": return new(c, null!);
            case "null-clusters": return new(null!, g);
            case "negative-total": g[0] = g[0] with { AdvanceX = -2000 }; break;
            case "advance-nan": g[0] = g[0] with { AdvanceX = double.NaN }; break;
            case "advance-infinity": g[0] = g[0] with { AdvanceX = double.PositiveInfinity }; break;
            case "advance-negative-infinity": g[0] = g[0] with { AdvanceX = double.NegativeInfinity }; break;
            case "advance-y-nan": g[0] = g[0] with { AdvanceY = double.NaN }; break;
            case "advance-y-infinity": g[0] = g[0] with { AdvanceY = double.PositiveInfinity }; break;
            case "offset-x-nan": g[0] = g[0] with { OffsetX = double.NaN }; break;
            case "offset-x-infinity": g[0] = g[0] with { OffsetX = double.PositiveInfinity }; break;
            case "offset-y-nan": g[0] = g[0] with { OffsetY = double.NaN }; break;
            case "offset-y-infinity": g[0] = g[0] with { OffsetY = double.PositiveInfinity }; break;
            case "advance-huge": g[0] = g[0] with { AdvanceX = double.MaxValue }; break;
            case "offset-huge": g[0] = g[0] with { OffsetX = double.MaxValue }; break;
            default: throw new InvalidOperationException(kind);
        }
        return new(c, g);
    }
    [Theory] [InlineData("😀")] [InlineData("Á")] [InlineData("น้ำ")]
    public void AdapterCannotInventBoundariesInsideCombiningSequences(string text)
    {
        var shaper = new DelegateShaper(i => new(Enumerable.Range(i.SourceStart, i.SourceLength).Select(n => new TextCluster(n, 1)).ToArray(), Enumerable.Range(0, i.SourceLength).Select(n => new PositionedGlyph(1, n, 600)).ToArray()));
        Assert.Throws<PdfFontException>(() => Prepare(text, shaper));
    }
    [Fact]
    public void PreparedStateSnapshotsAdapterCollectionsAndSourceElements()
    {
        var clusters = new List<TextCluster> { new(0, 1) }; var glyphs = new List<PositionedGlyph> { new(1, 0, 600) };
        var font = PortableFont(); var shaper = new DelegateShaper(_ => new(clusters, glyphs));
        var element = new TextElement("A", new() { Font = font, Size = 10 });
        var block = element.Measure(new() { Shaper = shaper }, 100);
        var text = Assert.IsType<TextCommand>(Assert.Single(block.Commands));
        element.Text = "changed"; element.Style = new() { Font = FontFace.Courier, Size = 30 };
        glyphs[0] = new(999, 20, double.NaN); clusters[0] = new(-1, 999);
        Assert.Equal("A", text.Prepared!.Text); Assert.Same(font, text.Prepared.Run.Input.Font); Assert.Equal(10, text.Prepared.FontSize);
        Assert.Equal(600, text.Prepared.Run.Glyphs[0].AdvanceX); Assert.Equal(new TextCluster(0, 1), text.Prepared.Run.Clusters[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<PositionedGlyph>)text.Prepared.Run.Glyphs)[0] = default);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RenderingAndParagraphPaginationDoNotReshapeAcceptedLines(bool async)
    {
        var shaper = new DelegateShaper(BasicTextShaper.Instance.Shape);
        var d = Pdf.Document(b => { b.TextShaper(shaper); b.Page(p => { p.Size(new(160, 140)).Margin(10); p.Content(c => c.Paragraph(string.Join(" ", Enumerable.Repeat("Word", 100)))); p.Footer(f => f.PageNumber()); }); });
        var plans = d.Plan(); Assert.True(plans.Count > 1);
        int planCalls = shaper.Calls; shaper.Calls = 0;
        using var stream = new WatchStream(() => shaper.Calls);
        if (async) await d.WriteAsync(stream); else d.Write(stream);
        Assert.Equal(planCalls, shaper.Calls); Assert.Equal(stream.FirstWriteCalls, shaper.Calls);
        Assert.All(plans.SelectMany(p => p.Commands.OfType<TextCommand>()), t => Assert.NotNull(t.Prepared));
    }
    [Fact]
    public void AFinalSingleLineIsShapedExactlyOnce()
    {
        var shaper = new DelegateShaper(BasicTextShaper.Instance.Shape);
        Pdf.Document(b => { b.TextShaper(shaper); b.Page(p => p.Content(c => c.Text("One final line"))); }).Write(Stream.Null);
        Assert.Equal(1, shaper.Calls);
    }
    [Fact]
    public void ExplicitShapingHintsReachPreparedState()
    {
        var shaper = new DelegateShaper(i => new([new(i.SourceStart, i.SourceLength)], [new(1, 0, 600)]));
        var element = new TextElement("A", new() { Font = PortableFont(), Direction = TextDirection.RightToLeft, Script = "Thai", Language = "th" });
        var prepared = Assert.IsType<TextCommand>(Assert.Single(element.Measure(new() { Shaper = shaper }, 100).Commands)).Prepared!;
        Assert.Equal(TextDirection.RightToLeft, prepared.Run.Input.Direction); Assert.Equal("Thai", prepared.Run.Input.Script); Assert.Equal("th", prepared.Run.Input.Language);
    }
    [Fact]
    public void BasicShaperDoesNotPretendToImplementBidi() => Assert.Throws<PdfFontException>(() => LineBreaker.Prepare("A", PortableFont(), 10, direction: TextDirection.RightToLeft));
    [Fact]
    public void ActualTextGraphicsMustBalanceAndEscape()
    {
        var canvas = new PdfCanvas(100); Assert.Throws<InvalidOperationException>(() => canvas.EndActualText());
        canvas.BeginActualText(@"(A)\B"); Assert.Throws<InvalidOperationException>(() => canvas.ToArray()); canvas.EndActualText();
        Assert.Contains(new PdfString(@"(A)\B").ToPdfSyntax(), Encoding.ASCII.GetString(canvas.ToArray()));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task InvalidShapingIsDetectedBeforeSaveOpensFile(bool asynchronous)
    {
        string file = Path.Combine(Path.GetTempPath(), "kypelon-shape-" + Guid.NewGuid() + ".pdf");
        try
        {
            await File.WriteAllTextAsync(file, "KEEP");
            var shaper = new DelegateShaper(i => Invalid("glyph", i));
            var doc = Pdf.Document(d => { d.DefaultFont(PortableFont()).TextShaper(shaper); d.Page(p => p.Content(c => c.Text("AB"))); });
            if (asynchronous) await Assert.ThrowsAsync<PdfFontException>(() => doc.SaveAsync(file));
            else Assert.Throws<PdfFontException>(() => doc.Save(file));
            Assert.Equal("KEEP", await File.ReadAllTextAsync(file));
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void FinalPlanDoesNotDependOnLaterElementStyleOrFontMutation()
    {
        var text = new TextElement("Original", new() { Size = 10 });
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Add(text))));
        var plans = doc.Plan();
        text.Text = "changed"; text.Style = new() { Font = PortableFont(), Size = 22 };
        var command = Assert.Single(plans.SelectMany(p => p.Commands).OfType<TextCommand>());
        Assert.Equal("Original", command.Prepared!.Text); Assert.Same(FontFace.Courier, command.Prepared.Run.Input.Font); Assert.Equal(10, command.Prepared.FontSize);
        Assert.Throws<NotSupportedException>(() => ((IList<RenderCommand>)plans[0].Commands)[0] = new RectangleCommand(default, null, null, 0));
        Assert.Equal("changed", Assert.Single(doc.Plan().SelectMany(p => p.Commands).OfType<TextCommand>()).Prepared!.Text);
    }
    [Fact]
    public void PositionedGlyphAdvancesAndOffsetsAreRenderedExactly()
    {
        var shaper = new DelegateShaper(i => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)],
            [new(1, 0, 700, 100, 25, 50), new(1, 1, 500, 0, -25, -50)]));
        var doc = Pdf.Document(d => { d.DefaultFont(PortableFont()).TextShaper(shaper); d.Page(p => p.Content(c => c.Text("AB").FontSize(10))); });
        doc.Options.CompressStreams = false;
        var command = Assert.Single(doc.Plan().SelectMany(p => p.Commands).OfType<TextCommand>());
        Assert.Equal(12, command.Prepared!.Width);
        string syntax = Encoding.Latin1.GetString(doc.ToArray());
        Assert.Contains($"1 0 0 -1 {PdfReal.Format(command.X + .25)} {PdfReal.Format(command.Baseline - .5)} Tm", syntax);
        Assert.Contains($"1 0 0 -1 {PdfReal.Format(command.X + 6.75)} {PdfReal.Format(command.Baseline - .5)} Tm", syntax);
    }
    [Fact]
    public void ClusterOwnershipCannotEscapeRequestedSliceEvenInsideSameSource()
    {
        var source = TextSource.Normalize("ABCD");
        var input = new TextRun(PortableFont(), source, 1, 2);
        var shaper = new DelegateShaper(_ => new([new(0, 2)], [new(1, 0, 600)]));
        Assert.Throws<PdfFontException>(() => PreparedGlyphRun.Create(input, shaper));
    }
    [Fact]
    public void EmptySourceCannotOwnInventedGlyphs()
    {
        var shaper = new DelegateShaper(_ => new([], [new(1, 0, 600)]));
        Assert.Throws<PdfFontException>(() => Prepare("", shaper));
    }
    [Fact]
    public void ThaiClusterUsesContextualWidthInsteadOfSummedScalarWidths()
    {
        var shaper = new DelegateShaper(i =>
        {
            var basic = BasicTextShaper.Instance.Shape(i);
            return basic with { Glyphs = basic.Glyphs.Select((g, n) => g with { AdvanceX = i.Text == "กิ" ? 400 : 600 }).ToArray() };
        });
        var line = Assert.Single(LineBreaker.Wrap("กิ", PortableFont(), 10, 9, shaper));
        Assert.Equal(8, line.Width); Assert.Single(line.Run.Clusters); Assert.Equal("กิ", line.Text);
    }
    [Fact]
    public void FontEncoderRejectsDifferentPreparedFontIdentity()
    {
        using var stream = new MemoryStream(); using var writer = new PdfFileWriter(stream);
        var resource = new FontResource(writer, PortableFont());
        Assert.Throws<PdfFontException>(() => resource.Encode(LineBreaker.Prepare("A", PortableFont(), 10).Run));
        Assert.Equal(0, stream.Length);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void ContextualWidthIsRetainedInParagraphAndTablePlans(bool table)
    {
        var shaper = new DelegateShaper(i => new(i.Text.Select((c, n) => new TextCluster(i.SourceStart + n, 1)).ToArray(), i.Text.Select((c, n) => new PositionedGlyph((ushort)c, n, i.Text == "AV" ? 900 : 600)).ToArray()));
        var d = Pdf.Document(b => { b.TextShaper(shaper); b.Page(p => { p.Margin(10); p.Content(c =>
        {
            if (table) c.Table(t => { t.Columns(Column.Fixed(25)); t.Row("AV"); });
            else c.Add(new TextElement("AV", new() { Size = 10 }) { Box = new() { Width = 15 } });
        }); }); });
        var text = d.Plan().SelectMany(p => p.Commands).OfType<TextCommand>().ToArray();
        Assert.Equal(new[] { "A", "V" }, text.Select(t => t.Text));
        Assert.All(text, t => Assert.True(t.Prepared!.Width <= t.ReservedWidth));
    }
    [Fact]
    public void ExcessiveGlyphExpansionIsRejectedBeforeSnapshot()
    {
        var shaper = new DelegateShaper(i => new([new(i.SourceStart, 1)], Enumerable.Repeat(new PositionedGlyph(1, 0, 0), 33).ToArray()));
        Assert.Contains("excessive", Assert.Throws<PdfFontException>(() => Prepare("A", shaper)).Message);
    }
    [Fact]
    public void ScaledPositionsAreValidatedBeforeRendering()
    {
        var shaper = new DelegateShaper(i => new([new(i.SourceStart, i.SourceLength)], Enumerable.Repeat(new PositionedGlyph(1, 0, 0, 10_000_000), 100).ToArray()));
        Assert.Contains("position", Assert.Throws<PdfFontException>(() => LineBreaker.Prepare(new string('A', 100), PortableFont(), 100_000, shaper)).Message);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task FailedPreparationStillClosesAnOwnedOutputStream(bool asynchronous)
    {
        var doc = Pdf.Document(d => { d.DefaultFont(PortableFont()).TextShaper(new DelegateShaper(i => Invalid("glyph", i))); d.Page(p => p.Content(c => c.Text("AB"))); });
        var stream = new MemoryStream();
        if (asynchronous) await Assert.ThrowsAsync<PdfFontException>(() => doc.WriteAsync(stream, leaveOpen: false));
        else Assert.Throws<PdfFontException>(() => doc.Write(stream, leaveOpen: false));
        Assert.False(stream.CanWrite); Assert.Empty(stream.ToArray());
    }
    [Fact]
    public void CustomUnpreparedTextCommandsUseTheSameNormalizationPolicy()
    {
        var doc = Pdf.Document(d => d.Page(p => p.Content(c => c.Add(new RawTextElement()))));
        var command = Assert.Single(doc.Plan().SelectMany(p => p.Commands).OfType<TextCommand>());
        Assert.Equal("A    B", command.Text); Assert.Equal(command.Text, command.Prepared!.Text);
    }
    private sealed class RawTextElement : Element
    {
        protected override MeasuredBlock MeasureContent(LayoutContext context, double width) =>
            new(width, 20, [new TextCommand("A\tB", context.Font, 10, Color.Black, 0, 10, false)]);
    }
    private sealed class WatchStream(Func<int> count) : MemoryStream
    {
        public int? FirstWriteCalls;
        public override void Write(ReadOnlySpan<byte> data) { FirstWriteCalls ??= count(); base.Write(data); }
        public override void Write(byte[] data, int offset, int length) { FirstWriteCalls ??= count(); base.Write(data, offset, length); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken token = default) { FirstWriteCalls ??= count(); return base.WriteAsync(data, token); }
    }

    [Theory]
    [InlineData("ligature", "fi")] [InlineData("one-to-many", "A")] [InlineData("reordered", "AB")]
    [InlineData("surrogate", "😀")] [InlineData("combining", "Á")]
    [InlineData("thai", ThaiCorpus)] [InlineData("interleaved", "AB")]
    [InlineData("thai-reordered", "กิข")] [InlineData("ligature-native", "fi")]
    public void ComplexExtractionFixturesUseSourceOwnedMappings(string mode, string text)
    {
        static GlyphRun Reverse(GlyphRun run) => run with { Glyphs = run.Glyphs.Reverse().ToArray() };
        var font = PortableFont();
        var shaper = new DelegateShaper(i => mode switch
        {
            "ligature-native" => new([new(i.SourceStart, i.SourceLength)], [new(1, 0, 600)]),
            "thai-reordered" => Reverse(BasicTextShaper.Instance.Shape(i)),
            "ligature" => new([new(i.SourceStart, i.SourceLength)], [new(1, 0, 900)]),
            "one-to-many" => new([new(i.SourceStart, i.SourceLength)], [new(1, 0, 300), new(1, 0, 300)]),
            "reordered" => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)], [new(1, 1, 600), new(1, 0, 600)]),
            "interleaved" => new([new(i.SourceStart, 1), new(i.SourceStart + 1, 1)], [new(1, 0, 200), new(1, 1, 200), new(1, 0, 200)]),
            _ => BasicTextShaper.Instance.Shape(i)
        });
        var d = Pdf.Document(b => { b.DefaultFont(font).TextShaper(shaper); b.Page(p => p.Content(c => c.Text(text))); });
        d.Options.Deterministic = true; d.Options.CompressStreams = false;
        var bytes = d.ToArray(); var syntax = Encoding.Latin1.GetString(bytes);
        if (mode is "ligature" or "one-to-many" or "reordered" or "interleaved" or "thai-reordered")
        {
            Assert.Contains("/ActualText " + new PdfString(text).ToPdfSyntax(), syntax);
            Assert.Equal(1, syntax.Split("/ActualText ").Length - 1);
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "text-pipeline-pdfs"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, mode + ".pdf"), bytes);
        File.WriteAllText(Path.Combine(folder, mode + ".txt"), text);
        Inspect.Objects(bytes);
    }
}
