using PortableDocument.Core;
using PortableDocument.Document;
using Xunit;

namespace FreeDF.Tests;

public class MultimediaThreeDTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void Shared3DStreamsAndLargeViewArraysHaveBoundedDetailWork(int nodes)
    {
        var pdf = "%PDF-2.0\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
            "2 0 obj << /Type /Pages /Kids [3 0 R] /Count 1 >> endobj\n" +
            "3 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [" +
            string.Join(" ", Enumerable.Repeat("4 0 R", 500)) + "] >> endobj\n" +
            "4 0 obj << /Subtype /3D /3DD 5 0 R >> endobj\n" +
            "5 0 obj << /Type /3D /Subtype /U3D /VA [" +
            string.Join(" ", Enumerable.Range(10, 100).Select(i => $"{i} 0 R")) +
            "] /Length 0 >> stream\n\nendstream\nendobj\n" +
            "6 0 obj [" + string.Join(" ", Enumerable.Repeat("<< /N (shared) >>", nodes)) + "] endobj\n" +
            string.Join("\n", Enumerable.Range(10, 100).Select(i => $"{i} 0 obj << /Type /3DView /NA 6 0 R /MA 6 0 R /SA 6 0 R >> endobj")) +
            "\ntrailer << /Root 1 0 R /Size 110 >>\n%%EOF\n";
        var doc = PdfDocument.Load(System.Text.Encoding.ASCII.GetBytes(pdf));
        var annotations = doc.Get3DAnnotations();
        Assert.Equal(500, annotations.Count);
        var stream = Assert.IsType<Pdf3DStream>(annotations[0].StreamDetails);
        Assert.True(stream.Truncated);
        Assert.InRange(stream.Views.Count, 1, 64);
        Assert.InRange(stream.Views.Sum(v => v.NodeDetails.Count + v.MeasureDetails.Count + v.CrossSectionDetails.Count), 0, 1024);
        Assert.All(annotations, a => { Assert.Same(stream, a.StreamDetails); Assert.True(a.Truncated); });
        Assert.All(stream.Views, v =>
        {
            Assert.Equal(nodes, v.Nodes!.Count);
            Assert.InRange(v.NodeDetails.Count, 0, 256);
            Assert.InRange(v.MeasureDetails.Count, 0, 256);
            Assert.InRange(v.CrossSectionDetails.Count, 0, 256);
        });
        if (nodes > 0) Assert.Contains(stream.Views, v => v.Truncated);
    }

    static byte[] MultimediaPdf()
    {
        const string pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [4 0 R 20 0 R 21 0 R 22 0 R 40 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /Screen /Rect [0 0 100 100] /T (Clip screen) /MK << /BG [0] >> /A 5 0 R >>\nendobj\n" +
            "5 0 obj\n<< /S /Rendition /OP 0 /R 6 0 R /AN 4 0 R /JS (rendition-script) >>\nendobj\n" +
            "6 0 obj\n<< /Type /Rendition /S /SR /N (selector) /MH << /C << /A true /R 1000 /D << /V 24 /M 1 >> /Z << /V [640 480] >> /V [30 0 R] /L [(en)] /P [/1.5 /1.7] >> >> /BE << /C << /C true >> >> /R [7 0 R 6 0 R 99 0 R] >>\nendobj\n" +
            "7 0 obj\n<< /Type /Rendition /S /MR /N (media) /C 8 0 R /P 12 0 R /SP 13 0 R >>\nendobj\n" +
            "8 0 obj\n<< /Type /MediaClip /S /MCS /N (section) /D 9 0 R /MH << /B << /S /T /T << /S /S /V 1.5 >> >> /E << /S /F /F 120 >> >> /BE << /E << /S /M /M (end) >> >> >>\nendobj\n" +
            "9 0 obj\n<< /Type /MediaClip /S /MCD /N (data) /D 10 0 R /CT (video/mp4) /P << /TF (TEMPEXTRACT) >> /PL 14 0 R /MH << /BU (https://example.com/) >> /BE << /BU (https://example.org/) >> >>\nendobj\n" +
            "10 0 obj\n<< /Type /Filespec /F (clip.mp4) >>\nendobj\n" +
            "12 0 obj\n<< /Type /MediaPlayParams /PL 14 0 R /MH << /V 50 /C true /F 1 /D << /S /T /T << /S /S /V 10 >> >> /A false /RC 0 >> /BE << >> >>\nendobj\n" +
            "13 0 obj\n<< /Type /MediaScreenParams /MH << /W 0 /B [0 0 0] /O 0.5 /M 2 /F << /D << >> >> >> /BE << /W 9 >> >>\nendobj\n" +
            "14 0 obj\n<< /Type /MediaPlayers /MU [<< /PID 30 0 R >>] /A [] /NU [<< /PID 31 0 R >>] >>\nendobj\n" +
            "30 0 obj\n<< /Type /SoftwareIdentifier /U (vnd.adobe.swname:ADBE_PDFX) /L [1] /H [2] /HI false /OS [(Win)] >>\nendobj\n" +
            "31 0 obj\n<< /U (vnd.example:other) >>\nendobj\n" +
            "20 0 obj\n<< /Type /Annot /Subtype /Sound /Rect [0 0 10 10] /Sound 23 0 R >>\nendobj\n" +
            "23 0 obj\n<< /Type /Sound /R 22050 /C 2 /E /Signed /CO /ADPCM /Length 4 >>\nstream\ndata\nendstream\nendobj\n" +
            "21 0 obj\n<< /Type /Annot /Subtype /Movie /Rect [0 0 10 10] /T (m) /Movie << /F (m.mov) /Aspect [320 240] /Rotate 90 /Poster true >> /A << /Start 5 /Rate -1 /Volume 0.25 /ShowControls true /Mode /Palindrome /FWScale [1 2] >> >>\nendobj\n" +
            "22 0 obj\n<< /Type /Annot /Subtype /3D /Rect [0 0 100 100] /3DD 24 0 R /3DV /L /3DI false /3DB [-50 -50 50 50] /3DA << /A /PV /AIS /I /D /XD /DIS /L /TB false /NP true >> >>\nendobj\n" +
            "24 0 obj\n<< /Type /3DRef /3DD 25 0 R >>\nendobj\n" +
            "25 0 obj\n<< /Type /3D /Subtype /PRC /VA [26 0 R 27 0 R] /DV (second) /OnInstantiate 28 0 R /AN << /Subtype /Oscillating /PC -1 /TM 2 >> /Length 3 >>\nstream\nabc\nendstream\nendobj\n" +
            "26 0 obj\n<< /Type /3DView /XN (first) /IN (first) /MS /M /C2W [1 0 0 0 1 0 0 0 1 0 0 0] /P << /Subtype /O /CS /XNF /F 5 /N 0 /OS 2 /OB /W >> /BG << /EA true /C [0 0 0] >> /RM << /Subtype /Wireframe /AC [/DeviceRGB 1 0 0] /FC /BG >> /LS << /Subtype /Night >> /MA [<< /Subtype /LD3 >>] /SA [<< /C [0 0 0] /O [null 0 0] /PO 0.25 /IV true /IC [/DeviceRGB 0 1 0] >>] /NA [<< /N (Cone) /O 0.5 /V false /M [1 0 0 0 1 0 0 0 1 0 0 0] >> 5] >>\nendobj\n" +
            "27 0 obj\n<< /Type /3DView /XN (second) /IN (second) /MS /U3D /U3DPath (Root) >>\nendobj\n" +
            "28 0 obj\n<< /Length 8 >>\nstream\nalert(1)\nendstream\nendobj\n" +
            "40 0 obj\n<< /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /A << /S /Rendition /OP 4 /R 41 0 R >> >>\nendobj\n" +
            "41 0 obj\n<< /S /MR /N 5 /C 42 0 R /P 7 /SP [] /MH 3 >>\nendobj\n" +
            "42 0 obj\n<< /S /MCS /D 42 0 R /CT (ignored) >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 43 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(pdf);
    }

    [Fact]
    public void Rendition_actions_expose_typed_inert_metadata_and_stop_selector_cycles()
    {
        var doc = PdfDocument.Load(MultimediaPdf());
        var actions = doc.GetRenditionActions();

        Assert.Equal(2, actions.Count);
        var action = actions[0];
        Assert.Equal(0, action.Operation);
        Assert.Equal(PdfActionScope.Annotation, action.Scope);
        Assert.Equal(0, action.PageIndex);
        Assert.Equal(0, action.AnnotationIndex);
        Assert.Equal("rendition-script", Assert.IsType<PdfString>(action.JavaScript).Text);
        Assert.Same(doc.File.GetObject(4), action.ScreenAnnotation);

        var selector = Assert.IsType<PdfRendition>(action.Rendition);
        Assert.Equal("SR", selector.Subtype);
        Assert.Equal("selector", selector.Name);
        var alternative = Assert.Single(selector.Renditions);
        Assert.Equal("MR", alternative.Subtype);

        var must = Assert.IsType<PdfMediaCriteria>(selector.MustHonorCriteria);
        Assert.True(must.AudioDescriptions);
        Assert.Null(must.Captions);
        Assert.Equal(1000, must.MinBandwidth);
        Assert.Equal(24, must.MinBitDepth?.Value);
        Assert.Equal(1, must.MinBitDepth?.Monitor);
        Assert.Equal(640, must.MinScreenSize?.Width);
        Assert.Equal(480, must.MinScreenSize?.Height);
        Assert.Equal(0, must.MinScreenSize?.Monitor);
        Assert.Equal("vnd.adobe.swname:ADBE_PDFX", Assert.Single(must.Viewers).Uri);
        Assert.Equal(2, must.PdfVersions?.Count);
        Assert.Single(must.Languages!.Items);
        Assert.True(selector.BestEffortCriteria?.Captions);
    }

    [Fact]
    public void Media_clips_players_play_and_screen_parameters_use_spec_defaults()
    {
        var doc = PdfDocument.Load(MultimediaPdf());
        var media = Assert.Single(Assert.IsType<PdfRendition>(doc.GetRenditionActions()[0].Rendition).Renditions);

        var section = Assert.IsType<PdfMediaClip>(media.Clip);
        Assert.Equal("MCS", section.Subtype);
        Assert.Equal("T", section.MustHonorBegin?.Subtype);
        Assert.Equal(1.5, section.MustHonorBegin?.Seconds);
        Assert.Equal(120, section.MustHonorEnd?.Frame);
        Assert.Equal("end", section.BestEffortEnd?.Marker);
        Assert.Null(section.BestEffortBegin);

        var data = Assert.IsType<PdfMediaClip>(section.Next);
        Assert.Equal("MCD", data.Subtype);
        Assert.Equal("video/mp4", data.ContentType);
        Assert.Equal("TEMPEXTRACT", data.TemporaryFilePermission);
        Assert.Equal("https://example.com/", data.MustHonorBaseUrl);
        Assert.Equal("https://example.org/", data.BestEffortBaseUrl);
        Assert.IsType<PdfDictionary>(data.Data);
        Assert.Equal("TEMPNEVER", section.TemporaryFilePermission);

        var players = Assert.IsType<PdfMediaPlayers>(data.Players);
        var player = Assert.Single(players.MustUse);
        Assert.False(player.Software?.HighInclusive);
        Assert.True(player.Software?.LowInclusive);
        Assert.Equal("Win", Assert.IsType<PdfString>(player.Software?.OperatingSystems?[0]).Text);
        Assert.Empty(players.Alternates);
        Assert.Null(Assert.Single(players.NotUse).Software?.OperatingSystems);

        var play = Assert.IsType<PdfMediaPlayParameters>(media.PlayParameters);
        Assert.Equal(50, play.MustHonor.Volume);
        Assert.True(play.MustHonor.ShowController);
        Assert.Equal(1, play.MustHonor.Fit);
        Assert.False(play.MustHonor.AutoPlay);
        Assert.Equal(0, play.MustHonor.RepeatCount);
        Assert.Equal("T", play.MustHonor.Duration?.Subtype);
        Assert.Equal(10, play.MustHonor.Duration?.Seconds);
        Assert.Equal(100, play.BestEffort.Volume);
        Assert.False(play.BestEffort.ShowController);
        Assert.Equal(5, play.BestEffort.Fit);
        Assert.True(play.BestEffort.AutoPlay);
        Assert.Equal(1.0, play.BestEffort.RepeatCount);

        var screen = Assert.IsType<PdfMediaScreenParameters>(media.ScreenParameters);
        Assert.Equal(0, screen.MustHonor.WindowType);
        Assert.Equal(0.5, screen.MustHonor.Opacity);
        Assert.Equal(2, screen.MustHonor.Monitor);
        Assert.NotNull(screen.MustHonor.FloatingWindow);
        Assert.Equal(9, screen.BestEffort.WindowType);
        Assert.Equal(1.0, screen.BestEffort.Opacity);
    }

    [Fact]
    public void Malformed_renditions_remain_inspectable_and_cyclic_clips_terminate()
    {
        var action = PdfDocument.Load(MultimediaPdf()).GetRenditionActions()[1];

        Assert.Equal(4, action.Operation);
        var rendition = Assert.IsType<PdfRendition>(action.Rendition);
        Assert.Null(rendition.Name);
        Assert.Null(rendition.MustHonorCriteria);
        Assert.Null(rendition.PlayParameters);
        Assert.Null(rendition.ScreenParameters);
        Assert.Empty(rendition.Renditions);
        var clip = Assert.IsType<PdfMediaClip>(rendition.Clip);
        Assert.Null(clip.Next);
        Assert.Equal("TEMPNEVER", clip.TemporaryFilePermission);
        Assert.Equal(7, Assert.IsType<PdfInteger>(rendition.Dictionary.Get("P")).Value);
    }

    [Fact]
    public void Screen_sound_and_movie_annotations_are_typed_without_playback()
    {
        var doc = PdfDocument.Load(MultimediaPdf());

        var screens = doc.GetScreenAnnotations();
        Assert.Equal(2, screens.Count);
        Assert.Equal("Clip screen", screens[0].Title);
        Assert.NotNull(screens[0].AppearanceCharacteristics);
        Assert.IsType<PdfDictionary>(screens[0].Action);

        var sound = Assert.Single(doc.GetSoundAnnotations());
        Assert.Equal("Speaker", sound.IconName);
        Assert.Equal(22050, sound.Sound?.SamplingRate);
        Assert.Equal(2, sound.Sound?.Channels);
        Assert.Equal(8, sound.Sound?.BitsPerSample);
        Assert.Equal("Signed", sound.Sound?.Encoding);
        Assert.Equal("ADPCM", sound.Sound?.Compression);
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(sound.Sound!.Stream.RawData));

        var movie = Assert.Single(doc.GetMovieAnnotations());
        Assert.Equal("m", movie.Title);
        Assert.True(movie.PlayOnActivation);
        Assert.Equal(90, movie.Movie?.Rotate);
        Assert.IsType<PdfBoolean>(movie.Movie?.Poster);
        Assert.Equal(2, movie.Movie?.Aspect?.Count);
        Assert.Equal(-1, movie.Activation?.Rate);
        Assert.Equal(0.25, movie.Activation?.Volume);
        Assert.True(movie.Activation?.ShowControls);
        Assert.Equal("Palindrome", movie.Activation?.Mode);
        Assert.False(movie.Activation?.Synchronous);
        Assert.Equal(5, Assert.IsType<PdfInteger>(movie.Activation?.Start).Value);
    }

    [Fact]
    public void Three_d_annotation_exposes_stream_views_activation_and_view_dictionaries()
    {
        var annotation = Assert.Single(PdfDocument.Load(MultimediaPdf()).Get3DAnnotations());

        Assert.False(annotation.Interactive);
        Assert.Equal(4, annotation.ViewBox?.Count);
        var activation = Assert.IsType<Pdf3DActivation>(annotation.ActivationDetails);
        Assert.Equal("PV", activation.Activate);
        Assert.Equal("I", activation.ArtworkInstanceState);
        Assert.Equal("XD", activation.Deactivate);
        Assert.Equal("L", activation.DeactivatedState);
        Assert.False(activation.Toolbar);
        Assert.True(activation.NavigationPanel);

        var stream = Assert.IsType<Pdf3DStream>(annotation.StreamDetails);
        Assert.True(stream.ViaReference);
        Assert.Equal("PRC", stream.Subtype);
        Assert.True(stream.IsRecognizedSubtype);
        Assert.Equal(new[] { "first", "second" }, stream.Views.Select(v => v.ExternalName));
        Assert.Equal("abc", System.Text.Encoding.ASCII.GetString(stream.Stream.RawData));
        Assert.Equal("alert(1)", System.Text.Encoding.ASCII.GetString(Assert.IsType<PdfStream>(stream.OnInstantiate).RawData));
        Assert.Equal("Oscillating", stream.AnimationStyle?.Subtype);
        Assert.Equal(-1, stream.AnimationStyle?.PlayCount);
        Assert.Equal(2, stream.AnimationStyle?.TimeMultiplier);

        Assert.Equal("second", annotation.ResolvedDefaultView?.ExternalName);
        Assert.Equal("U3D", annotation.ResolvedDefaultView?.MatrixSource);
        Assert.Equal("Root", Assert.IsType<PdfString>(annotation.ResolvedDefaultView?.U3DPath).Text);

        var view = stream.Views[0];
        Assert.Equal("O", view.ProjectionDetails?.Subtype);
        Assert.Equal("XNF", view.ProjectionDetails?.ClippingStyle);
        Assert.Equal(2, view.ProjectionDetails?.OrthographicScale);
        Assert.Equal("W", view.ProjectionDetails?.OrthographicBinding);
        Assert.Equal("SC", view.BackgroundDetails?.Subtype);
        Assert.Equal("DeviceRGB", view.BackgroundDetails?.ColorSpace);
        Assert.True(view.BackgroundDetails?.EntireAnnotation);
        Assert.Equal("Wireframe", view.RenderModeDetails?.Subtype);
        Assert.Equal(0.5, view.RenderModeDetails?.Opacity);
        Assert.Equal(45, view.RenderModeDetails?.CreaseValue);
        Assert.Equal("BG", Assert.IsType<PdfName>(view.RenderModeDetails?.FaceColor).Value);
        Assert.Equal("Night", view.LightingSchemeDetails?.Subtype);
        Assert.Equal("LD3", Assert.Single(view.MeasureDetails).Subtype);
        var cross = Assert.Single(view.CrossSectionDetails);
        Assert.Equal(0.25, cross.Opacity);
        Assert.True(cross.IntersectionVisible);
        Assert.Same(PdfNull.Instance, cross.Orientation![0]);
        var node = Assert.Single(view.NodeDetails);
        Assert.Equal("Cone", node.Name);
        Assert.Equal(0.5, node.Opacity);
        Assert.False(node.Visible);
        Assert.Equal(12, node.Matrix?.Count);
    }

    static PdfDocument SelectorPdf(string[] objects, string root)
    {
        var pdf = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Annots [4 0 R] >>\nendobj\n" +
            $"4 0 obj\n<< /Type /Annot /Subtype /Screen /Rect [0 0 1 1] /A << /S /Rendition /OP 0 /R {root} >> >>\nendobj\n" +
            string.Concat(objects) +
            "trailer\n<< /Root 1 0 R /Size 5000 >>\n%%EOF\n";
        return PdfDocument.Load(System.Text.Encoding.ASCII.GetBytes(pdf));
    }

    [Fact]
    public void Fan_out_selector_dag_is_memoized_bounded_and_reports_truncation()
    {
        // 70 levels, each listing the next level twice: 2^70 paths without memoization.
        var objects = Enumerable.Range(0, 70)
            .Select(i => $"{100 + i} 0 obj\n<< /S /SR /R [{101 + i} 0 R {101 + i} 0 R] >>\nendobj\n")
            .Append("170 0 obj\n<< /S /MR /N (leaf) >>\nendobj\n")
            .ToArray();
        var rendition = Assert.IsType<PdfRendition>(SelectorPdf(objects, "100 0 R").GetRenditionActions()[0].Rendition);

        Assert.True(rendition.Truncated);
        Assert.Equal(2, rendition.Renditions.Count);
        Assert.Same(rendition.Renditions[0], rendition.Renditions[1]);
    }

    [Fact]
    public void Wide_selector_exceeding_node_budget_is_truncated_with_raw_children_kept()
    {
        var children = string.Join(' ', Enumerable.Range(0, 1500).Select(i => $"{200 + i} 0 R"));
        var objects = new[] { $"100 0 obj\n<< /S /SR /R [{children}] >>\nendobj\n" }
            .Concat(Enumerable.Range(0, 1500).Select(i => $"{200 + i} 0 obj\n<< /S /MR >>\nendobj\n"))
            .ToArray();
        var rendition = Assert.IsType<PdfRendition>(SelectorPdf(objects, "100 0 R").GetRenditionActions()[0].Rendition);

        Assert.True(rendition.Truncated);
        Assert.True(rendition.Renditions.Count < 1500);
        Assert.Equal(1500, Assert.IsType<PdfArray>(rendition.Dictionary.Get("R")).Count);
    }

    [Fact]
    public void Shared_children_are_reused_and_cycles_are_not_truncation()
    {
        var objects = new[]
        {
            "100 0 obj\n<< /S /SR /R [101 0 R 102 0 R] >>\nendobj\n",
            "101 0 obj\n<< /S /SR /R [103 0 R 100 0 R] >>\nendobj\n",
            "102 0 obj\n<< /S /SR /R [103 0 R] >>\nendobj\n",
            "103 0 obj\n<< /S /MR /N (shared) >>\nendobj\n",
        };
        var rendition = Assert.IsType<PdfRendition>(SelectorPdf(objects, "100 0 R").GetRenditionActions()[0].Rendition);

        Assert.False(rendition.Truncated);
        Assert.Same(rendition.Renditions[0].Renditions[0], rendition.Renditions[1].Renditions[0]);
        Assert.Single(rendition.Renditions[0].Renditions);
    }

    [Fact]
    public void Round_trip_preserves_multimedia_and_three_d_metadata()
    {
        var doc = PdfDocument.Load(MultimediaPdf());
        var roundTrip = PdfDocument.Load(PdfEditor.Open(doc).Save());

        Assert.Equal(2, roundTrip.GetRenditionActions().Count);
        var selector = Assert.IsType<PdfRendition>(roundTrip.GetRenditionActions()[0].Rendition);
        Assert.Equal("video/mp4", Assert.Single(selector.Renditions).Clip?.Next?.ContentType);
        Assert.Equal(22050, Assert.Single(roundTrip.GetSoundAnnotations()).Sound?.SamplingRate);
        Assert.Equal("Palindrome", Assert.Single(roundTrip.GetMovieAnnotations()).Activation?.Mode);
        var threeD = Assert.Single(roundTrip.Get3DAnnotations());
        Assert.Equal("PRC", threeD.StreamDetails?.Subtype);
        Assert.Equal("second", threeD.ResolvedDefaultView?.ExternalName);
    }
}
