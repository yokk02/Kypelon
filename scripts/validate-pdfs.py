"""Independent read/extraction/render checks. These tools never generate Kypelon.Pdf PDFs."""
from pathlib import Path
import json, logging, re, sys
from pypdf import PdfReader
import fitz
base = Path(sys.argv[1] if len(sys.argv)>1 else 'artifacts/pdf')
out = Path('artifacts/validation'); out.mkdir(parents=True, exist_ok=True)
results=[]
for file in sorted(base.glob('*.pdf')):
    reader = PdfReader(file, strict=True)
    doc = fitz.open(file)
    assert not doc.is_repaired, f'{file}: MuPDF repaired the file'
    assert len(reader.pages) == len(doc)
    text='\n'.join(p.extract_text() or '' for p in reader.pages)
    (out/(file.stem+'.txt')).write_text(text, encoding='utf-8')
    for page in doc:
        pix=page.get_pixmap(matrix=fitz.Matrix(.4,.4), alpha=False)
        assert pix.width>0 and pix.height>0
    if file.stem in ['business-report','thai-unicode','images','debug-layout']:
        doc[0].get_pixmap(matrix=fitz.Matrix(1,1), alpha=False).save(out/(file.stem+'-page1.png'))
    if file.stem=='long-table-500-rows':
        for i in range(1,501): assert len(re.findall(r'Employee\s+'+f'{i:04d}'+r'\b',text))==1, f'Missing/duplicate row {i}'
        for i,p in enumerate(reader.pages,1):
            t=p.extract_text(); assert 'Employee' in t and 'Status' in t
            assert f'Page {i} of {len(reader.pages)}' in t
    if file.stem=='thai-unicode':
        for sample in ['ภาษาไทย','บริษัท ทดสอบ จำกัด','รายงานผลการตรวจสอบ','ข้อมูลพนักงาน','ปีงบประมาณ 2569','รายงาน Audit FY26 – บริษัท ABC จำกัด','๐๑๒๓๔๕๖๗๘๙']:
            assert sample in text, f'Thai extraction failed: {sample}'
    if file.stem=='images':
        objects=reader.pages[0]['/Resources']['/XObject'].get_object()
        assert len(objects)==3
        assert any('/SMask' in v.get_object() for v in objects.values())
    results.append(dict(file=str(file),pages=len(reader.pages),bytes=file.stat().st_size,strict_read=True,mupdf_repair=False,all_pages_rendered=True))
(out/'results.json').write_text(json.dumps({'pypdf':__import__('pypdf').__version__,'pymupdf':fitz.VersionBind,'results':results},indent=2))
print(json.dumps(results,indent=2))
