using System.Text;
using UuDok.Core.Opc;
using UuDok.Intake;

namespace UuDok.Tests;

/// <summary>Filinntak og sikkerhetskontroll: innhold kontrolleres, ikke filendelse.</summary>
public class PackageSecurityTests
{
    [Theory]
    [InlineData("ondsinnet/zip-slip.docx", "path_traversal")]
    [InlineData("ondsinnet/zip-bombe.docx", "zip_bomb")]
    [InlineData("ondsinnet/for-mange-deler.docx", "too_many_parts")]
    [InlineData("ondsinnet/xxe.docx", "xml_dtd")]
    [InlineData("ondsinnet/kryptert-eller-doc.docx", "cfb")]
    [InlineData("ondsinnet/makro-forkledd.docx", "unsupported_variant")]
    [InlineData("ondsinnet/tekst-forkledd.docx", "not_ooxml")]
    [InlineData("ondsinnet/avkuttet.docx", "not_zip")]
    public void Ondsinnede_og_ustottede_filer_avvises_med_forklaring(string file, string code)
    {
        var r = FileInspector.Inspect(Corpus.Bytes(file));
        Assert.False(r.Accepted);
        Assert.Equal(code, r.RejectCode);
        Assert.False(string.IsNullOrWhiteSpace(r.RejectMessage));
    }

    [Fact]
    public void Signert_dokument_godtas_for_analyse_men_flagges()
    {
        var r = FileInspector.Inspect(Corpus.Bytes("ondsinnet/signert.docx"));
        Assert.True(r.Accepted);
        Assert.True(r.IsSigned);
        Assert.Contains(r.Notes, n => n.Contains("signert"));
    }

    [Theory]
    [InlineData("office-presentasjon.pptx", UuDok.Core.DocumentKind.Pptx)]
    [InlineData("office-dokument.docx", UuDok.Core.DocumentKind.Docx)]
    [InlineData("sdk-mangler-mye.docx", UuDok.Core.DocumentKind.Docx)]
    public void Gyldige_filer_godtas_og_type_bestemmes_av_innhold(string file, UuDok.Core.DocumentKind kind)
    {
        var r = FileInspector.Inspect(Corpus.Bytes(file));
        Assert.True(r.Accepted, r.RejectMessage);
        Assert.Equal(kind, r.Kind);
    }

    [Fact]
    public void Ekstern_relasjon_registreres_men_hentes_ikke()
    {
        using var spy = new NetworkSpy();
        var r = FileInspector.Inspect(Corpus.Bytes("ekstern-relasjon.docx"));
        Assert.True(r.Accepted);
        Assert.Single(r.ExternalTargets);
        Assert.Empty(spy.Requests);
    }

    [Theory]
    [InlineData("../x.xml")]
    [InlineData("/abs.xml")]
    [InlineData("a/../../b.xml")]
    [InlineData("C:/windows/x.xml")]
    public void Stitraversering_i_zip_avvises(string name)
    {
        var zip = Corpus.Zip(("[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'/>"), (name, "<a/>"));
        var ex = Assert.Throws<PackageRejectedException>(() => OpcPackage.Load(new MemoryStream(zip)));
        Assert.Equal("path_traversal", ex.Code);
    }

    [Fact]
    public void Dupliserte_pakkedeler_avvises()
    {
        var zip = Corpus.Zip(("a.xml", "<a/>"), ("A.xml", "<a/>"));
        Assert.Equal("duplicate_part", Assert.Throws<PackageRejectedException>(() => OpcPackage.Load(new MemoryStream(zip))).Code);
    }

    [Fact]
    public void Utpakket_storrelse_kontrolleres_uavhengig_av_oppgitt_lengde()
    {
        var limits = new PackageLimits { MaxTotalUncompressed = 1000, MaxEntryUncompressed = 1000 };
        var zip = Corpus.Zip(("a.xml", new string('x', 5000)));
        Assert.Equal("zip_bomb", Assert.Throws<PackageRejectedException>(() => OpcPackage.Load(new MemoryStream(zip), limits)).Code);
    }

    [Fact]
    public void Xml_med_ekstern_entitet_lastes_aldri()
    {
        var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><a>&e;</a>");
        Assert.Equal("xml_dtd", Assert.Throws<PackageRejectedException>(() => SafeXml.Load(xml)).Code);
    }

    [Fact]
    public void Serialisering_bevarer_deklarasjon_og_linjeskift_i_attributter()
    {
        // Regresjon fra bevis 1: Word skriver &#xA; i o:gfxdata. Rå linjeskift ville blitt mellomrom ved ny innlesing.
        var orig = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n<a x=\"1&#xA;2\" xmlns:q=\"urn:q\"><q:b>t</q:b></a>");
        var doc = SafeXml.Load(orig);
        var saved = SafeXml.Save(doc, orig);
        var s = Encoding.UTF8.GetString(saved);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n", s);
        Assert.Equal("1\n2", (string?)SafeXml.Load(saved).Root!.Attribute("x"));
        Assert.Empty(UuDok.Patching.SemanticDiff.Compare(doc, SafeXml.Load(saved)));
    }
}
