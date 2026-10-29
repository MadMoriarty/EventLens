"""Refresh public event-page URLs only; no third-party article text is redistributed."""
import argparse, datetime, hashlib, json, pathlib, re, urllib.parse, urllib.request
from html.parser import HTMLParser

ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCES={'ultimate':'https://www.ultimatewindowssecurity.com/securitylog/encyclopedia/default.aspx',
         'eventsentry':'https://system32.eventsentry.com/security/search'}

class Links(HTMLParser):
    def __init__(self): super().__init__(); self.links=[]
    def handle_starttag(self,tag,attrs):
        if tag=='a': self.links.extend(v for k,v in attrs if k=='href' and v)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--reviewed-on',default=datetime.date.today().isoformat());args=parser.parse_args()
    reviewed=datetime.date.fromisoformat(args.reviewed_on).isoformat()
    cache=ROOT/'.research/knowledge'; cache.mkdir(parents=True,exist_ok=True)
    result={'reviewedOn':reviewed,'sources':{}}
    for source,url in SOURCES.items():
        req=urllib.request.Request(url,headers={'User-Agent':'EventLens-reference-links/1.2'})
        with urllib.request.urlopen(req,timeout=60) as response: raw=response.read()
        (cache/(source+'.html')).write_bytes(raw)
        parser=Links();parser.feed(raw.decode('utf-8-sig'));events={}
        for href in parser.links:
            absolute=urllib.parse.urljoin(url,href); parts=urllib.parse.urlsplit(absolute)
            if source=='ultimate':
                if parts.hostname!='www.ultimatewindowssecurity.com' or not parts.path.lower().endswith('/event.aspx'): continue
                params={k.lower():v for k,v in urllib.parse.parse_qs(parts.query).items()}; value=params.get('eventid',[''])[0]
            else:
                if parts.hostname!='system32.eventsentry.com': continue
                match=re.fullmatch(r'/security/event/(\d+)',parts.path)
                if not match: continue
                value=match[1]
            if not value.isdigit() or not (1100<=int(value)<=1108 or 4600<=int(value)<=5999): continue
            # Store canonical, event-number-only URLs. Do not retain tracking parameters.
            events[value]=('https://www.ultimatewindowssecurity.com/securitylog/encyclopedia/event.aspx?eventID='+value if source=='ultimate' else 'https://system32.eventsentry.com/security/event/'+value)
        if len(events)<250: raise ValueError(f'{source}: suspiciously small index ({len(events)})')
        result['sources'][source]={'url':url,'sha256':hashlib.sha256(raw).hexdigest(),'events':dict(sorted(events.items(),key=lambda x:int(x[0])))}
        print(source,len(events),'modern Windows audit page links')
    (ROOT/'src/EventLens.Core/Reference/knowledge-links.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':main()
