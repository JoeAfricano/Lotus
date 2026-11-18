"""Independent descriptive analysis. Never smooths samples or reports optical input latency."""
import csv, json, math, re, statistics, sys
from collections import Counter
from pathlib import Path

def percentile(values, p):
    v=sorted(values)
    return v[max(0,math.ceil(len(v)*p)-1)] if v else None

def analyze(folder):
    rows=list(csv.DictReader((folder/'presentmon.csv').open(encoding='utf-8-sig')))
    valid=[r for r in rows if 12<=float(r['TimeInSeconds'])<=154]
    chain=Counter(r['SwapChainAddress'] for r in valid).most_common(1)[0][0]
    valid=[r for r in valid if r['SwapChainAddress']==chain]
    def values(key): return [float(r[key]) for r in valid if r.get(key) not in ('','NA',None) and float(r[key])>0]
    present=values('msBetweenPresents');display=values('msBetweenDisplayChange');latency=values('msUntilDisplayed')
    def summary(v):
        med=statistics.median(v)
        tail=sorted(v,reverse=True)[:max(1,math.ceil(len(v)*.01))]
        return dict(N=len(v),MeanMs=statistics.mean(v),MedianMs=med,StdMs=statistics.pstdev(v),P99Ms=percentile(v,.99),
                    P99AbsoluteJitterMs=percentile([abs(x-med) for x in v],.99),Low1Fps=1000/statistics.mean(tail),
                    Over1_5x=sum(x>med*1.5 for x in v),Over50ms=sum(x>50 for x in v),MaxMs=max(v))
    logs='\n'.join(p.read_text(errors='replace') for p in folder.glob('collector*.log'))
    loss={k:max([int(r[k]) for r in rows if (r.get(k) or '').isdigit()] or [-1]) for k in ['EtwEventsLost','EtwBuffersLost','OverflowedPresents']}
    reported={k:max([int(x) for x in re.findall(k+r'=(\d+)',logs)] or [-1]) for k in ['EventsLost','BuffersLost','OverflowedPresents']}
    result=dict(Case=folder.name,WindowSeconds=[12,154],Present=summary(present),Display=summary(display),
                PresentToDisplayMedianMs=statistics.median(latency),PresentToDisplayP95Ms=percentile(latency,.95),
                Dropped=sum(r['Dropped']=='1' for r in valid),Modes=dict(Counter(r['PresentMode'] for r in valid)),
                EtwLoss=loss,ReportedEtwCounters=reported,CollectorStoppedNormally='Stopped recording.' in logs,
                LatencyScope='Present call to display; excludes input sampling and some pre-Present waits. Not click-to-photon.')
    # A separate, identical steady-scene window for every run. Keep the complete primary window above.
    valid=[r for r in rows if 30<=float(r['TimeInSeconds'])<=150 and r['SwapChainAddress']==chain]
    result['SteadyScene']=dict(WindowSeconds=[30,150],Present=summary(values('msBetweenPresents')),
        Display=summary(values('msBetweenDisplayChange')),PresentToDisplayMedianMs=statistics.median(values('msUntilDisplayed')),
        PresentToDisplayP95Ms=percentile(values('msUntilDisplayed'),.95))
    (folder/'analysis.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    return result

root=Path(sys.argv[1])
results=[analyze(p) for p in sorted(root.iterdir()) if p.is_dir() and (p/'complete.json').exists() and (p/'presentmon.csv').exists()]
(root/'comparison.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
for r in results:
    print(r['Case'],json.dumps({k:r[k] for k in ['Present','Display','PresentToDisplayMedianMs','Dropped','EtwLoss']}))
