"""Rebuild embedded reference data from Microsoft Learn. Python 3, standard library only.
Run from the repository root. Cached HTML is private build input; --refresh downloads it.
Descriptions are Microsoft reference text, not model-generated diagnoses.
"""
import argparse, datetime, hashlib, json, pathlib, re, urllib.request
from html.parser import HTMLParser

ROOT = pathlib.Path(__file__).resolve().parents[1]
BASE = 'https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/'
SOURCES = {
    'ntstatus': BASE + '596a1078-e883-4972-9bbc-49e60bebca55',
    'win32': BASE + '18d8fbe8-a967-4f1c-ae50-99ca8e491d2d',
    'hresult': BASE + '705fb797-2175-4a90-b5a3-3918024b10b8',
    'kerberos': 'https://learn.microsoft.com/en-us/windows/security/threat-protection/auditing/event-4771',
    'security': 'https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/appendix-l--events-to-monitor',
}

class Tables(HTMLParser):
    def __init__(self):
        super().__init__(); self.rows=[]; self.row=None; self.cell=None
    def handle_starttag(self, tag, attrs):
        if tag == 'tr': self.row=[]
        if tag == 'td' and self.row is not None: self.cell=[]
    def handle_endtag(self, tag):
        if tag == 'td' and self.cell is not None:
            self.row.append(re.sub(r'\s+', ' ', ' '.join(self.cell)).strip()); self.cell=None
        if tag == 'tr' and self.row is not None:
            self.rows.append(self.row); self.row=None
    def handle_data(self, data):
        if self.cell is not None: self.cell.append(data)

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('--refresh', action='store_true'); args=parser.parse_args()
    cache=ROOT/'.research/catalog'; cache.mkdir(parents=True,exist_ok=True)
    result={'retrievedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'sources':{}, 'codes':{}, 'security':{}}
    for kind,url in SOURCES.items():
        path=cache/(kind+'.html')
        if args.refresh or not path.exists():
            req=urllib.request.Request(url,headers={'User-Agent':'EventLens-reference-builder/1.1'})
            with urllib.request.urlopen(req, timeout=60) as response: path.write_bytes(response.read())
        raw=path.read_bytes(); table=Tables(); table.feed(raw.decode('utf-8-sig'))
        entries={}
        for row in table.rows:
            if kind=='security':
                # Only modern Security-auditing IDs; legacy IDs and non-Security providers are not inferred.
                if len(row)==4 and row[0].isdigit() and 4600<=int(row[0])<=5999:
                    result['security'][row[0]]=row[3]
                continue
            if len(row)<2: continue
            match=re.fullmatch(r'(0x[0-9a-fA-F]+)\s+([A-Za-z_][A-Za-z_0-9]+)',row[0])
            if kind=='kerberos':
                if len(row)<3 or not re.fullmatch('0x[0-9a-fA-F]+',row[0]): continue
                if not re.fullmatch(r'(KDC_ERR_|KRB_|KRB_AP_)[A-Z_0-9]+',row[1]): continue
                value=int(row[0],16); symbol=row[1]; description=row[2]
            elif match:
                value=int(match[1],16); symbol=match[2]; description=row[1]
            else: continue
            entries.setdefault(str(value),[]).append({'symbol':symbol,'description':description})
        count=len(result['security']) if kind=='security' else sum(map(len,entries.values()))
        minimum={'ntstatus':1000,'win32':2000,'hresult':2000,'kerberos':40,'security':150}[kind]
        if count<minimum: raise ValueError(f'{kind}: only {count} entries; reference layout may have changed')
        result['sources'][kind]={'url':url,'sha256':hashlib.sha256(raw).hexdigest(),'entries':count}
        if kind!='security': result['codes'][kind]=entries
        print(f'{kind}: {count} documented entries')
    target=ROOT/'src/EventLens.Core/Reference/catalog.json'; target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(result,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')

if __name__=='__main__': main()
