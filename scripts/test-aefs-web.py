"""Live AEFS-template regression: Kypelon.Pdf generates; pypdf/PyMuPDF only inspect/render."""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from io import BytesIO
from html.parser import HTMLParser
import json
import re
import sys
import fitz
from pypdf import PdfReader

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5077'
out = Path('artifacts/web-tests')
out.mkdir(parents=True, exist_ok=True)
def call(patch):
    request = Request(base + '/api/export', json.dumps(default | patch).encode('utf-8'), {'Content-Type': 'application/json'})
    try:
        with urlopen(request, timeout=30) as response:
            return response.status, response.headers, response.read()
    except HTTPError as error:
        return error.code, error.headers, error.read()

def normalize(text):
    return re.sub(r'\s+', ' ', text).strip()

def verify(name, payload):
    status, headers, data = call(payload)
    assert status == 200, (name, status, data[:1000])
    assert headers['Content-Type'].startswith('application/pdf')
    assert headers['Cache-Control'] == 'no-store'
    assert headers['X-Kypelon-Pages'] == '1'
    reader = PdfReader(BytesIO(data), strict=True)
    document = fitz.open(stream=data, filetype='pdf')
    assert not document.is_repaired and len(reader.pages) == len(document) == 1
    page = reader.pages[0]
    text = normalize(page.extract_text())
    expected = default | payload
    for phrase in [expected['title'], expected['recipientName'], expected['engagementName'], expected['notes'], '31 December 2026', 'Open ' + expected['requestType'] + ' request', 'This is a system-generated notification from AEFS. Please do not reply to this email.']:
        assert normalize(phrase) in text, (name, phrase, text)
    assert re.sub(r'\s+', '', expected['requestId']) in re.sub(r'\s+', '', text), 'Wrapped request ID lost characters'
    assert '{{' not in text and 'KYPELON / REPORT STUDIO' not in text
    assert 'Page 1 of' not in text
    assert reader.metadata.title == expected['title']
    assert reader.metadata.subject == expected['engagementName']
    assert len(page['/Annots']) == 1
    annotation = page['/Annots'][0].get_object()
    assert annotation['/Subtype'] == '/Link' and annotation['/A']['/S'] == '/URI'
    uri = annotation['/A']['/URI']
    assert all(ord(c) < 128 for c in uri), uri
    assert uri == expected['recordUrl'] or (name == 'aefs-thai' and '%E0%B8' in uri)
    link = document[0].get_links()[0]
    assert link['kind'] == fitz.LINK_URI and link['uri'] == uri
    bounds = link['from']
    # The visible dark button and the link must occupy the same logical rectangle.
    buttons = [d for d in document[0].get_drawings() if d['fill'] and max(abs(v-32/255) for v in d['fill']) < .002]
    assert any(max(abs(a-b) for a,b in zip(d['rect'], bounds)) < .01 for d in buttons)
    width, height = float(page.mediabox.width), float(page.mediabox.height)
    assert 0 <= bounds.x0 < bounds.x1 <= width and 0 <= bounds.y0 < bounds.y1 <= height
    for word in document[0].get_text('words'):
        assert word[0] >= 0 and word[1] >= 0 and word[2] <= width + .01 and word[3] <= height + .01, word
    content = page['/Contents'].get_object().get_data()
    assert content.count(b' c\n') >= 16, 'Rounded Details corners missing'
    document[0].get_pixmap(matrix=fitz.Matrix(1.2,1.2),alpha=False).save(out / (name + '.png'))
    (out / (name + '.pdf')).write_bytes(data)
    return {'name': name, 'pages': 1, 'bytes': len(data), 'clickable_link': True}, data

default = {
    'template': 'aefs', 'title': 'Request pending your approval', 'recipientName': 'Krittanai',
    'requestId': 'AEFS-2026-0042', 'engagementName': 'ABC Company Limited – FY26 Audit',
    'periodEndDate': '2026-12-31', 'requestType': 'eForm',
    'recordUrl': 'https://aefs.example/requests/AEFS-2026-0042',
    'notes': 'A new request has been submitted and is ready for your review. Please review the details below and open the request to continue.',
    'margin': 24
}
with urlopen(base + '/?template=aefs') as response:
    html = response.read().decode('utf-8')
class FormCheck(HTMLParser):
    ids = []
    templates = []
    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if a.get('id'): self.ids.append(a['id'])
        if tag == 'input' and a.get('name') == 'template': self.templates.append(a['value'])
form = FormCheck()
form.feed(html)
assert len(form.ids) == len(set(form.ids)), 'Duplicate IDs'
assert set(form.templates) == {'business', 'table', 'unicode', 'aefs', 'edocket'}
assert all(key in form.ids for key in ['recipientName', 'requestId', 'engagementName', 'periodEndDate', 'requestType', 'recordUrl'])

results = []
for name, patch in [
    ('aefs-notification', {}),
    ('aefs-thai', {'title': 'คำขอรอการอนุมัติ', 'recipientName': 'กฤตนัย', 'requestId': 'คำขอ-๒๕๖๙-0042', 'engagementName': 'บริษัท ทดสอบ จำกัด – รายงาน Audit FY26', 'notes': 'รายงานผลการตรวจสอบ และข้อมูลพนักงาน ปีงบประมาณ 2569\nกรุณาตรวจสอบข้อมูลคำขอด้านล่าง', 'recordUrl': 'https://aefs.example/คำขอ/42?view=details'}),
    ('aefs-wrapped', {'title': 'A request for the financial year engagement is waiting for your review', 'recipientName': 'Management and engagement review team', 'requestId': 'AEFS-' + 'X' * 70, 'requestType': 'Annual financial engagement approval', 'engagementName': 'ABC Company Limited – FY26 Audit, internal management and financial controls review', 'notes': 'Please review the following information. ' * 8}),
    ('aefs-landscape', {'paper': 'letter', 'orientation': 'landscape'}),
    ('aefs-debug', {'debugLayout': True}),
    ('aefs-uncompressed', {'compress': False})
]:
    result, data = verify(name, patch)
    results.append(result)
    if name == 'aefs-notification':
        Path('artifacts/pdf/aefs-notification.pdf').write_bytes(data)
        assert call({})[2] == data, 'Deterministic output changed'
    if name == 'aefs-thai': Path('artifacts/pdf/aefs-thai.pdf').write_bytes(data)

invalid = [
    {'recipientName': ''}, {'recipientName': None}, {'recipientName': 'X'*121},
    {'engagementName': ''}, {'requestId': None}, {'requestId': 'X'*81},
    {'periodEndDate': '2026-02-30'}, {'periodEndDate': None}, {'requestType': ''},
    {'recordUrl': None}, {'recordUrl': 'javascript:alert(1)'}, {'recordUrl': 'file:///C:/local.txt'},
    {'recordUrl': '/request/42'}, {'recordUrl': 'https://aefs.example/\nheader'}, {'recordUrl': 'https://example.com/'+'x'*2048}
]
for patch in invalid:
    status, headers, data = call(patch)
    assert status == 400 and json.loads(data)['errors'], (patch, status, data[:1000])
status, headers, data = call({'notes': 'Long message line.\n'*200})
assert status == 422 and 'AEFS notification needs' in json.loads(data)['detail']
assert headers['Content-Type'].startswith('application/problem+json')
# No browser/HTML interpreter is involved; input remains literal text in the PDF.
status, headers, data = call({'notes': '<script>alert(1)</script> & <b>literal text</b>'})
assert status == 200
assert '<script>alert(1)</script> & <b>literal text</b>' in PdfReader(BytesIO(data)).pages[0].extract_text()
(out / 'aefs-results.json').write_text(json.dumps({'exports':results, 'invalid_requests':len(invalid), 'oversize_rejected_before_streaming':True, 'literal_markup_preserved':True},indent=2))
print(json.dumps(results, indent=2))
print(str(len(invalid)) + ' invalid requests rejected; overflow, determinism, Unicode extraction, URI and button bounds passed.')
