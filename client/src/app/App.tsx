import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { Profile, Trip, TripRequest, TripSummary } from '../types/contracts';
import * as travelApi from '../services/travelApi';
import { Icon } from '../components/Icon';
import { TravelBuddy as Buddy } from '../components/TravelBuddy';
import { Questionnaire } from '../features/profile/Questionnaire';
import { ProfileCard } from '../features/profile/ProfileCard';
import { TaskForm } from '../features/tasks/TaskForm';
import { initialDraft } from '../features/tasks/taskDefaults';
import { TripList } from '../features/trips/TripList';
import { Itinerary } from '../features/trips/Itinerary';
import { MapScreen } from '../features/maps/MapScreen';

type Screen = 'profile' | 'new' | 'trips' | 'itinerary' | 'map';

export function App() {
  const [profile, setProfile] = useState<Profile | null>(null);
  const [trips, setTrips] = useState<TripSummary[]>([]);
  const [screen, setScreen] = useState<Screen>('profile');
  const [booting, setBooting] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [answers, setAnswers] = useState<(number | null)[]>(Array(6).fill(null));
  const [step, setStep] = useState(0);
  const [draft, setDraft] = useState<TripRequest>(initialDraft);
  const [selectedTrip, setSelectedTrip] = useState<Trip | null>(null);
  const [selectedVersion, setSelectedVersion] = useState(1);
  const [mapFromItinerary, setMapFromItinerary] = useState(false);
  const inFlight = useRef(false);
  const createKey = useRef(crypto.randomUUID());
  const replanKey = useRef(crypto.randomUUID());
  async function bootstrap() {
    setBooting(true); setError('');
    try { const data = await travelApi.fetchBootstrap(); setProfile(data.profile); setTrips(data.trips); setScreen(data.profile ? (data.trips.length ? 'trips' : 'profile') : 'profile'); }
    catch (e) { setError(message(e)); }
    finally { setBooting(false); }
  }
  useEffect(() => { void bootstrap(); }, []);
  useEffect(() => { window.scrollTo({ top: 0 }); }, [screen, step]);
  function message(e: unknown) { return e instanceof Error ? e.message : '操作未完成，请稍后重试。'; }
  function navigate(next: Screen) { setError(''); setScreen(next); }
  function openMap(fromItinerary = false) { setMapFromItinerary(fromItinerary); navigate('map'); }
  function addSummary(trip: Trip) { setTrips(prev => [{ id: trip.id, title: trip.title, status: trip.status, createdAt: trip.createdAt, latestVersion: trip.latestVersion, request: trip.request }, ...prev.filter(t => t.id !== trip.id)]); }
  async function createProfile() {
    if (inFlight.current || answers.some(a => a === null)) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const p = await travelApi.createProfile(answers as number[]); setProfile(p); window.scrollTo({ top: 0 }); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  function newTask(request?: TripRequest) { createKey.current = crypto.randomUUID(); setDraft(request ? { ...request, title: request.title + ' · 调整' } : initialDraft()); navigate('new'); }
  async function createTrip(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!profile || inFlight.current) return;
    if (draft.endTime <= draft.startTime) { setError('最晚返回时间需要晚于出发时间；第一版仅支持当天出发与返回。'); return; }
    if (!draft.title.trim()) { setError('请填写任务名称。'); return; }
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await travelApi.createTrip({ ...draft, profileId: profile.id, title: draft.title.trim(), idempotencyKey: createKey.current }); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); setScreen('itinerary'); createKey.current = crypto.randomUUID(); }
    catch (e) { setError(message(e)); window.scrollTo({ top: 0 }); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function openTrip(id: string) {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await travelApi.getTrip(id); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); setScreen('itinerary'); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function replan() {
    if (!selectedTrip || inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await travelApi.replanTrip(selectedTrip.id, true, replanKey.current); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); replanKey.current = crypto.randomUUID(); window.scrollTo({ top: 0 }); }
    catch (e) { setError(message(e)); window.scrollTo({ top: 0 }); }
    finally { inFlight.current = false; setBusy(false); }
  }
  const chosen = selectedTrip?.versions.find(v => v.version === selectedVersion) ?? selectedTrip?.versions.at(-1);
  const active = screen === 'itinerary' || screen === 'trips' ? 'trips' : screen;
  return <div id="roam-app"><div className="shell">
    <header className="app-header"><div className="header-brand">{screen === 'itinerary' ? <button className="icon-button" aria-label="返回我的行程" onClick={() => navigate('trips')} disabled={busy}><Icon name="back" /></button> : <span className="brand-symbol"><Icon name="map" size={22} /></span>}<div className="brand">漫游<span>.</span><small>ROAM</small></div></div><span className="demo-badge"><Icon name="star" size={14} /> 本地冒险版</span></header>
    <main>
      {error && <div className="error-banner" role="alert">{error}{!profile && !booting && <button className="text-button" onClick={() => void bootstrap()}>重新连接</button>}</div>}
      {booting ? <div className="loading"><div className="loading-mark"><Buddy size={114} /></div><h1>打开你的旅行档案</h1><p className="subtitle">画像和旅程，正在回到你身边。</p></div> : <>
        {screen === 'profile' && !profile && <Questionnaire answers={answers} step={step} busy={busy} onAnswer={answer => setAnswers(prev => prev.map((value, index) => index === step ? answer : value))} onPrevious={() => setStep(value => value - 1)} onNext={() => setStep(value => value + 1)} onFinish={() => void createProfile()} />}
        {screen === 'profile' && profile && <ProfileCard profile={profile} onCreateTask={() => newTask()} />}
        {screen === 'new' && profile && <TaskForm profile={profile} draft={draft} setDraft={setDraft} busy={busy} onSubmit={event => void createTrip(event)} />}
        {screen === 'trips' && <TripList trips={trips} busy={busy} onCreateTask={() => newTask()} onOpenTrip={id => void openTrip(id)} />}
        {screen === 'itinerary' && selectedTrip && chosen && <Itinerary trip={selectedTrip} version={chosen} selectedVersion={selectedVersion} onVersion={setSelectedVersion} onReplan={() => void replan()} onAdjust={() => newTask(selectedTrip.request)} onMap={() => openMap(true)} busy={busy} />}
        {screen === 'map' && <MapScreen trips={trips} selectedTrip={mapFromItinerary ? selectedTrip : null} selectedVersion={selectedVersion} />}
      </>}
      <footer>本地测试版 · 示例数据<br />HP 为规划估算，未接入实时交通、票价或预订服务</footer>
    </main>
    <nav className="bottom-nav" aria-label="主要导航">
      <button className={'navitem ' + (active === 'trips' ? 'active' : '')} aria-current={active === 'trips' ? 'page' : undefined} onClick={() => navigate('trips')} disabled={!profile || busy || booting}><span className="nav-icon"><Icon name="route" size={25} /></span><span>行程</span></button>
      <button className={'navitem nav-map ' + (active === 'map' ? 'active' : '')} aria-current={active === 'map' ? 'page' : undefined} onClick={() => openMap()} disabled={busy || booting}><span className="nav-icon"><Icon name="map" size={25} /></span><span>地图</span></button>
      <button className={'navitem nav-create ' + (active === 'new' ? 'active' : '')} aria-current={active === 'new' ? 'page' : undefined} onClick={() => active !== 'new' && newTask()} disabled={!profile || busy || booting}><span className="nav-icon"><Icon name="plus" size={25} /></span><span>创建</span></button>
      <button className={'navitem nav-profile ' + (active === 'profile' ? 'active' : '')} aria-current={active === 'profile' ? 'page' : undefined} onClick={() => navigate('profile')} disabled={busy || booting}><span className="nav-icon"><Icon name="person" size={25} /></span><span>画像</span></button>
    </nav>
  </div></div>;
}
