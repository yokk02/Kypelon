"""Independent source-cluster extraction regressions. Run after Release xUnit tests."""
import json
from pathlib import Path
import pypdf
import pymupdf

root = Path(__file__).resolve().parent.parent
results = []
for framework in ("net8.0", "net10.0"):
    folder = root / "tests/Kypelon.Pdf.Tests/bin/Release" / framework / "text-pipeline-pdfs"
    cases = ("ligature", "one-to-many", "reordered", "surrogate", "combining", "thai", "interleaved", "thai-reordered", "ligature-native")
    for name in cases:
        pdf = folder / (name + ".pdf")
        expected = pdf.with_suffix(".txt").read_text(encoding="utf-8-sig")
        reader = pypdf.PdfReader(pdf, strict=True)
        fallback = "\n".join(p.extract_text() for p in reader.pages).strip()
        doc = pymupdf.open(pdf)
        assert not doc.is_repaired, (framework, name, "repair required")
        actual = "\n".join(p.get_text(sort=False).rstrip("\n") for p in doc)
        assert actual == expected, (framework, name, repr(expected), repr(actual))
        for page in doc:
            page.get_pixmap(matrix=pymupdf.Matrix(1, 1))
        if name not in ("one-to-many", "reordered", "interleaved", "thai-reordered"):
            assert fallback == expected, (framework, name, "ToUnicode", repr(fallback))
        target = root / "artifacts/release-alpha.2/extraction" / framework
        target.mkdir(parents=True, exist_ok=True)
        (target / pdf.name).write_bytes(pdf.read_bytes())
        results.append(dict(framework=framework, case=name, pages=len(doc), actualTextReaderPassed=True,
                            pypdfExact=fallback == expected, pypdfText=fallback, expected=expected))
output = root / "artifacts/release-alpha.2/extraction/results.json"
output.write_text(json.dumps(dict(pypdf=pypdf.__version__, pymupdf=pymupdf.VersionBind, cases=results), ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(results, ensure_ascii=False, indent=2))
