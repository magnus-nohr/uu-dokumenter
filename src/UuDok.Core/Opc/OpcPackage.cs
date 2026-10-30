using System.IO.Compression;
using System.Security.Cryptography;

namespace UuDok.Core.Opc;

public sealed record PackageLimits
{
    public long MaxInputBytes { get; init; } = 500L * 1024 * 1024;        // 500 MB fil
    public long MaxTotalUncompressed { get; init; } = 2L * 1024 * 1024 * 1024; // 2 GB utpakket
    public long MaxEntryUncompressed { get; init; } = 512L * 1024 * 1024;
    public int MaxEntries { get; init; } = 20_000;
    public double MaxCompressionRatio { get; init; } = 200;              // per oppføring > 1 MB
    public static PackageLimits Default { get; } = new();
}

public sealed class PackageRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class OpcEntry
{
    public required string Name { get; init; }           // zip-navn uten innledende «/»
    public required byte[] Data { get; init; }
    public DateTimeOffset LastWriteTime { get; init; }
    public bool IsDirectory => Name.EndsWith('/');
    public string PartUri => "/" + Name;
    public string Sha256 => Convert.ToHexString(SHA256.HashData(Data));
}

/// <summary>
/// Pakken lest helt inn i minnet som dekomprimerte bytes, med grenser mot ZIP-bomber og
/// stitraversering. Ingenting pakkes ut til disk.
/// </summary>
public sealed class OpcPackage
{
    private readonly List<OpcEntry> _entries;
    private readonly Dictionary<string, OpcEntry> _byName;

    private OpcPackage(List<OpcEntry> entries)
    {
        _entries = entries;
        _byName = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<OpcEntry> Entries => _entries;

    public bool Contains(string partUri) => _byName.ContainsKey(Norm(partUri));
    public OpcEntry? Get(string partUri) => _byName.GetValueOrDefault(Norm(partUri));
    public byte[] GetBytes(string partUri) =>
        Get(partUri)?.Data ?? throw new KeyNotFoundException($"Pakkedel mangler: {partUri}");

    public static string Norm(string partUri) => Uri.UnescapeDataString(partUri.TrimStart('/'));

    public static OpcPackage Load(Stream input, PackageLimits? limits = null)
    {
        limits ??= PackageLimits.Default;
        if (input.CanSeek && input.Length > limits.MaxInputBytes)
            throw new PackageRejectedException("too_large", "Filen er større enn tillatt grense.");

        ZipArchive zip;
        try { zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true); }
        catch (InvalidDataException) { throw new PackageRejectedException("not_zip", "Filen er ikke en gyldig Office-pakke (ZIP)."); }

        using (zip)
        {
            if (zip.Entries.Count > limits.MaxEntries)
                throw new PackageRejectedException("too_many_parts", "Filen har for mange pakkedeler.");

            var list = new List<OpcEntry>(zip.Entries.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var e in zip.Entries)
            {
                ValidateName(e.FullName);
                if (!seen.Add(Uri.UnescapeDataString(e.FullName)))
                    throw new PackageRejectedException("duplicate_part", "Filen har dupliserte pakkedeler.");

                byte[] data;
                if (e.FullName.EndsWith('/')) data = [];
                else
                {
                    if (e.Length > limits.MaxEntryUncompressed)
                        throw new PackageRejectedException("zip_bomb", "En pakkedel er for stor utpakket.");
                    if (e.Length > 1024 * 1024 && e.CompressedLength > 0 &&
                        (double)e.Length / e.CompressedLength > limits.MaxCompressionRatio)
                        throw new PackageRejectedException("zip_bomb", "Filen har mistenkelig høy komprimeringsgrad (mulig ZIP-bombe).");
                    data = ReadBounded(e, Math.Min(limits.MaxEntryUncompressed, limits.MaxTotalUncompressed - total));
                }
                total += data.Length;
                if (total > limits.MaxTotalUncompressed)
                    throw new PackageRejectedException("zip_bomb", "Filen blir for stor når den pakkes ut.");
                list.Add(new OpcEntry { Name = e.FullName, Data = data, LastWriteTime = e.LastWriteTime });
            }
            return new OpcPackage(list);
        }
    }

    private static byte[] ReadBounded(ZipArchiveEntry e, long max)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            if (ms.Length + n > max)
                throw new PackageRejectedException("zip_bomb", "En pakkedel er større utpakket enn oppgitt (mulig ZIP-bombe).");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    internal static void ValidateName(string name)
    {
        var n = name.Replace('\\', '/');
        if (name.Contains('\\') || n.StartsWith('/') || n.Contains(':') || n.Contains('\0') ||
            n.Split('/').Any(seg => seg is ".." or "."))
            throw new PackageRejectedException("path_traversal", "Filen har ugyldige stier i pakken (mulig angrep).");
    }

    /// <summary>
    /// Skriver ny pakke: urørte deler får identiske dekomprimerte bytes og samme rekkefølge;
    /// bare deler i <paramref name="replacements"/> endres. Nye deler legges til sist.
    /// </summary>
    public void WriteTo(Stream output, IReadOnlyDictionary<string, byte[]> replacements,
        IReadOnlyList<(string Name, byte[] Data)>? additions = null)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            var entry = zip.CreateEntry(e.Name, CompressionLevel.Optimal);
            entry.LastWriteTime = e.LastWriteTime;
            if (e.IsDirectory) continue;
            var data = replacements.TryGetValue(e.PartUri, out var r) ? r : e.Data;
            if (replacements.ContainsKey(e.PartUri)) used.Add(e.PartUri);
            using var s = entry.Open();
            s.Write(data);
        }
        foreach (var k in replacements.Keys)
            if (!used.Contains(k)) throw new InvalidOperationException($"Erstatning for ukjent del: {k}");
        foreach (var (name, data) in additions ?? [])
        {
            ValidateName(name);
            if (Contains(name)) throw new InvalidOperationException($"Delen finnes allerede: {name}");
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(data);
        }
    }
}
