"""Repository-wide pre-public brand-migration stale-name gate.
Every retained old-brand match is reported with a migration/history reason.
"""
from pathlib import Path
import collections, json, re, zipfile
root=Path(__file__).resolve().parents[1]
out=root/'artifacts/brand-migration'
pattern=re.compile(rb'nova[ ]?pdf|\bnova\b',re.I)
unicode_pattern=re.compile(rb'n\x00o\x00v\x00a\x00(?: \x00)?p\x00d\x00f\x00',re.I)
historical_docs={
 'docs/performance-baseline.md','docs/release-report.md',
 'docs/development/hardening-alpha.2.md','docs/development/text-pipeline-v0.2-verification.md',
 'docs/development/verification.md','docs/reviews/library-review-2026-09-23.md',
 'docs/reviews/chatgpt-review-prompt.md'
}
history_roots=['artifacts/'+p+'/' for p in [
 'history','benchmark','consumer-cache','final-consumer-cache','hardening',
 'package-consumer','reference','review','tests','text-v0.2'
]]
migration_files={
 'README.md','CHANGELOG.md','LICENSE','artifacts/HISTORY.md',
 'docs/development/kypelon-brand-migration.md','docs/architecture/decisions.md',
 'docs/architecture/text-pipeline-v0.2.md',
 'docs/development/aefs-template.md','docs/development/edocket-template.md',
 'scripts/scan-brand-migration.py','scripts/verify-brand-migration.py',
 'artifacts/brand-migration/source-before.json','artifacts/brand-migration/before-name-scan.json',
 'artifacts/brand-migration/migrate-brand.py','artifacts/brand-migration/files-changed.json'
}
excluded_outputs={'artifacts/brand-migration/stale-name-scan.json',
                  'artifacts/brand-migration/stale-name-scan.md'}
def reason(name):
 base=name.split('!')[0]
 if base in historical_docs: return 'Historical report: original pre-rename identities/results retained with notice'
 if any(base.startswith(p) for p in history_roots): return 'Archived pre-public evidence, binaries or original review bundle'
 if base in migration_files: return 'Explicit migration/history explanation, retained MIT attribution, or migration audit'
 if ('/consumer-cache/' in base and base.endswith('/README.md')) or ('!' in name and name.endswith('!README.md')):
  return 'Packaged README migration-history paragraph'
 return None
records=[]; violations=[]; files=0; members=0
def inspect(name,data):
 matches=[dict(offset=m.start(),encoding='ASCII',line=data[:m.start()].count(b'\n')+1) for m in pattern.finditer(data)]
 matches += [dict(offset=m.start(),encoding='UTF-16LE') for m in unicode_pattern.finditer(data)]
 path_matches=len(re.findall(r'nova[ ]?pdf|\bnova\b',name,re.I))
 if not matches and not path_matches:return
 why=reason(name)
 if why is None and name.startswith('artifacts/nuget/microsoft.codeanalysis.') and '/pt-BR/Microsoft.CodeAnalysis.' in name and not re.search(rb'nova[ ]?pdf', data, re.I):
  why='Non-brand false positive: Portuguese word nova (new) in external compiler/analyzer resources; dependency left unchanged'
 item=dict(file=name,reason=why,pathMatches=path_matches,occurrences=matches)
 records.append(item)
 if why is None: violations.append(item)
for p in root.rglob('*'):
 if not p.is_file():continue
 rel=p.relative_to(root).as_posix()
 if rel in excluded_outputs:continue
 files+=1
 data=p.read_bytes()
 inspect(rel,data)
 if p.suffix in {'.zip','.nupkg','.snupkg'}:
  with zipfile.ZipFile(p) as z:
   for member in z.infolist():
    if member.is_dir():continue
    members+=1
    # Inspect decompressed members; original nested archive bytes are retained as evidence.
    inspect(rel+'!'+member.filename,z.read(member))
summary=dict(filesScanned=files,archiveMembersScanned=members,matchingFilesOrMembers=len(records),
             retainedOccurrences=sum(len(r['occurrences'])+r['pathMatches'] for r in records),
             unclassifiedCount=len(violations),
             exclusions={'selfGeneratedReports':sorted(excluded_outputs),
                         'nestedArchiveRecursion':'Archive members are decompressed once; nested archive bytes are scanned as stored.'})
report=dict(summary=summary,violations=violations,records=records)
(out/'stale-name-scan.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
md=['# Retained pre-public name inventory','','The current product is Kypelon, by Konkaew. Every detected NovaPdf / NOVAPDF / Nova Pdf / NovaPDF occurrence is classified below. Detailed byte offsets (and ASCII line numbers) are in stale-name-scan.json. Generated copies and archive members are included. Only these reports themselves are excluded to prevent self-reference.','','## Result','',json.dumps(summary,indent=2),'','## Matching files and archive members','','| File/member | Matches (content + path) | Reason |','| --- | ---: | --- |']
for r in records:
 md.append('| '+r['file'].replace('|','%7C')+' | '+str(len(r['occurrences'])+r['pathMatches'])+' | '+(r['reason'] or '**UNCLASSIFIED**')+' |')
(out/'stale-name-scan.md').write_text('\n'.join(md)+'\n',encoding='utf-8')
print(json.dumps(summary,indent=2))
if violations:
 print(json.dumps([{'file':r['file'],'matches':len(r['occurrences'])} for r in violations],indent=2))
 raise SystemExit(1)
