"""Independent local release PDF checks; PDF generation remains entirely in Kypelon."""
from pathlib import Path
import json,re,shutil
import pypdf,pymupdf
root=Path(__file__).resolve().parents[1]
out=root/'artifacts/release-alpha.2'
results=[]
def inspect(file):
 reader=pypdf.PdfReader(file,strict=True);doc=pymupdf.open(file)
 assert not doc.is_repaired and len(reader.pages)==len(doc)
 assert reader.metadata.producer=='Kypelon.Pdf 0.2.0-alpha.2',file
 for page in doc:
  page.get_pixmap(matrix=pymupdf.Matrix(.35,.35),alpha=False)
 return reader,doc
for name,pages in [('business',3),('table',17),('unicode',1),('aefs',1),('edocket',4)]:
 file=root/f'artifacts/pdf/kypelon-{name}.pdf'
 reader,doc=inspect(file)
 assert len(doc)==pages
 old=pypdf.PdfReader(root/f'artifacts/history/kypelon-alpha.1/pdf/kypelon-{name}.pdf',strict=True)
 assert len(old.pages)==pages
 # Stronger than text snapshots: every final page's graphics/text stream remains unchanged.
 for i,page in enumerate(reader.pages):
  assert page['/Contents'].get_object().get_data()==old.pages[i]['/Contents'].get_object().get_data(),(name,i,'page commands changed')
 for page in doc:
  for block in page.get_text('dict')['blocks']:
   for line in block.get('lines',[]):
    for span in line.get('spans',[]):
     x0,y0,x1,y1=span['bbox']
     assert x0>=-.1 and y0>=-.1 and x1<=page.rect.width+.1 and y1<=page.rect.height+.1,(file,span)
 text='\n'.join(p.extract_text() or '' for p in reader.pages)
 assert not re.search(r'nova\s?pdf|\bNOVA\b',text,re.I)
 if name=='table':
  rows=re.findall(r'Employee\s+(\d{4})\b',text)
  assert len(rows)==len(set(rows))==500
  for i,page in enumerate(reader.pages,1):
   t=page.extract_text()
   assert 'Employee' in t and f'Page {i} of {pages}' in t
 if name=='unicode': assert 'ภาษาไทย' in text and 'ข้อมูลพนักงาน' in text
 if name=='aefs':
  assert any(a.get_object().get('/A',{}).get('/S')=='/URI' for a in reader.pages[0].get('/Annots',[]))
 results.append(dict(file=file.relative_to(root).as_posix(),pages=pages,bytes=file.stat().st_size,
                     pageCommandsUnchanged=True,strictRead=True,rendered=True,textBoundsPassed=True))
consumers=[]
for tfm in ['net8.0','net10.0']:
 folder=out/'consumer-output'/tfm
 files=sorted(folder.glob('*.pdf')); assert len(files)==7
 for file in files:
  reader,doc=inspect(file)
  text='\n'.join(p.get_text(sort=False).rstrip('\n') for p in doc)
  if file.stem=='hello-kypelon': assert text=='Hello from Kypelon.Pdf'
  elif file.stem=='table':
   rows=re.findall(r'Employee\s+(\d{4})\b',text)
   assert len(rows)==len(set(rows))==500
   for i,page in enumerate(doc,1): assert f'Page {i} of {len(doc)}' in page.get_text() and 'Employee' in page.get_text()
  elif file.stem=='unicode': assert text=='ภาษาไทย บริษัท ทดสอบ จำกัด | Unicode FY26'
  elif file.stem=='contextual': assert text=='AV'
  else: assert 'Snapshot' in text and 'Changed' not in text
  consumers.append(dict(file=file.relative_to(root).as_posix(),pages=len(doc),extractionPassed=True))
fixture=json.loads((out/'extraction/results.json').read_text(encoding='utf-8'))
assert len(fixture['cases'])==18 and all(c['actualTextReaderPassed'] for c in fixture['cases'])
report=dict(validators={'pypdf':pypdf.__version__,'PyMuPDF':pymupdf.VersionBind},
            unavailable={n:shutil.which(n) for n in ['qpdf','pdfinfo','pdftotext','mutool','gs','gswin64c']},
            pdfs=results,consumerPdfs=consumers,extractionFixtures=18)
(out/'pdf-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
