"""Real SDK restores with injected offline feeds; no network or production package needed."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from zipfile import ZipFile

spec=importlib.util.spec_from_file_location("publisher",Path(__file__).with_name("trusted-publishing.py"))
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class ConsumerIsolationTests(unittest.TestCase):
    def setUp(self):
        base=m.ROOT/"artifacts";base.mkdir(exist_ok=True)
        self.temp=tempfile.TemporaryDirectory(prefix="consumer-isolation-tests-",dir=base)
        self.root=Path(self.temp.name).resolve()
        self.assertTrue(self.root.is_relative_to(base.resolve()))
        self.feed=self.root/"approved feed & packages"
        self.injected=self.root/"sdk-library-packs"
        self.additional=self.root/"environment-feed"
        self.fallback=self.root/"fallback-cache"
        self.warm=self.root/"preexisting-cache"
        for folder in (self.feed,self.injected,self.additional,self.fallback,self.warm):folder.mkdir()
        (self.root/"NuGet.Config").write_text('<configuration><packageSources><clear/><add key="inherited" value="environment-feed"/></packageSources></configuration>',encoding="utf-8")
        (self.root/"Directory.Build.props").write_text('<Project />')
        (self.root/"Directory.Packages.props").write_text('<Project />')
        target=self.root/"consumer";target.mkdir()
        self.project=target/"Consumer.csproj"
        self.cache=target/"fresh-cache"
        self.log=self.root/"restore.log"
        self.write_project()
        self.env={**os.environ,"PYTHONUTF8":"1","NUGET_PACKAGES":str(self.warm),
                  "_WorkloadLibraryPacksFolder":str(self.injected),
                  "RestoreAdditionalProjectSources":str(self.additional),
                  "RestoreAdditionalProjectFallbackFolders":str(self.fallback),
                  "RestoreFallbackFolders":str(self.fallback)}

    def tearDown(self):
        self.assertTrue(self.root.is_relative_to((m.ROOT/"artifacts").resolve()))
        self.temp.cleanup()

    def write_project(self, package=False):
        reference='<ItemGroup><PackageReference Include="Isolation.Probe" Version="1.0.0" /></ItemGroup>' if package else ''
        self.project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>'+reference+'</Project>',encoding="utf-8")

    def make_package(self, feed):
        with ZipFile(feed/"Isolation.Probe.1.0.0.nupkg","w") as archive:
            archive.writestr("Isolation.Probe.nuspec",'<package><metadata><id>Isolation.Probe</id><version>1.0.0</version><authors>Tests</authors><description>Source-isolation regression fixture</description></metadata></package>')

    def restore(self, source):
        return m.restore_consumer(self.project,str(source),self.cache,self.log,self.env)

    def test_local_feed_excludes_sdk_environment_and_inherited_sources(self):
        assets=self.restore(self.feed)
        restore=assets["project"]["restore"]
        self.assertEqual({Path(p).resolve() for p in restore["sources"]},{self.feed.resolve()})
        self.assertEqual({Path(p).resolve() for p in assets["packageFolders"]},{self.cache.resolve()})
        self.assertEqual({Path(p).resolve() for p in restore["configFilePaths"]},{self.project.parent/"NuGet.Config"})

    def test_public_feed_configuration_has_no_local_fallback(self):
        # No PackageReference means this checks public-only configuration without downloading packages.
        source="https://api.nuget.org/v3/index.json"
        assets=self.restore(source)
        self.assertEqual(set(assets["project"]["restore"]["sources"]),{source})
        self.assertEqual({Path(p).resolve() for p in assets["packageFolders"]},{self.cache.resolve()})

    def test_package_is_downloaded_from_approved_feed(self):
        self.make_package(self.feed);self.write_project(package=True)
        assets=self.restore(self.feed)
        self.assertEqual(set(assets["libraries"]),{"Isolation.Probe/1.0.0"})
        metadata=json.loads((self.cache/"isolation.probe/1.0.0/.nupkg.metadata").read_text())
        self.assertEqual(Path(metadata["source"]).resolve(),self.feed.resolve())

    def test_missing_package_cannot_leak_from_sdk_feed_or_warm_cache(self):
        self.make_package(self.injected);self.write_project(package=True)
        # The old command restores from the SDK-injected source and populates a warm cache.
        baseline=subprocess.run(["dotnet","restore",str(self.project),"--source",str(self.feed),"--no-cache","-p:NuGetAudit=false"],
                                env=self.env,cwd=m.ROOT,capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(baseline.returncode,0,baseline.stdout+baseline.stderr)
        before=json.loads((self.project.parent/"obj/project.assets.json").read_text())
        self.assertGreater(len(before["project"]["restore"]["sources"]),1)
        self.assertTrue((self.warm/"isolation.probe/1.0.0/.nupkg.metadata").is_file())
        with self.assertRaises(RuntimeError), contextlib.redirect_stdout(io.StringIO()):self.restore(self.feed)
        self.assertIn("NU1101",self.log.read_text(encoding="utf-8"))
        self.assertFalse((self.cache/"isolation.probe").exists())

    def test_existing_cache_is_rejected(self):
        self.cache.mkdir()
        with self.assertRaisesRegex(AssertionError,"cache must be fresh"):self.restore(self.feed)

if __name__=="__main__":unittest.main()
