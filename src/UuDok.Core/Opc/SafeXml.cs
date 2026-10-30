using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UuDok.Core.Opc;

/// <summary>
/// XML-lasting uten DTD/eksterne entiteter (XXE) og med størrelsesgrense,
/// og serialisering som beholder original XML-deklarasjon og innledende bytes.
/// </summary>
public static class SafeXml
{
    public const long MaxChars = 400L * 1024 * 1024;

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxChars,
        MaxCharactersFromEntities = 0,
        IgnoreWhitespace = false,
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        CloseInput = true,
    };

    public static XDocument Load(byte[] data)
    {
        try
        {
            using var r = XmlReader.Create(new MemoryStream(data, writable: false), Settings());
            return XDocument.Load(r, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase))
        {
            throw new PackageRejectedException("xml_dtd", "Filen inneholder XML med DTD, som ikke er tillatt (mulig XXE-angrep).");
        }
        catch (XmlException)
        {
            throw new PackageRejectedException("xml_invalid", "Filen inneholder ugyldig XML.");
        }
    }

    /// <summary>
    /// Serialiserer <paramref name="doc"/> og gjenbruker bytene foran rotelementet fra
    /// <paramref name="original"/> (BOM, deklarasjon, linjeskift), slik Office skrev dem.
    /// </summary>
    public static byte[] Save(XDocument doc, byte[] original)
    {
        var prefixLen = RootStartOffset(original);
        var prefix = original.AsSpan(0, prefixLen);
        var decl = Encoding.UTF8.GetString(prefix);
        if (decl.Contains("encoding=", StringComparison.OrdinalIgnoreCase) &&
            !decl.Contains("utf-8", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Bare UTF-8-kodet XML kan endres.");

        using var ms = new MemoryStream();
        ms.Write(prefix);
        var ws = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = true,
            Indent = false,
            // Entitize: linjeskift i attributter (f.eks. Words o:gfxdata) skrives som &#xA; og overlever
            // attributtnormalisering ved ny innlesing. «None» gjorde dem om til mellomrom (fanget av rundtur-kontrollen).
            NewLineHandling = NewLineHandling.Entitize,
            ConformanceLevel = ConformanceLevel.Fragment,
        };
        using (var w = XmlWriter.Create(ms, ws))
        {
            // Noder foran roten (deklarasjon/kommentarer) ligger allerede i prefix.
            var root = doc.Root ?? throw new InvalidOperationException("XML uten rotelement.");
            root.WriteTo(w);
            foreach (var n in root.NodesAfterSelf()) n.WriteTo(w);
        }
        return ms.ToArray();
    }

    /// <summary>Byte-posisjon der første element (etter deklarasjon/kommentarer) starter.</summary>
    public static int RootStartOffset(byte[] data)
    {
        int i = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) i = 3;
        while (i < data.Length)
        {
            if (data[i] == '<' && i + 1 < data.Length)
            {
                if (data[i + 1] == '?') { i = IndexOf(data, "?>"u8, i) + 2; continue; }
                if (data[i + 1] == '!') { i = IndexOf(data, "-->"u8, i) + 3; continue; }
                return i;
            }
            i++;
        }
        return 0;
    }

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pat, int start)
    {
        var idx = data.AsSpan(start).IndexOf(pat);
        if (idx < 0) throw new PackageRejectedException("xml_invalid", "Filen inneholder ugyldig XML.");
        return start + idx;
    }

    public static string Fingerprint(XElement e) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(e.ToString(SaveOptions.DisableFormatting))))[..32];
}
