"""Regression tests for release-artifact gating. No network requests or actual credentials."""
import importlib.util,json,os,subprocess,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
spec=importlib.util.spec_from_file_location("publisher",Path(__file__).with_name("trusted-publishing.py"))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
class ReleaseGateTests(unittest.TestCase):
 def setUp(self):
  base=m.ROOT/"artifacts";base.mkdir(exist_ok=True)
  self.temp=tempfile.TemporaryDirectory(prefix="publisher-tests-",dir=base)
  self.root=Path(self.temp.name).resolve();self.assertTrue(self.root.is_relative_to(base.resolve()))
  self.saved=m.OUT;m.OUT=self.root
  (m.OUT/"packages").mkdir();(m.OUT/"evidence").mkdir()
  self.order=["Kypelon.Pdf.Core","Kypelon.Pdf.Graphics","Kypelon.Pdf.Text","Kypelon.Pdf.Layout","Kypelon.Pdf","Kypelon.Pdf.AspNetCore"]
  deps=[[],["Kypelon.Pdf.Core"],["Kypelon.Pdf.Core"],["Kypelon.Pdf.Graphics","Kypelon.Pdf.Text"],self.order[:4],["Kypelon.Pdf"]]
  packages=[]
  for name,dependencies in zip(self.order,deps):
   p={"id":name,"version":m.VERSION,"dependencies":dependencies}
   for field,hashfield,extension in [("package","sha256",".nupkg"),("symbols","symbolSha256",".snupkg")]:
    file=m.OUT/"packages"/(name+"."+m.VERSION+extension);file.write_bytes(("TEST FIXTURE "+file.name).encode())
    p[field]=file.name;p[hashfield]=m.SHA(file)
   packages.append(p)
  log=m.OUT/"evidence/passed.txt";log.write_text("test evidence",encoding="utf-8")
  self.data={"version":m.VERSION,"sourceCommit":"a"*40,"localGatesPassed":True,"packages":packages,
             "publicationOrder":self.order,"evidenceHashes":{"evidence/passed.txt":m.SHA(log)}}
  self.env=patch.dict(os.environ,{"GITHUB_SHA":"a"*40,"NUGET_API_KEY":"test-only-not-a-credential"})
  self.env.start();self.save()
 def save(self):
  file=m.OUT/"manifest.json";file.write_text(json.dumps(self.data),encoding="utf-8")
  os.environ["REVIEWED_MANIFEST_SHA256"]=m.SHA(file)
 def tearDown(self):
  self.env.stop();m.OUT=self.saved
  self.assertTrue(self.root.is_relative_to((m.ROOT/"artifacts").resolve()))
  self.temp.cleanup()
 def test_complete_reviewed_candidate_passes(self):self.assertEqual(m.review()["publicationOrder"],self.order)
 def test_missing_review_hash_fails(self):
  os.environ.pop("REVIEWED_MANIFEST_SHA256")
  with self.assertRaises(AssertionError):m.review()
 def test_wrong_review_hash_fails(self):
  os.environ["REVIEWED_MANIFEST_SHA256"]="0"*64
  with self.assertRaises(AssertionError):m.review()
 def test_wrong_commit_fails(self):
  os.environ["GITHUB_SHA"]="b"*40
  with self.assertRaises(AssertionError):m.review()
 def test_tampered_primary_fails(self):
  (m.OUT/"packages"/self.data["packages"][0]["package"]).write_bytes(b"CHANGED")
  with self.assertRaises(AssertionError):m.review()
 def test_tampered_symbols_fail(self):
  (m.OUT/"packages"/self.data["packages"][0]["symbols"]).write_bytes(b"CHANGED")
  with self.assertRaises(AssertionError):m.review()
 def test_tampered_evidence_fails(self):
  (m.OUT/"evidence/passed.txt").write_text("changed")
  with self.assertRaises(AssertionError):m.review()
 def test_dependent_before_dependency_fails(self):
  self.data["publicationOrder"]=list(reversed(self.order));self.save()
  with self.assertRaises(AssertionError):m.review()
 def test_extra_package_fails(self):
  self.data["packages"].append(dict(self.data["packages"][0]));self.save()
  with self.assertRaises(AssertionError):m.review()
 def test_duplicate_response_stops_before_dependents(self):
  with patch.object(m,"run",return_value="Package already exists") as call:
   with self.assertRaises(AssertionError):m.push()
   self.assertEqual(call.call_count,1)
 def test_push_error_stops_before_dependents(self):
  with patch.object(m,"run",side_effect=RuntimeError("Rejected")) as call:
   with self.assertRaises(RuntimeError):m.push()
   self.assertEqual(call.call_count,1)
 def test_output_redaction_applies_before_persisting(self):
  marker="test-only-not-a-credential"
  result=subprocess.CompletedProcess(["test"],0,"Fake output "+marker)
  log=m.OUT/"redaction.txt"
  with patch.object(m.subprocess,"run",return_value=result):
   text=m.run(["test"],log,secret=marker)
  self.assertNotIn(marker,text);self.assertNotIn(marker,log.read_text())
  self.assertIn("[REDACTED]",text)
if __name__=="__main__":unittest.main()
