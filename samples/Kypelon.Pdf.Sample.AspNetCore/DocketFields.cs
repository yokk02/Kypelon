using System.Globalization;

namespace Kypelon.Pdf.Sample.AspNetCore;

// Synthetic workflow inputs for the playground, not a production approval rules engine.
public sealed record DocketFields
{
    public string Id { get; init; } = "EDK-2026-TH-004218";
    public string EngagementCode { get; init; } = "TH-AUD-ARH-2026";
    public string BusinessUnit { get; init; } = "Audit & Assurance";
    public string Country { get; init; } = "Thailand";
    public string Partner { get; init; } = "Ananda Charoen";
    public string Manager { get; init; } = "Pimchanok Wattanakul";
    public string ArchiveDeadline { get; init; } = "2026-09-30";
    public string Repository { get; init; } = "EMS / Levvia (sample)";
    public string AatId { get; init; } = "AAT-TH-2026-004218";
    public string GeneratedAt { get; init; } = "2026-09-23T09:00";
    public int SourceVersion { get; init; } = 42;
    public string Stage { get; init; } = "pending";

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, value, max) in new[]
        {
            ("id", Id, 80), ("engagementCode", EngagementCode, 80), ("businessUnit", BusinessUnit, 100),
            ("country", Country, 80), ("partner", Partner, 120), ("manager", Manager, 120),
            ("repository", Repository, 160), ("aatId", AatId, 80)
        })
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) errors["docket." + key] = [$"{key} ต้องมี 1–{max} ตัวอักษร"];
        if (!DateOnly.TryParseExact(ArchiveDeadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors["docket.archiveDeadline"] = ["วันที่กำหนดจัดเก็บไม่ถูกต้อง"];
        if (!DateTime.TryParseExact(GeneratedAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            errors["docket.generatedAt"] = ["วันเวลาสร้างเอกสารไม่ถูกต้อง"];
        if (SourceVersion is < 1 or > 999999) errors["docket.sourceVersion"] = ["Source version ต้องอยู่ระหว่าง 1–999,999"];
        if (Stage is not ("pending" or "ready" or "archived")) errors["docket.stage"] = ["เลือกสถานการณ์ตัวอย่างที่รองรับ"];
        return errors;
    }
}
