"""Test live native eDocket exports; independent libraries only parse/render output."""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from html.parser import HTMLParser
from io import BytesIO
from datetime import datetime, timezone, timedelta
import json
import re
import sys
import fitz
from pypdf import PdfReader

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5077'
out = Path('artifacts/web-tests/edocket')
out.mkdir(parents=True, exist_ok=True)
notes = 'Reviewer note: The engagement team confirmed that the final archival package will include the approved financial statements, required completion documentation, applicable consultation evidence, and the final eDocket export. This intentionally long paragraph is included to validate line wrapping, page-width calculations, font metrics and paragraph spacing in the PDF export library. The implementation should wrap naturally without clipping, overlap, or horizontal overflow.'
default = dict(template='edocket', title='Engagement Summary Report', client='Aurora Retail Holdings Public Company Limited', engagementName='Aurora Retail Holdings - FY2026 Audit', periodEndDate='2026-12-31', notes=notes, rows=6, margin=48, docket=dict(stage='pending'))

def call(patch):
    payload = default | patch
    request = Request(base+'/api/export', json.dumps(payload).encode('utf8'), {'Content-Type':'application/json'})
    try:
        with urlopen(request,timeout=60) as response:
            return response.status, response.headers, response.read()
    except HTTPError as error:
        return error.code, error.headers, error.read()

def norm(text):
    return re.sub(r'\s+', ' ', text).strip()

class FormCheck(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids=[]
        self.templates=[]
    def handle_starttag(self, tag, attrs):
        a=dict(attrs)
        if a.get('id'): self.ids.append(a['id'])
        if tag=='input' and a.get('name')=='template': self.templates.append(a['value'])
with urlopen(base+'/?template=edocket') as response:
    form=FormCheck()
    form.feed(response.read().decode('utf8'))
assert len(form.ids)==len(set(form.ids))
assert set(form.templates)=={'business','table','unicode','aefs','edocket'}
assert all(i in form.ids for i in ['docketId','engagementName','partner','manager','stage','archiveDeadline','generatedAt','sourceVersion'])

results=[]
scenarios=[
    ('edocket-summary', {}),
    ('edocket-ready', {'docket':{'stage':'ready'}}),
    ('edocket-archived', {'docket':{'stage':'archived'}}),
    ('edocket-thai', {'title':'รายงานสรุป eDocket', 'client':'บริษัท ทดสอบ จำกัด', 'engagementName':'รายงาน Audit FY26 - บริษัท ABC จำกัด', 'notes':'รายงานผลการตรวจสอบ ข้อมูลพนักงาน ปีงบประมาณ 2569', 'docket':{'partner':'อนันต์ เจริญ', 'manager':'พิมพ์ชนก วัฒนกุล', 'businessUnit':'งานสอบบัญชี', 'country':'ประเทศไทย', 'repository':'ระบบจัดเก็บเอกสาร'}}),
    ('edocket-long-fields', {'client':'A long client name for the management review and financial statement audit of Aurora Holdings Limited', 'engagementName':'Aurora group annual audit and internal management review for the financial year ended December 2026', 'notes':'\n'.join('Reviewer note %03d: preserve every line of this paragraph.' % i for i in range(1,61)), 'docket':{'partner':'Ananda Charoen Senior Engagement Partner and Quality Review Lead', 'manager':'Pimchanok Wattanakul Engagement Manager and Financial Controls Reviewer'}}),
    ('edocket-custom-data', {'periodEndDate':'2027-03-31','docket':{'id':'EDK-TEST-007','engagementCode':'ENG-TEST-007','aatId':'AAT-TEST-007','archiveDeadline':'2027-04-30','generatedAt':'2027-04-01T13:45','sourceVersion':99}}),
    ('edocket-500-events', {'rows':500}),
    ('edocket-5000-events', {'rows':5000}),
    ('edocket-landscape', {'paper':'letter','orientation':'landscape'}),
    ('edocket-debug', {'debugLayout':True}),
    ('edocket-uncompressed', {'compress':False})
]
for name, patch in scenarios:
    status, headers, data=call(patch)
    assert status==200, (name,status,data[:1000])
    assert headers['Content-Type'].startswith('application/pdf') and headers['Cache-Control']=='no-store'
    assert headers['Content-Disposition'].startswith('attachment;')
    reader=PdfReader(BytesIO(data),strict=True)
    doc=fitz.open(stream=data,filetype='pdf')
    assert not doc.is_repaired
    assert len(reader.pages)==len(doc)==int(headers['X-Kypelon-Pages'])
    if name in ['edocket-summary','edocket-ready','edocket-archived','edocket-thai','edocket-debug','edocket-uncompressed']:
        assert len(doc)==4, (name,len(doc))
    texts=[norm(p.extract_text()) for p in reader.pages]
    all_text=' '.join(texts)
    expected=default|patch
    assert reader.metadata.title==expected['title']
    assert reader.metadata.subject==expected['engagementName']
    timestamp=datetime.fromisoformat(expected['docket'].get('generatedAt','2026-09-23T09:00')).replace(tzinfo=timezone(timedelta(hours=7)))
    assert reader.metadata.creation_date.astimezone(timezone.utc) == timestamp.astimezone(timezone.utc)
    for phrase in [expected['title'],expected['client'],expected['engagementName'],'Readiness & Checklist Status','Sign-off History','Archival & Audit Trail','PDF-'+expected['docket'].get('id','EDK-2026-TH-004218')+'-R1','Source record version','Library validation note.']:
        assert norm(phrase) in all_text, (name,phrase)
    # All note lines and customized names survive wrapping/pagination.
    for line in expected['notes'].splitlines():
        assert norm(line) in all_text, (name,line)
    for key in ['id','engagementCode','aatId','partner','manager','businessUnit','country','repository']:
        if key in expected['docket']: assert norm(expected['docket'][key]) in all_text
    for field, value in [('periodEndDate',expected['periodEndDate']),('archiveDeadline',expected['docket'].get('archiveDeadline','2026-09-30'))]:
        assert datetime.fromisoformat(value).strftime('%d %b %Y') in all_text
    assert str(expected['docket'].get('sourceVersion',42)) in all_text
    stage=expected['docket'].get('stage','pending')
    count=7 if stage=='archived' else 6 if stage=='ready' else 5
    assert f'7 Checklist modules {count} Complete / exempt / N/A {7-count} Outstanding' in texts[0], texts[0]
    if stage=='pending':
        assert 'Blocks eAD1' in all_text and 'SIG-e2313-002' not in all_text
    elif stage=='ready':
        assert 'READY FOR eAD1' in texts[0] and 'Blocks eAD1' not in all_text and 'SIG-e2313-002' in all_text
    else:
        assert 'ARCHIVED' in texts[0] and 'NOT STARTED' not in all_text and 'Not ready -' not in all_text
    extra=re.findall(r'Audit trail event (\d{4})',all_text)
    assert len(extra)==expected['rows']-6
    assert sorted(map(int,extra))==list(range(7,expected['rows']+1))
    assert all(all_text.count(event)==1 for event in ['CS completed','EQCR exemption approved','TR completed','e2313 Lead Manager sign-off'])
    for index,page in enumerate(doc):
        assert f'Page {index+1} of {len(doc)}' in texts[index]
        # Every continued audit table retains all four header labels.
        if 'Audit trail event' in texts[index]:
            for label in ['Timestamp','Event','Actor','Result']: assert label in texts[index]
        width,height=page.rect.width,page.rect.height
        for word in page.get_text('words'):
            assert word[0]>=0 and word[1]>=0 and word[2]<=width+.01 and word[3]<=height+.01,(name,index,word)
            # Footer is within the bottom 30pt; content must stay above the reserved gap.
            assert word[3]<height-35 or word[1]>height-30,(name,index,'footer overlap',word)
        page.get_pixmap(matrix=fitz.Matrix(.25,.25),alpha=False)
    # Dark table headers contain white text in the generated PDF, unlike the source's low-contrast headers.
    spans=[s for p in doc for b in p.get_text('dict')['blocks'] if 'lines' in b for l in b['lines'] for s in l['spans']]
    assert any(s['text']=='Checklist / Gate' and s['color']==0xFFFFFF for s in spans)
    assert any(s['text']=='Timestamp' and s['color']==0xFFFFFF for s in spans)
    if name=='edocket-uncompressed':
        assert all('/Filter' not in p['/Contents'].get_object() for p in reader.pages)
    if name=='edocket-landscape': assert doc[0].rect.width==792 and doc[0].rect.height==612
    (out/(name+'.pdf')).write_bytes(data)
    if name in ['edocket-summary','edocket-thai','edocket-long-fields','edocket-debug']:
        target=out/name
        target.mkdir(exist_ok=True)
        for i,p in enumerate(doc): p.get_pixmap(matrix=fitz.Matrix(1.2,1.2),alpha=False).save(target/f'page-{i+1}.png')
    if name=='edocket-summary':
        Path('artifacts/pdf/edocket-summary.pdf').write_bytes(data)
        assert call({})[2]==data
    results.append(dict(name=name,pages=len(doc),bytes=len(data),events=expected['rows'],rendered=True))

invalid=[{'docket':None},{'docket':{'id':''}},{'docket':{'partner':None}},{'docket':{'manager':'x'*121}},{'docket':{'stage':'unknown'}},{'docket':{'archiveDeadline':'2026-02-30'}},{'docket':{'generatedAt':'not-a-date'}},{'docket':{'sourceVersion':0}},{'docket':{'sourceVersion':1000000}},{'rows':5},{'rows':5001},{'engagementName':None},{'periodEndDate':'bad-date'}]
for patch in invalid:
    status,headers,data=call(patch)
    assert status==400 and json.loads(data)['errors'],(patch,status,data[:1000])
(out/'results.json').write_text(json.dumps({'exports':results,'invalid_requests_rejected':len(invalid),'deterministic':True,'all_events_present':True,'headers_repeat':True},indent=2))
print(json.dumps(results,indent=2))
print(str(len(invalid))+' invalid requests rejected; four sections, workflow states, Thai text, page numbers, footer bounds, repeated headers and all events passed.')
