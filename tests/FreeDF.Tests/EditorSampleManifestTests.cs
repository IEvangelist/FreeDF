using System.Security.Cryptography;
using System.Text.Json;
using FreeDF.Samples;
using Xunit;

namespace FreeDF.Tests;

/// <summary>The Pages publish ships the samples as lazily fetched assets listed by manifest.json; the browser verifies each file against it.</summary>
public class EditorSampleManifestTests(GeneratedSamples gen) : IClassFixture<GeneratedSamples>
{
    [Fact]
    public void ManifestLeadsWithTheShowcaseThenFullCollectionAndDescribesEveryFile()
    {
        var entries = SampleManifest.Entries(gen.Dir);
        Assert.Equal(28, entries.Count);
        Assert.Equal("28-benchmarks-brand-spec", entries[0].Name);
        Assert.Equal(18, entries[0].Pages);
        Assert.Equal("27-full-collection", entries[1].Name);
        Assert.Equal(50, entries[1].Pages);
        Assert.Equal(entries.Skip(2).Select(e => e.Name).Order(StringComparer.Ordinal), entries.Skip(2).Select(e => e.Name));
        foreach (var e in entries)
        {
            var bytes = File.ReadAllBytes(Path.Combine(gen.Dir, e.File));
            Assert.Equal(e.Name + ".pdf", e.File);
            Assert.Equal(bytes.Length, e.Size);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), e.Sha256);
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.True(e.Pages > 0);
        }
        Assert.Equal(["17-encrypted-user-password", "18-encrypted-owner-restricted"], entries.Where(e => e.Encrypted).Select(e => e.Name));
        Assert.Equal(["17-encrypted-user-password"], entries.Where(e => e.RequiresPassword).Select(e => e.Name));
    }

    [Fact]
    public void ManifestJsonIsStableAndMatchesWhatTheLoaderReads()
    {
        var entries = SampleManifest.Entries(gen.Dir);
        var json = SampleManifest.ToJson(entries);
        Assert.Equal(json, SampleManifest.ToJson(SampleManifest.Entries(gen.Dir)));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("version").GetInt32());
        var samples = doc.RootElement.GetProperty("samples").EnumerateArray().ToList();
        Assert.Equal(entries.Count, samples.Count);
        foreach (var (s, e) in samples.Zip(entries))
        {
            Assert.Equal(e.Name, s.GetProperty("name").GetString());
            Assert.Equal(e.File, s.GetProperty("file").GetString());
            Assert.Equal(e.Size, s.GetProperty("size").GetInt64());
            Assert.Matches("^[0-9a-f]{64}$", s.GetProperty("sha256").GetString()!);
        }
    }

    [Fact]
    public void FullCollectionIsReproducible()
    {
        var other = Path.Combine(Path.GetTempPath(), "samples-repro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other);
        try
        {
            // sample 28 sits beside the sources but must never change the collection
            foreach (var f in gen.Sources.Concat(gen.Standalone)) File.Copy(Path.Combine(gen.Dir, f), Path.Combine(other, f));
            SampleGenerator.Generate(other, "27-");
            Assert.True(File.ReadAllBytes(gen.Full).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(other, "27-full-collection.pdf"))));
        }
        finally { Directory.Delete(other, true); }
    }

    [Fact]
    public void PublishCopiesFilesBesideTheManifestAndRequiresTheFullCollection()
    {
        var site = Path.Combine(Path.GetTempPath(), "samples-site-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entries = SampleManifest.Publish(gen.Dir, site);
            Assert.Equal(entries.Count + 1, Directory.GetFiles(site).Length);
            Assert.Equal(SampleManifest.ToJson(entries), File.ReadAllText(Path.Combine(site, "manifest.json")));
            foreach (var e in entries) Assert.Equal(e.Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(site, e.File)))));

            var incomplete = Path.Combine(site, "no-full");
            Directory.CreateDirectory(incomplete);
            File.Copy(Path.Combine(gen.Dir, "01-spec-showcase.pdf"), Path.Combine(incomplete, "01-spec-showcase.pdf"));
            Assert.Throws<FileNotFoundException>(() => SampleManifest.Publish(incomplete, Path.Combine(site, "out")));
        }
        finally { Directory.Delete(site, true); }
    }
}
