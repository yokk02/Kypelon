"""Inspect all current Kypelon package identities and deployment contents."""
from pathlib import Path
from zipfile import ZipFile
import xml.etree.ElementTree as ET
import hashlib, json, sys
expected = sys.argv[1] if len(sys.argv) > 1 else "0.2.0-alpha.2"
ids = {"Kypelon.Pdf"+suffix for suffix in ["", ".Core", ".Graphics", ".Text", ".Layout", ".AspNetCore"]}
expected_deps = {
 "Kypelon.Pdf": {"Kypelon.Pdf.Core","Kypelon.Pdf.Graphics","Kypelon.Pdf.Text","Kypelon.Pdf.Layout"},
 "Kypelon.Pdf.Core": set(),
 "Kypelon.Pdf.Graphics": {"Kypelon.Pdf.Core"},
 "Kypelon.Pdf.Text": {"Kypelon.Pdf.Core"},
 "Kypelon.Pdf.Layout": {"Kypelon.Pdf.Graphics","Kypelon.Pdf.Text"},
 "Kypelon.Pdf.AspNetCore": {"Kypelon.Pdf"}
}
packages=[]
assert {p.name for p in Path('artifacts/packages').glob('*.nupkg')} == {f'{name}.{expected}.nupkg' for name in ids}, 'Stale or unexpected package in release feed'
for p in sorted(Path('artifacts/packages').glob(f'*.{expected}.nupkg')):
 with ZipFile(p) as z:
  assert z.testzip() is None
  names=z.namelist()
  tree=ET.fromstring(z.read(next(n for n in names if n.endswith('.nuspec'))))
  ns={'n':tree.tag.split('}')[0].lstrip('{')}
  meta=tree.find('n:metadata',ns); assert meta is not None
  get=lambda field:meta.find('n:'+field,ns).text
  name=get('id')
  assert name in ids and get('version')==expected
  assert get('title')==name and get('authors')=='Konkaew'
  assert get('description')=='Native C# PDF and document layout engine for .NET.'
  assert get('license')=='MIT' and meta.find('n:license',ns).attrib['type']=='expression'
  notes=get('releaseNotes')
  assert all(word in notes for word in ['First public Kypelon.Pdf alpha', 'PreparedPdfDocument', 'deterministic preparation', 'GSUB/GPOS', 'bidi', 'Thai', 'Reader/Edit', 'PDF/A', 'PDF/UA'])
  assert get('readme')=='README.md' and 'README.md' in names
  # Git checkouts on Windows may use CRLF; validate text without changing package bytes.
  readme=z.read('README.md').decode('utf-8').replace('\r\n', '\n')
  assert readme.startswith('# Kypelon\n') and '**by Konkaew**' in readme, f'Invalid README branding in {p.name}'
  assert not any(marker in readme.casefold() for marker in ('publication is pending', 'target the pending public release')), f'Prepublication instructions remain in README in {p.name}'
  assert {'pdf','document','layout','csharp','dotnet','report','unicode','thai','aspnetcore'} <= set(get('tags').split())
  for framework in ['net8.0','net10.0']:
   assert f'lib/{framework}/{name}.dll' in names
   assert f'lib/{framework}/{name}.xml' in names
   assert ET.fromstring(z.read(f'lib/{framework}/{name}.xml')).find('assembly/name').text==name
  assert {n.split('/')[1] for n in names if n.startswith('lib/')}=={'net8.0','net10.0'}
  assert all(Path(n).name.startswith('Kypelon.Pdf') for n in names if n.endswith('.dll'))
  deps=meta.findall('.//n:dependency',ns)
  dep_ids={d.attrib['id'] for d in deps}
  assert dep_ids==expected_deps[name]
  assert all(d.attrib['version'].strip('[]')==expected for d in deps)
  symbols=p.with_suffix('.snupkg')
  assert symbols.is_file()
  with ZipFile(symbols) as sz:
   assert sz.testzip() is None
   allowed={'.pdb','.nuspec','.xml','.psmdcp','.rels','.p7s'}
   assert all(entry.endswith(tuple(allowed)) for entry in sz.namelist()), 'Unsupported symbol-package payload'
   for entry in sz.namelist():
    if entry.endswith('.pdb'): assert sz.read(entry).startswith(b'BSJB'), 'Portable PDB required'
   assert all(f'lib/{tfm}/{name}.pdb' in sz.namelist() for tfm in ['net8.0','net10.0'])
  packages.append(dict(package=p.name,id=name,version=get('version'),authors=get('authors'),
                       description=get('description'),license=get('license'),releaseNotes=notes,bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest(),
                       frameworks=['net8.0','net10.0'],dependencies=sorted(dep_ids),symbols=symbols.name,symbolSha256=hashlib.sha256(symbols.read_bytes()).hexdigest()))
assert {p['id'] for p in packages}==ids and len(packages)==6
Path('artifacts/validation').mkdir(parents=True,exist_ok=True)
Path('artifacts/validation/packages.json').write_text(json.dumps(packages,indent=2))
print(json.dumps(packages,indent=2))

remaining={p["id"]:set(p["dependencies"]) for p in packages}; order=[]
while remaining:
 ready=sorted(name for name,deps in remaining.items() if deps <= set(order))
 assert ready, "Cyclic or missing package dependency"
 for name in ready: order.append(name); del remaining[name]
Path("artifacts/release-alpha.2").mkdir(parents=True,exist_ok=True)
Path("artifacts/release-alpha.2/publication-order.json").write_text(json.dumps(order,indent=2))
Path("artifacts/release-alpha.2/packages.json").write_text(json.dumps(packages,indent=2))
print("Publication order: " + " -> ".join(order))
