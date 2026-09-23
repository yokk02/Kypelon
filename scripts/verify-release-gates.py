"""Check recorded local release gates and lock the reviewed artifacts for publishing.
This verifies local evidence only; it never marks a public release successful.
"""
from pathlib import Path
import hashlib, json, re
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
out=root/'artifacts/release-alpha.2'
load=lambda name:json.loads((out/name).read_text(encoding='utf-8'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
version='0.2.0-alpha.2'
tests={}
for tfm in ['net8.0','net10.0']:
 tree=ET.parse(out/f'tests/release-{tfm}.trx')
 results=tree.findall('.//{*}UnitTestResult')
 passed=[r for r in results if r.get('outcome')=='Passed']
 skipped=[r for r in results if r.get('outcome')=='NotExecuted']
 assert len(results)==385 and len(passed)==384 and len(skipped)==1, tfm
 assert skipped[0].get('testName').endswith('.ThaiStackedMarksReceiveContextualOffsets')
 tests[tfm]=dict(passed=384,failed=0,skipped=1)
for log in ['build.txt','consumer-build.txt']:
 text=(out/log).read_text(encoding='utf-8')
 assert 'Build succeeded.' in text and '0 Warning(s)' in text and '0 Error(s)' in text,log
packages=load('packages.json')
assert len(packages)==6
for p in packages:
 assert p['version']==version
 assert sha(root/'artifacts/packages'/p['package'])==p['sha256'],p['id']
 assert sha(root/'artifacts/packages'/p['symbols'])==p['symbolSha256'],p['id']
for file,count in [('web-runs.json',3),('consumer-runs.json',2)]:
 runs=load(file)
 assert len(runs)==count and all(r['status']=='fulfilled' and r['value']['exit']==0 for r in runs),file
consumer=ET.parse(out/'consumer/Consumer.csproj').getroot()
assert not consumer.findall('.//ProjectReference')
refs={r.get('Include'):r.get('Version') for r in consumer.findall('.//PackageReference')}
assert refs=={'Kypelon.Pdf':version,'Kypelon.Pdf.AspNetCore':version}
assets=json.loads((out/'consumer/obj/project.assets.json').read_text(encoding='utf-8'))
assert len(assets['libraries'])==6
assert all(v['type']=='package' and k.endswith('/'+version) for k,v in assets['libraries'].items())
assert len(assets['project']['restore']['sources'])==1
source=next(iter(assets['project']['restore']['sources']))
assert Path(source).resolve()==(root/'artifacts/packages').resolve()
assert Path(assets['project']['restore']['packagesPath']).resolve()==(out/'local-consumer-cache').resolve()
pdf=load('pdf-validation.json')
assert len(pdf['pdfs'])==5 and [p['pages'] for p in pdf['pdfs']]==[3,17,1,1,4]
assert all(all(p[k] for k in ['strictRead','rendered','textBoundsPassed','pageCommandsUnchanged']) for p in pdf['pdfs'])
assert len(pdf['consumerPdfs'])==14 and all(p['extractionPassed'] for p in pdf['consumerPdfs'])
extraction=load('extraction/results.json')
assert len(extraction['cases'])==18 and all(p['actualTextReaderPassed'] for p in extraction['cases'])
performance={}
for tfm in tests:
 before=load(f'before/{tfm}/results.json'); after=load(f'after/{tfm}/results.json')
 assert len(before)==len(after)==3
 for b,a in zip(before,after):
  assert b['Scenario']==a['Scenario'] and b['Pages']==a['Pages'] and b['OutputBytes']==a['OutputBytes']
  assert a['AllocatedBytes'] < b['AllocatedBytes']*1.1
  assert a['ShapingCalls']==b['ShapingCalls']
 modes=load(f'prepared/{tfm}/results.json')
 assert len(modes)==6
 for old,new in zip(modes[::2],modes[1::2]):
  assert old['Scenario']==new['Scenario']
  assert old['Pages']==new['Pages'] and old['OutputBytes']==new['OutputBytes']
  assert old['ShapingCalls']==2*new['ShapingCalls']
 performance[tfm]=dict(directWriteBefore=before,directWriteAfter=after,preparedComparison=modes)
runtime_paths=[]
for folder in ['src','tests','samples','benchmarks']:
 runtime_paths += [p for p in (root/folder).rglob('*') if p.is_file() and
                   not {'bin','obj','node_modules'}.intersection(p.relative_to(root).parts) and
                   p.suffix in {'.cs','.csproj','.props','.targets','.html','.js','.css'}]
runtime_paths += [out/'consumer/Consumer.csproj',out/'consumer/Program.cs']
stale=[]
for p in runtime_paths:
 for number,line in enumerate(p.read_text(encoding='utf-8-sig').splitlines(),1):
  if re.search(r'Nova\s?Pdf',line,re.I): stale.append(dict(file=p.relative_to(root).as_posix(),line=number,text=line))
assert not stale, stale
lock_paths=runtime_paths+[root/'Directory.Build.props',root/'Directory.Packages.props',root/'Kypelon.sln',root/'README.md',root/'LICENSE',root/'CHANGELOG.md',root/'docs/architecture/preparation-alpha.2.md',root/'docs/development/release-alpha.2.md']
lock_paths += list((root/'scripts').glob('*.*'))
evidence=['build.txt','consumer-build.txt','packages.json','publication-order.json','web-runs.json',
          'consumer-runs.json','pdf-validation.json','extraction/results.json',
          'tests/release-net8.0.trx','tests/release-net10.0.trx']
lock_paths += [out/p for p in evidence]
lock_paths += [root/p['file'] for p in pdf['pdfs']+pdf['consumerPdfs']]
report=dict(version=version,localGatesPassed=True,publicReleaseVerified=False,tests=tests,
            build=dict(warnings=0,errors=0),packages=packages,publicationOrder=load('publication-order.json'),
            pdfs=pdf['pdfs'],localConsumer='Both frameworks passed all seven scenarios, local feed and fresh cache only',
            performance=performance,currentRuntimeStaleNames=stale,
            fileHashes={p.relative_to(root).as_posix():sha(p) for p in sorted(set(lock_paths))})
(out/'local-gates.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('LOCAL GATES PASS: 384 passed / 0 failed / 1 skipped per framework; 6 packages + symbols; all PDF/consumer gates.')
print('Public release is NOT verified by this local check.')
