using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopTools.Core;

/// <summary>What the user accepted for one pack: the revision, the licence and the exact licence text shown (spec 2.4).</summary>
public sealed record ModelConsentRecord(string PackId, string Revision, string LicenseId, string LicenseSha256, DateTimeOffset AcceptedAt, string AppVersion);

/// <summary>Local record of licence acceptances in <c>consents.json</c>. Never transmitted; written atomically.</summary>
public sealed class ModelConsentStore
{
    private readonly string _directory;
    private readonly string _path;
    private readonly object _gate = new();
    private List<ModelConsentRecord> _records;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public ModelConsentStore(string directory)
    {
        _directory = directory;
        _path = Path.Combine(directory, "consents.json");
        _records = Load();
    }

    /// <summary>SHA-256 (lowercase hex) of the UTF-8 licence text, with Windows line endings normalized so a checkout setting cannot ask again.</summary>
    public static string LicenseHash(string licenseText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(licenseText.Replace("\r\n", "\n")))).ToLowerInvariant();

    public IReadOnlyList<ModelConsentRecord> All { get { lock (_gate) return _records.ToArray(); } }

    public bool IsCurrent(ModelPack pack, string licenseText)
    {
        string hash = LicenseHash(licenseText);
        lock (_gate)
            return _records.Any(r => r.PackId == pack.Id && r.Revision == pack.Revision && r.LicenseId == pack.LicenseId && r.LicenseSha256 == hash);
    }

    public void Accept(ModelPack pack, string licenseText, DateTimeOffset now, string appVersion)
    {
        lock (_gate)
        {
            var next = _records.Where(r => r.PackId != pack.Id)
                .Append(new ModelConsentRecord(pack.Id, pack.Revision, pack.LicenseId, LicenseHash(licenseText), now, appVersion)).ToList();
            Write(next);
            _records = next;
        }
    }

    public void Remove(string packId)
    {
        lock (_gate)
        {
            if (_records.All(r => r.PackId != packId)) return;
            var next = _records.Where(r => r.PackId != packId).ToList();
            Write(next);
            _records = next;
        }
    }

    private List<ModelConsentRecord> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var records = JsonSerializer.Deserialize<ModelConsentRecord?[]>(File.ReadAllText(_path), Options) ?? throw new JsonException("empty");
            if (records.Any(r => r == null || string.IsNullOrEmpty(r.PackId) || string.IsNullOrEmpty(r.LicenseSha256))) throw new JsonException("incomplete record");
            return records.Select(r => r!).ToList();
        }
        catch (JsonException)
        {
            // An unreadable record means "ask again". Keep the original for inspection instead of discarding it.
            try { File.Move(_path, _path + ".invalid", overwrite: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    private void Write(List<ModelConsentRecord> records)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, $"consents-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records, Options));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
