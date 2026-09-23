"""Exercise the real local web endpoints and validate the exported PDFs independently.
Run the ASP.NET sample first; this test never uses another engine to generate a PDF.
"""
from pathlib import Path
from urllib.request import Request, urlopen
from urllib.error import HTTPError
from io import BytesIO
import json
import re
import sys
from pypdf import PdfReader
import fitz

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5077'
out = Path('artifacts/web-tests')
out.mkdir(parents=True, exist_ok=True)

def call(payload):
    request = Request(base + '/api/export', json.dumps(payload).encode('utf-8'), {'Content-Type': 'application/json'})
    try:
        with urlopen(request, timeout=60) as response:
            return response.status, response.headers, response.read()
    except HTTPError as error:
        return error.code, error.headers, error.read()

with urlopen(base + '/') as response:
    html = response.read().decode('utf-8')
    assert response.status == 200 and 'Export Studio' in html and 'ดาวน์โหลด PDF' in html
for asset in ['/studio.js', '/studio.css']:
    with urlopen(base + asset) as response:
        assert response.status == 200 and len(response.read()) > 100
with urlopen(base + '/api/status') as response:
    assert json.load(response)['ready'] is True

results = []
for name, payload in [
    ('business', {'rows': 50, 'title': 'Web Export FY26', 'client': 'บริษัท ทดสอบ จำกัด'}),
    ('table-500', {'template': 'table', 'rows': 500}),
    ('table-5000', {'template': 'table', 'rows': 5000, 'orientation': 'landscape', 'paper': 'letter'}),
    ('thai', {'template': 'unicode', 'rows': 0, 'notes': 'รายงาน Audit FY26 – บริษัท ABC จำกัด\nข้อมูลพนักงาน'}),
    ('debug', {'rows': 5, 'debugLayout': True}),
    ('uncompressed', {'rows': 5, 'compress': False})
]:
    status, headers, data = call(payload)
    assert status == 200, (name, status, data[:1000])
    assert headers['Content-Type'].startswith('application/pdf')
    assert headers['Content-Disposition'].startswith('attachment;')
    assert headers['Cache-Control'] == 'no-store'
    assert data.startswith(b'%PDF-1.7') and data.endswith(b'%%EOF\n')
    reader = PdfReader(BytesIO(data), strict=True)
    document = fitz.open(stream=data, filetype='pdf')
    assert not document.is_repaired
    assert int(headers['X-Kypelon-Pages']) == len(reader.pages) == len(document)
    text = '\n'.join(page.extract_text() or '' for page in reader.pages)
    if payload.get('rows', 0) > 0:
        rows = re.findall(r'Employee\s+(\d{4})\b', text)
        assert len(rows) == payload['rows'] and len(set(rows)) == payload['rows'], (name, len(rows))
    if name == 'thai':
        for phrase in ['ภาษาไทย', 'รายงาน Audit FY26 – บริษัท ABC จำกัด', 'ข้อมูลพนักงาน', '๐๑๒๓๔๕๖๗๘๙']:
            assert phrase in text, phrase
    for i, page in enumerate(reader.pages, 1):
        assert f'Page {i} of {len(reader.pages)}' in page.extract_text()
    if name == 'table-5000':
        assert float(reader.pages[0].mediabox.width) == 792
        assert float(reader.pages[0].mediabox.height) == 612
    if name == 'uncompressed':
        assert '/Filter' not in reader.pages[0]['/Contents'].get_object()
    for page in document:
        page.get_pixmap(matrix=fitz.Matrix(.2,.2), alpha=False)
    if name in ['business', 'thai', 'debug']:
        document[0].get_pixmap(matrix=fitz.Matrix(.8,.8), alpha=False).save(out / (name + '.png'))
    (out / (name + '.pdf')).write_bytes(data)
    results.append({'name': name, 'status': status, 'pages': len(reader.pages), 'bytes': len(data), 'rows': payload.get('rows'), 'rendered': True})

for payload in [{'rows': -1}, {'rows': 5001}, {'title': ''}, {'title': None}, {'notes': 'x'*4001}, {'margin': 0}, {'template': 'unknown'}, {'paper': 'unknown'}, {'orientation': 'unknown'}]:
    status, headers, body = call(payload)
    assert status == 400, (payload, status, body[:200])
    assert json.loads(body)['errors']
status, headers, body = call({'notes': 'Unsupported glyph: \U0001F600'})
assert status == 422 and 'detail' in json.loads(body)
assert headers['Content-Type'].startswith('application/problem+json')
with urlopen(base + '/report', timeout=60) as response:
    assert response.status == 200 and response.read(8).startswith(b'%PDF-1.7')
(out / 'results.json').write_text(json.dumps({'exports': results, 'invalid_requests_rejected': 9, 'unsupported_glyph_rejected': True, 'legacy_report_route': True}, indent=2))
print(json.dumps(results, indent=2))
print('9 invalid requests and an unsupported glyph rejected with structured errors; /report passed.')
