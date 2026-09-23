"""Synthetic NuGet metadata regressions; no network, SDK, fonts or real assemblies needed."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
INSPECTOR = ROOT / "scripts/inspect-packages.py"
VERSION = "0.2.0-alpha.2"
DEPENDENCIES = {
    "Kypelon.Pdf.Core": [],
    "Kypelon.Pdf.Graphics": ["Kypelon.Pdf.Core"],
    "Kypelon.Pdf.Text": ["Kypelon.Pdf.Core"],
    "Kypelon.Pdf.Layout": ["Kypelon.Pdf.Graphics", "Kypelon.Pdf.Text"],
    "Kypelon.Pdf": ["Kypelon.Pdf.Core", "Kypelon.Pdf.Graphics", "Kypelon.Pdf.Text", "Kypelon.Pdf.Layout"],
    "Kypelon.Pdf.AspNetCore": ["Kypelon.Pdf"],
}
README = "# Kypelon\n\nNative PDF & Document Engine for .NET\n\n**by Konkaew**\n"


class PackageReadmeTests(unittest.TestCase):
    def inspect(self, readme):
        base = ROOT / "artifacts"
        base.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="package-inspection-tests-", dir=base) as directory:
            workspace = Path(directory).resolve()
            self.assertTrue(workspace.is_relative_to(base.resolve()))
            feed = workspace / "artifacts/packages"
            feed.mkdir(parents=True)
            for name, dependencies in DEPENDENCIES.items():
                package = ET.Element("package", xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")
                metadata = ET.SubElement(package, "metadata")
                fields = {
                    "id": name, "version": VERSION, "title": name, "authors": "Konkaew",
                    "description": "Native C# PDF and document layout engine for .NET.",
                    "readme": "README.md", "tags": "pdf document layout csharp dotnet report unicode thai aspnetcore",
                    "releaseNotes": "First public Kypelon.Pdf alpha; PreparedPdfDocument; deterministic preparation; no GSUB/GPOS, bidi, advanced Thai, Reader/Edit, PDF/A, PDF/UA.",
                }
                for field, value in fields.items():
                    ET.SubElement(metadata, field).text = value
                ET.SubElement(metadata, "license", type="expression").text = "MIT"
                groups = ET.SubElement(metadata, "dependencies")
                for tfm in ("net8.0", "net10.0"):
                    group = ET.SubElement(groups, "group", targetFramework=tfm)
                    for dependency in dependencies:
                        ET.SubElement(group, "dependency", id=dependency, version=VERSION)
                with ZipFile(feed / f"{name}.{VERSION}.nupkg", "w") as archive:
                    archive.writestr(name + ".nuspec", ET.tostring(package, encoding="utf-8"))
                    if readme is not None:
                        archive.writestr("README.md", readme)
                    for tfm in ("net8.0", "net10.0"):
                        archive.writestr(f"lib/{tfm}/{name}.dll", b"SYNTHETIC METADATA TEST FIXTURE")
                        archive.writestr(f"lib/{tfm}/{name}.xml", f"<doc><assembly><name>{name}</name></assembly></doc>")
                with ZipFile(feed / f"{name}.{VERSION}.snupkg", "w") as archive:
                    for tfm in ("net8.0", "net10.0"):
                        archive.writestr(f"lib/{tfm}/{name}.pdb", b"BSJB SYNTHETIC METADATA TEST FIXTURE")
            result = subprocess.run([sys.executable, str(INSPECTOR)], cwd=workspace,
                                    env={**os.environ, "PYTHONUTF8": "1"}, capture_output=True, text=True, encoding="utf-8")
            self.assertTrue(workspace.is_relative_to(base.resolve()))
            return result

    def test_lf_readme_passes_all_six_packages(self):
        result = self.inspect(README.encode("utf-8"))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Publication order:", result.stdout)

    def test_windows_crlf_readme_passes_all_six_packages(self):
        result = self.inspect(README.replace("\n", "\r\n").encode("utf-8"))
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Publication order:", result.stdout)

    def test_wrong_heading_is_rejected_with_lf_and_crlf(self):
        for newline in ("\n", "\r\n"):
            with self.subTest(newline=repr(newline)):
                result = self.inspect(README.replace("# Kypelon", "# Wrong product").replace("\n", newline).encode("utf-8"))
                self.assertNotEqual(result.returncode, 0)

    def test_missing_signature_is_rejected_with_lf_and_crlf(self):
        for newline in ("\n", "\r\n"):
            with self.subTest(newline=repr(newline)):
                result = self.inspect(README.replace("**by Konkaew**", "").replace("\n", newline).encode("utf-8"))
                self.assertNotEqual(result.returncode, 0)

    def test_prepublication_status_is_rejected(self):
        readme=README+"\nNuGet.org publication is pending.\n"
        self.assertNotEqual(self.inspect(readme.encode("utf-8")).returncode, 0)

    def test_pending_public_installation_instructions_are_rejected(self):
        readme=README+"\nThe versioned NuGet commands below target the pending public release.\n"
        self.assertNotEqual(self.inspect(readme.encode("utf-8")).returncode, 0)

    def test_missing_readme_is_rejected(self):
        self.assertNotEqual(self.inspect(None).returncode, 0)

    def test_invalid_utf8_readme_is_rejected(self):
        self.assertNotEqual(self.inspect(b"\xff" + README.encode("utf-8")).returncode, 0)


if __name__ == "__main__":
    unittest.main()
