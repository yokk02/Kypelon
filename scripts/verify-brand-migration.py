"""Brand migration evidence: PDF behavior and source-only change audit."""
from pathlib import Path
import difflib, json, re, shutil
import pypdf, pymupdf
root=Path(__file__).resolve().parents[1]
out=root/'artifacts/brand-migration'
before=json.loads((out/'source-before.json').read_text(encoding='utf-8'))
def migrate(s):
 s=s.replace('NovaPdf.sln','Kypelon.sln').replace('NovaPdfRepositoryUrl','KypelonRepositoryUrl')
 s=s.replace('NovaPdf','Kypelon.Pdf').replace('NOVAPDF','KYPELON').replace('novapdf','kypelon').replace('NovaPDF','Kypelon').replace('Nova Pdf','Kypelon')
 s=s.replace('Kypelon.Pdf Export Studio','Kypelon Export Studio').replace('X-Kypelon.Pdf-','X-Kypelon-')
 s=s.replace('"Kypelon.Pdf • ','"Kypelon • ').replace('"Kypelon.Pdf / Assurance reporting','"Kypelon / Assurance reporting')
 s=s.replace('"Kypelon.Pdf baseline"','"Kypelon baseline"').replace('Hello, Kypelon.Pdf!','Hello, Kypelon!')
 s=s.replace('"Kypelon.Pdf sample"','"Kypelon sample"').replace('engine = "Kypelon.Pdf"','engine = "Kypelon"')
 return s.replace('NOVA /','KYPELON /').replace('/Kypelon.PdfUnicode','/KypelonUnicode').replace('\r','')
audited=[]
for name,text in before.items():
 if name.endswith('.cs') and name.split('/')[0] in ['src','tests','samples','benchmarks']:
  target=name.replace('NovaPdf','Kypelon.Pdf')
  actual=(root/target).read_text(encoding='utf-8')
  assert migrate(text)==actual, target+' has changes beyond the listed brand substitutions'
  audited.append(target)

pdfs=[]
for name,pages in [('business',3),('table',17),('unicode',1),('aefs',1),('edocket',4)]:
 file=root/f'artifacts/pdf/kypelon-{name}.pdf'
 old=root/f'artifacts/history/novapdf-prebrand/pdf/novapdf-{name}.pdf'
 reader=pypdf.PdfReader(file,strict=True); doc=pymupdf.open(file)
 assert not doc.is_repaired and len(doc)==pages==len(reader.pages)
 assert reader.metadata.producer=='Kypelon.Pdf 0.2.0-alpha.1'
 assert reader.metadata.creator.startswith('Kypelon Export Studio')
 current='\n'.join(p.extract_text() or '' for p in reader.pages)
 previous='\n'.join(p.extract_text() or '' for p in pypdf.PdfReader(old,strict=True).pages)
 expected=previous.replace('NovaPdf','Kypelon').replace('NOVA /','KYPELON /')
 same_text=current==expected
 diff='\n'.join(difflib.unified_diff(expected.splitlines(),current.splitlines(),fromfile='before (brand normalized)',tofile='after'))
 (out/(name+'-text-diff.txt')).write_text(diff,encoding='utf-8')
 assert same_text, (name,diff[:2000])
 assert not re.search(r'nova\s?pdf|\bnova\b', current, re.I)
 for page in doc:
  page.get_pixmap(matrix=pymupdf.Matrix(.4,.4),alpha=False)
  for block in page.get_text('dict')['blocks']:
   for line in block.get('lines',[]):
    for span in line.get('spans',[]):
     x0,y0,x1,y1=span['bbox']
     assert x0>=-.1 and y0>=-.1 and x1<=page.rect.width+.1 and y1<=page.rect.height+.1,(name,span)
 pdfs.append(dict(file=str(file.relative_to(root)),pages=pages,producer=reader.metadata.producer,
                  creator=reader.metadata.creator,textMatchesBeforeAfterBrandNormalization=True,
                  allPagesRendered=True,textWithinPageBounds=True))

consumers=[]
for tfm in ['net8.0','net10.0']:
 for p in (out/'consumer-output'/tfm).glob('*.pdf'):
  reader=pypdf.PdfReader(p,strict=True);doc=pymupdf.open(p)
  assert not doc.is_repaired and reader.metadata.producer=='Kypelon.Pdf 0.2.0-alpha.1'
  text=''.join(page.get_text() for page in doc)
  assert ('AV' if p.stem=='consumer-shaped' else 'PackageReference: v0.2') in text
  for page in doc: page.get_pixmap()
  consumers.append(str(p.relative_to(root)))
deps=json.loads((out/'consumer-dependencies.json').read_text(encoding='utf-8'))
assert len(deps)==6 and all(k.startswith('Kypelon.Pdf') and v['type']=='package' for k,v in deps.items())
assert 'ProjectReference' not in (out/'consumer/Consumer.csproj').read_text()
result=dict(version='0.2.0-alpha.1',sourceFilesWithOnlyBrandSubstitutions=audited,
            pdfs=pdfs,consumerPdfs=consumers,validators={'pypdf':pypdf.__version__,'PyMuPDF':pymupdf.VersionBind},
            otherValidators={n:shutil.which(n) for n in ['qpdf','pdfinfo','pdftotext','mutool','gs','gswin64c']})
(out/'behavior-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False,indent=2))
