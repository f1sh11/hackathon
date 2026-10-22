import { useEffect, useState } from 'react';
import type { Trip, TripSummary } from '../../types/contracts';
import { getTrip } from '../../services/travelApi';
import { appleMapsPlaceActions, getPlaceReference, type MapMode } from './appleMaps';
import './maps.css';

type MapScreenProps = { trips: TripSummary[]; selectedTrip: Trip | null; selectedVersion: number };
type MapStop = { id: string; name: string; time?: string; remainingHp?: number; remainingBudget?: number };
const money = (value: number) => new Intl.NumberFormat('en-AU', { maximumFractionDigits: 2 }).format(value);
const explorationIds = ['harbour', 'garden', 'rocks', 'gallery', 'lookout', 'park'];
const positions = [[58, 78], [155, 49], [267, 89], [244, 187], [138, 218], [57, 181]];

function MapPinIcon() {
  return <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2.3" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M19 10c0 5-7 11-7 11S5 15 5 10a7 7 0 0 1 14 0Z" /><circle cx="12" cy="10" r="2.5" /></svg>;
}

export function MapScreen({ trips, selectedTrip, selectedVersion }: MapScreenProps) {
  const [tripId, setTripId] = useState(selectedTrip?.id ?? trips[0]?.id ?? '');
  const [trip, setTrip] = useState<Trip | null>(selectedTrip);
  const [versionNumber, setVersionNumber] = useState(selectedTrip ? selectedVersion : 0);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [mode, setMode] = useState<MapMode>('walking');
  const [returnAddress, setReturnAddress] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setError('');
    setSelectedIndex(0);
    if (!tripId) { setTrip(null); setLoading(false); return; }
    if (selectedTrip?.id === tripId) {
      setTrip(selectedTrip);
      setVersionNumber(selectedTrip.versions.some(version => version.version === selectedVersion) ? selectedVersion : selectedTrip.latestVersion);
      setLoading(false);
      return;
    }
    setTrip(null);
    setLoading(true);
    void getTrip(tripId).then(data => {
      if (!active) return;
      setTrip(data);
      setVersionNumber(data.latestVersion);
    }).catch(cause => {
      if (active) setError(cause instanceof Error ? cause.message : '没有读到这份行程，请重试。');
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [tripId, selectedTrip, selectedVersion, retry]);

  const version = trip?.versions.find(item => item.version === versionNumber);
  const plan = version?.plan;
  let runningCost = 0;
  const stops: MapStop[] = tripId ? (plan?.stops.map(stop => {
    runningCost += stop.cost;
    return { id: stop.id, name: stop.name, time: (stop.id === 'return' ? stop.endTime : stop.startTime).slice(0, 5), remainingHp: stop.remainingHp, remainingBudget: trip!.request.budget - runningCost };
  }) ?? []) : explorationIds.map(id => ({ id, name: getPlaceReference(id).label }));
  const currentIndex = Math.min(selectedIndex, Math.max(0, stops.length - 1));
  const current = stops[currentIndex];
  const actions = current ? appleMapsPlaceActions(current.id, mode, returnAddress) : null;
  const reference = actions?.reference;
  const route = stops.map((_, index) => positions[index % positions.length].join(',')).join(' ');
  const changeTrip = (id: string) => { setTripId(id); setSelectedIndex(0); };

  return <section className="map-screen" aria-label="地图与 Apple Maps 导航">
    <div className="map-intro"><span className="map-eyebrow">LET’S EXPLORE</span><h1>下一站，出发！</h1><p>选好目的地，让 Apple 地图带路。</p></div>
    <div className="map-source-card"><label className="map-label">查看哪份路线？<select value={tripId} onChange={event => changeTrip(event.currentTarget.value)} aria-label="地图选择行程"><option value="">自由探索 · 悉尼地标</option>{trips.map(item => <option key={item.id} value={item.id}>{item.title}</option>)}</select></label>
      {trip && <label className="map-label map-version-label">行程版本<select value={versionNumber} onChange={event => { setVersionNumber(Number(event.currentTarget.value)); setSelectedIndex(0); }} aria-label="地图查看行程版本">{trip.versions.map(item => <option key={item.version} value={item.version}>V{item.version}{item.easier ? ' · 轻松版' : ' · 初始版'}{item.plan ? '' : ' · 无可行解'}</option>)}</select></label>}
    </div>

    {loading ? <div className="map-message" role="status"><MapPinIcon /><h2>正在展开你的冒险地图…</h2><p>读取已保存的行程和版本。</p></div> : error ? <div className="map-message map-error" role="alert"><h2>地图暂时没有读到行程</h2><p>{error}</p><button type="button" className="map-action map-action-secondary" onClick={() => setRetry(value => value + 1)}>重新加载</button></div> : tripId && !plan ? <div className="map-message map-no-route"><MapPinIcon /><h2>这一版还没有可走的路线</h2><p>先调整行程条件，再回来展开地图。</p><ul>{(version?.conflicts.length ? version.conflicts : ['这份任务尚无可显示的行程。']).map(conflict => <li key={conflict}>{conflict}</li>)}</ul><button type="button" className="map-action map-action-secondary" onClick={() => changeTrip('')}>先探索悉尼地标</button></div> : <>
      <div className="map-canvas-wrap"><div className="map-canvas-label"><MapPinIcon /><span>路线示意 · 非实际道路</span></div>
        <svg className="map-canvas" viewBox="0 0 340 286" role="group" aria-label="交互式路线示意，非实际道路">
          <title>点击编号选择一站；背景为插画，不是 Apple 地图底图。</title>
          <rect width="340" height="286" fill="#f2f9df" />
          <path d="M0 0h340v52c-28 10-41 29-67 24-33-5-37-34-75-27-45 10-41 29-82 17C74 53 59 27 0 50Z" fill="#c7edff" />
          <path d="M0 257c42-14 71-8 112 5 43 14 64-18 101-7 45 14 91 1 127-13v44H0Z" fill="#c7edff" />
          <path d="m-13 136 370 0M104 0l-5 286M218 0l-7 286" stroke="#fff" strokeWidth="20" />
          <path d="m-13 136 370 0M104 0l-5 286M218 0l-7 286" stroke="#e3ecd1" strokeWidth="1.5" strokeDasharray="5 8" />
          <rect x="132" y="102" width="51" height="48" rx="14" fill="#d3ed9b" /><rect x="272" y="211" width="48" height="34" rx="12" fill="#d3ed9b" />
          <path d="M139 123h37m-25-9v20m12-20v20" stroke="#b2d779" strokeWidth="4" strokeLinecap="round" />
          <g fill="#a9dc70"><circle cx="28" cy="230" r="11" /><circle cx="27" cy="215" r="13" /><circle cx="302" cy="142" r="12" /><circle cx="304" cy="127" r="15" /></g><g stroke="#87b75c" strokeWidth="5" strokeLinecap="round"><path d="M28 232v13M303 147v13" /></g>
          <g fill="#ffde8e" stroke="#edbf52" strokeWidth="2"><rect x="26" y="114" width="27" height="24" rx="5" /><rect x="272" y="166" width="29" height="22" rx="5" /></g>
          <polyline points={route} fill="none" stroke="#fff" strokeWidth="10" strokeLinecap="round" strokeLinejoin="round" /><polyline points={route} fill="none" stroke="#58cc02" strokeWidth="5" strokeDasharray="3 8" strokeLinecap="round" strokeLinejoin="round" />
          {stops.map((stop, index) => { const [x, y] = positions[index % positions.length]; const selected = index === currentIndex; return <g key={`${stop.id}-${index}`} className="map-node" role="button" tabIndex={0} aria-label={`选择第 ${index + 1} 站 ${stop.name}`} aria-pressed={selected} onClick={() => setSelectedIndex(index)} onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setSelectedIndex(index); } }}><rect x={x - 25} y={y - 25} width="50" height="50" fill="transparent" /><circle cx={x} cy={y + 4} r={selected ? 22 : 18} fill={selected ? '#46a302' : '#d1d9ca'} /><circle cx={x} cy={y} r={selected ? 22 : 18} fill={selected ? '#58cc02' : '#fff'} stroke={selected ? '#46a302' : '#b7c8aa'} strokeWidth="2.5" /><text x={x} y={y + 1} dominantBaseline="middle" textAnchor="middle" fill={selected ? '#fff' : '#638548'} fontSize={selected ? 19 : 15} fontWeight="900">{index + 1}</text></g>; })}
          <text x="273" y="29" className="map-water-label">悉尼 · 示意</text>
        </svg>
        <span className="map-canvas-bottom">{tripId ? `${stops.length} 站 · 点击编号查看` : '先认识几个悉尼地标'}</span>
      </div>

      <div className="map-stop-tabs" aria-label="选择地图站点">{stops.map((stop, index) => <button type="button" key={`${stop.id}-${index}`} className="map-stop-tab" aria-pressed={index === currentIndex} onClick={() => setSelectedIndex(index)}><b>{index + 1}</b><span>{stop.name.split(' · ')[0]}</span></button>)}</div>
      {current && actions && reference && <article className="map-destination"><div className="map-destination-top"><span className="map-level">第 {currentIndex + 1} 站</span><span className="map-connect-badge">Apple Maps ↗</span></div><h2>{current.name}</h2>
        {tripId && <div className="map-stop-metrics"><span>{current.time} 到达</span><span>余力 HP {current.remainingHp}</span><span>预算余 A${money(current.remainingBudget ?? 0)}</span></div>}
        <p className="map-reference">{reference.kind === 'landmark' ? `地点参考：${reference.label}` : reference.kind === 'return' ? '回到你填写的出发 / 返程地点' : '在 Apple 地图中搜索并确认具体地点'}</p><p className="map-reference-note">{reference.note}</p>
        <div className="map-modes" aria-label="Apple 地图交通方式">{([['walking', '步行'], ['driving', '驾车'], ['transit', '公交']] as const).map(([value, label]) => <button type="button" key={value} aria-pressed={mode === value} onClick={() => setMode(value)}>{label}</button>)}</div>
        <div className="map-apple-actions">{actions.directionsUrl ? <a className="map-action map-action-primary" href={actions.directionsUrl} target="_blank" rel="noopener noreferrer">用 Apple 地图导航 <span aria-hidden="true">↗</span></a> : <button type="button" className="map-action map-action-primary" disabled>{reference.kind === 'return' ? '先填写返程地点' : '确认具体地点后再导航'}</button>}{actions.searchUrl && <a className="map-action map-action-secondary" href={actions.searchUrl} target="_blank" rel="noopener noreferrer">{reference.kind === 'search' ? '在 Apple 地图搜索地点' : '在 Apple 地图查看'} <span aria-hidden="true">↗</span></a>}</div>
        <p className="map-handoff-note">点击后打开 Apple 地图。导航起点、实际路线和耗时在 Apple 地图中确认。</p>
      </article>}
      {tripId && stops.some(stop => stop.id === 'return') && <section className="map-return-card"><label className="map-label">出发 / 返程地点<input type="text" value={returnAddress} maxLength={300} onChange={event => setReturnAddress(event.currentTarget.value)} placeholder="例如：酒店名称或完整地址" autoComplete="off" /></label><p>填好后，选择“返回出发点”就能打开返程导航。此处地址仅用于本次跳转，不会改动已保存行程。</p></section>}
    </>}
    <p className="map-footnote">App 内为路线插画；地标是示例地点参考。行程费用、HP 与交通时间仍为演示估算，Apple 地图导航不会自动重算这些数值。</p>
  </section>;
}
