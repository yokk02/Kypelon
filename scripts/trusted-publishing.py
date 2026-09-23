"""Manual alpha.2 CI verification and exact-artifact publishing. Never prints credentials."""
import argparse,hashlib,json,os,re,shutil,subprocess,sys,time,urllib.request,urllib.error
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile
ROOT=Path(__file__).resolve().parents[1]
os.chdir(ROOT)
os.environ["PYTHONUTF8"]="1"
os.environ["PYTHONIOENCODING"]="utf-8"
VERSION="0.2.0-alpha.2"
IDS={"Kypelon.Pdf"+s for s in ("",".Core",".Graphics",".Text",".Layout",".AspNetCore")}
OUT=ROOT/"artifacts/ci-release"
SHA=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
PROPS=["-p:IncludeSourceRevisionInInformationalVersion=false","-p:KypelonRepositoryUrl=https://github.com/yokk02/Kypelon"]
def run(args,log=None,env=None,secret=None):
 result=subprocess.run(list(map(str,args)),cwd=ROOT,env=env,text=True,encoding="utf-8",errors="replace",stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
 text=result.stdout.replace(secret,"[REDACTED]") if secret else result.stdout
 if log:
  log.parent.mkdir(parents=True,exist_ok=True);log.write_text(text,encoding="utf-8")
 if result.returncode:
  print(text);raise RuntimeError(f"Command failed: {args[0]} (exit {result.returncode})")
 print(f"PASS: {Path(str(args[0])).name}")
 return text
def restore_consumer(project,source,cache,log,env):
 """Use one explicit feed and a fresh cache, excluding SDK-added feeds/fallbacks."""
 project=Path(project).resolve();cache=Path(cache).resolve()
 assert not cache.exists(),"Consumer package cache must be fresh"
 config=project.parent/"NuGet.Config"
 assert not config.exists(),"Consumer NuGet configuration must be fresh"
 configuration=ET.Element("configuration")
 sources=ET.SubElement(configuration,"packageSources");ET.SubElement(sources,"clear")
 ET.SubElement(sources,"add",key="approved-release-feed",value=str(source))
 ET.SubElement(ET.SubElement(configuration,"fallbackPackageFolders"),"clear")
 ET.ElementTree(configuration).write(config,encoding="utf-8",xml_declaration=True)
 # --source alone does not suppress Microsoft.NET.NuGetOfflineCache.targets.
 run(["dotnet","restore",project,"--configfile",config,"--source",source,"--packages",cache,
      "--no-cache","-p:NuGetAudit=false","-p:DisableImplicitLibraryPacksFolder=true",
      "-p:DisableImplicitNuGetFallbackFolder=true","-p:RestoreAdditionalProjectSources=",
      "-p:RestoreAdditionalProjectFallbackFolders=","-p:RestoreFallbackFolders="],log,env)
 assets=json.loads((project.parent/"obj/project.assets.json").read_text(encoding="utf-8"))
 restore=assets["project"]["restore"]
 assert len(restore["sources"])==1,"Consumer restored with an unexpected additional feed"
 actual_source=next(iter(restore["sources"]))
 assert actual_source.rstrip("/")==str(source).rstrip("/") if str(source).startswith("https://") else Path(actual_source).resolve()==Path(source).resolve()
 assert Path(restore["packagesPath"]).resolve()==cache,"Consumer used an unexpected package cache"
 assert {Path(p).resolve() for p in restore["configFilePaths"]}=={config},"Consumer inherited an external NuGet configuration"
 assert not restore.get("fallbackFolders"),"Consumer used a fallback package folder"
 assert {Path(p).resolve() for p in assets["packageFolders"]}=={cache},"Consumer used an additional package folder"
 return assets
def consumer(source,label):
 import pymupdf
 from pypdf import PdfReader
 target=ROOT/"artifacts"/("consumer-"+label)
 assert not target.exists(),"Consumer/cache must be fresh"
 shutil.copytree(ROOT/"scripts/package-consumer",target,ignore=shutil.ignore_patterns("bin","obj"))
 env=os.environ.copy();env["NUGET_PACKAGES"]=str(target/"cache")
 project=target/"Consumer.csproj"
 assets=restore_consumer(project,source,Path(env["NUGET_PACKAGES"]),OUT/"evidence"/f"consumer-{label}-restore.txt",env)
 restore=assets["project"]["restore"]
 (OUT/"evidence"/f"consumer-{label}-restore-state.json").write_text(json.dumps({"sources":restore["sources"],"configFilePaths":restore["configFilePaths"],"packageFolders":assets["packageFolders"]},indent=2),encoding="utf-8")
 shutil.copy2(target/"NuGet.Config",OUT/"evidence"/f"consumer-{label}-NuGet.Config")
 assert len(assets["libraries"])==6 and all(v["type"]=="package" and k.split("/")[0] in IDS and k.endswith("/"+VERSION) for k,v in assets["libraries"].items())
 run(["dotnet","build",project,"-c","Release","--no-restore"],OUT/"evidence"/f"consumer-{label}-build.txt",env)
 for tfm in ("net8.0","net10.0"):
  dest=OUT/"evidence"/"consumer"/label/tfm
  run(["dotnet",target/"bin/Release"/tfm/"Consumer.dll",dest],OUT/"evidence"/f"consumer-{label}-{tfm}.txt",env)
  files=list(dest.glob("*.pdf"));assert len(files)==7
  for file in files:
   reader=PdfReader(file,strict=True);doc=pymupdf.open(file)
   assert not doc.is_repaired and len(reader.pages)==len(doc)
   text="\n".join(p.get_text(sort=False).rstrip("\n") for p in doc)
   if file.stem=="hello-kypelon":assert text=="Hello from Kypelon.Pdf"
   elif file.stem=="unicode":assert text=="ภาษาไทย บริษัท ทดสอบ จำกัด | Unicode FY26"
   elif file.stem=="contextual":assert text=="AV"
   elif file.stem=="table":
    rows=re.findall(r"Employee\s+(\d{4})\b",text);assert len(rows)==len(set(rows))==500
    for i,page in enumerate(doc,1):assert "Employee" in page.get_text() and f"Page {i} of {len(doc)}" in page.get_text()
   else:assert "Snapshot" in text and "Changed" not in text
   for page in doc:page.get_pixmap()
 (OUT/"evidence"/f"consumer-{label}-dependencies.json").write_text(json.dumps(assets["libraries"],indent=2),encoding="utf-8")
def verify(source):
 assert not OUT.exists(),"Use a fresh checkout/output directory"
 OUT.mkdir(parents=True)
 (ROOT/"artifacts/pdf").mkdir(parents=True,exist_ok=True)
 assert Path(os.environ.get("KYPELON_TEST_FONT",str(Path(os.environ["WINDIR"])/"Fonts/tahoma.ttf"))).is_file(),"A Thai-capable test font is required"
 restore=["dotnet","restore","Kypelon.sln"]
 if source:restore+=["--source",source,"-p:NuGetAudit=false"]
 run(restore,OUT/"evidence/restore.txt")
 run(["dotnet","build","Kypelon.sln","-c","Release","--no-restore",*PROPS],OUT/"evidence/build.txt")
 build=(OUT/"evidence/build.txt").read_text(encoding="utf-8")
 assert re.search(r"\b0 Warning\(s\)",build) and re.search(r"\b0 Error\(s\)",build)
 for tfm in ("net8.0","net10.0"):
  run(["dotnet","test","tests/Kypelon.Pdf.Tests/Kypelon.Pdf.Tests.csproj","-c","Release","-f",tfm,"--no-restore",*PROPS,"--logger",f"trx;LogFileName={tfm}.trx","--results-directory",OUT/"evidence/tests"],OUT/"evidence"/f"tests-{tfm}.txt")
  rows=ET.parse(OUT/"evidence/tests"/f"{tfm}.trx").findall(".//{*}UnitTestResult")
  assert len(rows)==385 and sum(x.get("outcome")=="Passed" for x in rows)==384
  skipped=[x for x in rows if x.get("outcome")=="NotExecuted"]
  assert len(skipped)==1 and skipped[0].get("testName").endswith(".ThaiStackedMarksReceiveContextualOffsets")
 run([sys.executable,"scripts/validate-text-pipeline.py"],OUT/"evidence/extraction.txt")
 shutil.copytree(ROOT/"artifacts/release-alpha.2/extraction",OUT/"evidence/extraction")
 # Existing live export suites exercise all five report templates.
 sample=ROOT/"samples/Kypelon.Pdf.Sample.AspNetCore"
 server_log=(OUT/"evidence/web-server.txt").open("w",encoding="utf-8")
 server=subprocess.Popen(["dotnet",str(sample/"bin/Release/net10.0/Kypelon.Pdf.Sample.AspNetCore.dll"),"--contentRoot",str(sample),"--urls","http://127.0.0.1:5078"],cwd=ROOT,stdout=server_log,stderr=subprocess.STDOUT,creationflags=getattr(subprocess,"CREATE_NO_WINDOW",0))
 try:
  for attempt in range(60):
   try:
    with urllib.request.urlopen("http://127.0.0.1:5078/api/status",timeout=2) as response:assert json.load(response)["ready"]
    break
   except (OSError,AssertionError):
    if server.poll() is not None:raise RuntimeError("Export Studio exited before readiness")
    if attempt==59:raise RuntimeError("Export Studio readiness timeout")
    time.sleep(1)
  for name in ("test-export-web.py","test-aefs-web.py","test-edocket-web.py"):
   run([sys.executable,"scripts/"+name,"http://127.0.0.1:5078"],OUT/"evidence"/(name+".txt"))
 finally:
  server.terminate();server.wait(timeout=30);server_log.close()
 names={"business":"business.pdf","table":"table-500.pdf","unicode":"thai.pdf","aefs":"aefs-notification.pdf","edocket":"edocket/edocket-summary.pdf"}
 (OUT/"evidence/pdf").mkdir()
 for name,file in names.items():shutil.copy2(ROOT/"artifacts/web-tests"/file,OUT/"evidence/pdf"/f"kypelon-{name}.pdf")
 run([sys.executable,"scripts/validate-pdfs.py",OUT/"evidence/pdf"],OUT/"evidence/pdf-validation.txt")
 for tfm in ("net8.0","net10.0"):
  bench=ROOT/"benchmarks/Kypelon.Pdf.Benchmarks/bin/Release"/tfm/"Kypelon.Pdf.Benchmarks.dll"
  for mode in ("text-pipeline","prepared"):
   destination=ROOT/"artifacts/ci-bench"/tfm/mode
   run(["dotnet",bench,"--"+mode,destination],OUT/"evidence"/f"benchmark-{tfm}-{mode}.txt")
   data=json.loads((destination/"results.json").read_text(encoding="utf-8"))
   assert len(data)==(3 if mode=="text-pipeline" else 6)
   assert all(0<r["AllocatedBytes"]<600_000_000 for r in data)
   if mode=="prepared":
    for old,new in zip(data[::2],data[1::2]):
     assert old["Scenario"]==new["Scenario"] and old["Pages"]==new["Pages"] and old["OutputBytes"]==new["OutputBytes"]
     assert old["ShapingCalls"]==2*new["ShapingCalls"]
   shutil.copy2(destination/"results.json",OUT/"evidence"/f"benchmark-{tfm}-{mode}.json")
 run(["dotnet","pack","Kypelon.sln","-c","Release","--no-build","--no-restore","-o","artifacts/packages",*PROPS],OUT/"evidence/pack.txt")
 run([sys.executable,"scripts/inspect-packages.py"],OUT/"evidence/package-inspection.txt")
 consumer(str(ROOT/"artifacts/packages"),"local")
 shutil.copytree(ROOT/"artifacts/packages",OUT/"packages")
 packages=json.loads((ROOT/"artifacts/release-alpha.2/packages.json").read_text())
 order=json.loads((ROOT/"artifacts/release-alpha.2/publication-order.json").read_text())
 commit=run(["git","rev-parse","HEAD"]).strip()
 evidence={p.relative_to(OUT).as_posix():SHA(p) for p in (OUT/"evidence").rglob("*") if p.is_file()}
 manifest={"version":VERSION,"sourceCommit":commit,"localGatesPassed":True,"publicReleaseVerified":False,"packages":packages,"publicationOrder":order,"evidenceHashes":evidence}
 (OUT/"manifest.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
 digest=SHA(OUT/"manifest.json")
 print("REVIEWED_MANIFEST_SHA256="+digest)
 if os.environ.get("GITHUB_STEP_SUMMARY"):
  with open(os.environ["GITHUB_STEP_SUMMARY"],"a",encoding="utf-8") as f:f.write(f"## Candidate verified\n\nVersion: {VERSION}\n\nCommit: {commit}\n\nRun ID: {os.environ['GITHUB_RUN_ID']}\n\nManifest SHA-256: {digest}\n\nReview the kypelon-nuget-candidate artifact before using publish mode. Nothing was published.\n")
def review():
 expected=os.environ.get("REVIEWED_MANIFEST_SHA256","")
 assert re.fullmatch("[0-9a-fA-F]{64}",expected),"Supply the reviewed candidate manifest SHA-256"
 assert SHA(OUT/"manifest.json")==expected.lower(),"Reviewed artifact mismatch"
 data=json.loads((OUT/"manifest.json").read_text())
 assert data["version"]==VERSION and data["localGatesPassed"]
 assert data["sourceCommit"]==os.environ["GITHUB_SHA"],"Candidate must be from this exact commit"
 assert len(data["packages"])==6 and {p["id"] for p in data["packages"]}==IDS
 ready=set()
 for name in data["publicationOrder"]:
  p=next(p for p in data["packages"] if p["id"]==name)
  assert p["version"]==VERSION and set(p["dependencies"])<=ready
  for field,hashfield,suffix in (("package","sha256",".nupkg"),("symbols","symbolSha256",".snupkg")):
   assert p[field]==name+"."+VERSION+suffix
   assert SHA(OUT/"packages"/p[field])==p[hashfield]
  ready.add(name)
 assert ready==IDS and len(data["publicationOrder"])==6
 for name,digest in data["evidenceHashes"].items():
  target=(OUT/name).resolve();assert target.is_relative_to(OUT.resolve()) and SHA(target)==digest
 print("PASS: reviewed artifact, evidence, package hashes and dependency graph")
 return data
def push():
 data=review();key=os.environ.get("NUGET_API_KEY");assert key,"Temporary OIDC credential missing"
 for name in data["publicationOrder"]:
  package=next(p for p in data["packages"] if p["id"]==name)
  text=run(["dotnet","nuget","push",OUT/"packages"/package["package"],"--api-key",key,"--source","https://api.nuget.org/v3/index.json","--skip-duplicate"],OUT/"push-results"/(name+".txt"),secret=key)
  assert not re.search("already exists|conflict|duplicate",text,re.I),"Duplicate response: stop and inspect public package before dependent pushes"
 print("Push commands completed; public indexing/consumer verification still required.")
def public():
 for name in sorted(IDS):
  url=f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/{VERSION}/{name.lower()}.{VERSION}.nupkg"
  target=OUT/"public-packages"/(name+"."+VERSION+".nupkg");target.parent.mkdir(exist_ok=True)
  for attempt in range(40):
   try:
    with urllib.request.urlopen(url,timeout=30) as response:target.write_bytes(response.read())
    break
   except urllib.error.HTTPError as e:
    if e.code!=404 or attempt==39:raise
    time.sleep(15)
  with ZipFile(target) as z:
   with ZipFile(OUT/"packages"/target.name) as reviewed:
    for entry in reviewed.namelist():
     if entry.startswith("lib/") or entry.endswith(".nuspec") or entry=="README.md":
      assert z.read(entry)==reviewed.read(entry),"Public package differs from the reviewed artifact"
   meta=ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith(".nuspec")))).find("{*}metadata")
   assert meta.find("{*}id").text==name and meta.find("{*}version").text==VERSION
   assert meta.find("{*}authors").text=="Konkaew" and meta.find("{*}license").text=="MIT"
   assert meta.find("{*}readme").text=="README.md" and b"# Kypelon" in z.read("README.md")
   for dep in meta.findall(".//{*}dependency"):assert dep.get("id") in IDS and dep.get("version").strip("[]")==VERSION
 consumer("https://api.nuget.org/v3/index.json","public")
 (OUT/"public-verification.json").write_text(json.dumps({"version":VERSION,"nugetOrgOnlyConsumerPassed":["net8.0","net10.0"],"packageIds":sorted(IDS)},indent=2))
 print("PASS: all public package metadata and fresh nuget.org-only consumers")
if __name__=="__main__":
 parser=argparse.ArgumentParser()
 parser.add_argument("mode",choices=["verify","review","push","public"])
 parser.add_argument("--restore-source")
 args=parser.parse_args()
 if args.mode=="verify":verify(args.restore_source)
 elif args.mode=="review":review()
 elif args.mode=="push":push()
 else:public()
