/// <reference types="vite/client" />
import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { api, type Bootstrap, type Profile, type Trip, type TripRequest, type TripSummary, type TripVersion } from './api';
import { MapScreen } from './MapScreen';
import './styles.css';

const questions = [
  { title: '一天走多少路，\n你会觉得舒服？', hint: '按平时的自己来选，画像没有高分或低分之分。', options: ['慢慢走，约 2–4 公里就好', '5–8 公里，边走边停', '喜欢步行，9 公里以上也可以'] },
  { title: '遇到台阶和上坡，\n你会怎么想？', hint: '我们会据此考虑路线坡度与预计体力消耗。', options: ['尽量平路，能坐电梯更好', '少量坡道可以接受', '愿意登高，风景值得多走一点'] },
  { title: '你想要\n怎样的一天？', hint: '留白也是旅行的一部分。', options: ['只去 1–2 个重点，留足休息', '3 个左右的重点，松紧适中', '想多看一些，行程可以紧凑'] },
  { title: '值得的体验，\n愿意多花一点吗？', hint: '只了解消费偏好，实际预算在创建任务时填写。', options: ['省钱优先，免费也能很好玩', '特别喜欢的，可以适当多花', '在预算内，体验和舒适更重要'] },
  { title: '一个小众地点，\n值得你绕路吗？', hint: '经典景点与意外发现，你更喜欢哪种？', options: ['先看经典，不想特意绕路', '顺路探索，也愿意小幅绕路', '喜欢发现，愿意专程前往'] },
  { title: '如果计划临时变了，\n你会怎样？', hint: '了解你对等待和变动的接受程度。', options: ['希望确定，尽量少等待和变动', '少量排队或调整可以接受', '比较随性，现场决定也没关系'] },
];
const axes = ['步行耐受', '爬坡耐受', '行程密度', '消费意愿', '探索意愿', '变化接受'];
const shortTime = (v: string) => v.slice(0, 5);
const minuteOfDay = (v: string) => Number(v.slice(0, 2)) * 60 + Number(v.slice(3, 5));
const currency = (n: number) => new Intl.NumberFormat('en-AU', { maximumFractionDigits: 2 }).format(n);
const dateLabel = (v: string) => new Intl.DateTimeFormat('zh-CN', { month: 'long', day: 'numeric' }).format(new Date(v + 'T12:00:00'));
const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
const reasonLabel = (reason: string) => reason.includes('轻松版') ? '更轻松的安排' : reason.includes('备注') ? '你补充的要求' : /预算|支出/.test(reason) ? '预算与余力' : /平缓|坡度|爬坡/.test(reason) ? '适合你的路线' : /休息|简餐/.test(reason) ? '休息也在计划里' : '照顾你的节奏';
const statusLabel = (s: string) => ({ completed: '已生成', infeasible: '需要调整', failed: '生成失败' }[s] ?? s);
const themeLabel = (s: string) => s === 'harbour' ? '海港与城市' : s === 'culture' ? '文化与咖啡' : '公园与自然';
const initialDraft = (): TripRequest => ({ profileId: '', title: '悉尼，慢慢走的一天', date: today(), startTime: '09:00', endTime: '18:00', budget: 120, energy: 100, theme: 'harbour', notes: '' });
type Screen = 'profile' | 'new' | 'trips' | 'itinerary' | 'map';
type IconName = 'route' | 'plus' | 'person' | 'arrow' | 'back' | 'leaf' | 'check' | 'lock' | 'sun' | 'pin' | 'clock' | 'wallet' | 'heart' | 'map' | 'star' | 'flag';
function Icon({ name, size = 22 }: { name: IconName; size?: number }) {
  const paths: Record<IconName, React.ReactNode> = {
    map: <><path d="m3 5 6-2 6 2 6-2v16l-6 2-6-2-6 2V5Z" /><path d="M9 3v16m6-14v16" /></>,
    star: <path d="m12 2 3 6.5 7 .9-5 5 .9 7.1-5.9-3.4-5.9 3.4.9-7.1-5-5 7-.9Z" />,
    flag: <><path d="M5 21V3m0 1c5-4 9 4 14 0v10c-5 4-9-4-14 0" /></>,
    route: <><circle cx="6" cy="5" r="2" /><circle cx="18" cy="19" r="2" /><path d="M6 7v5a4 4 0 0 0 4 4h4a4 4 0 0 0 0-8h-2M18 17v-3" /></>,
    plus: <path d="M12 5v14M5 12h14" />,
    person: <><circle cx="12" cy="8" r="3.5" /><path d="M5 21v-2a7 7 0 0 1 14 0v2" /></>,
    arrow: <path d="M5 12h14m-6-6 6 6-6 6" />,
    back: <path d="m14 5-7 7 7 7" />,
    leaf: <><path d="M19 4c0 10-3 15-9 15a6 6 0 0 1-6-6C4 7 10 4 19 4Z" /><path d="m5 19 9-9" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    lock: <><rect x="5" y="10" width="14" height="11" rx="3" /><path d="M8 10V6a4 4 0 0 1 8 0v4M12 14v3" /></>,
    sun: <><circle cx="12" cy="12" r="4" /><path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.4 1.4m11.2 11.2L19 19M5 19l1.4-1.4M17.6 6.4 19 5" /></>,
    pin: <><path d="M19 10c0 5-7 11-7 11S5 15 5 10a7 7 0 0 1 14 0Z" /><circle cx="12" cy="10" r="2.5" /></>,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    wallet: <><path d="M20 8V5a2 2 0 0 0-2-2H6a3 3 0 0 0 0 6h14v11H6a3 3 0 0 1-3-3V6" /><path d="M16 12h5v5h-5z" /></>,
    heart: <path d="M20.8 4.6a5.5 5.5 0 0 0-7.8 0l-1 1-1-1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8Z" />,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.65" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name]}</svg>;
}
function Buddy({ size = 124, excited = false }: { size?: number; excited?: boolean }) {
  return <svg className="travel-buddy" width={size} height={size} viewBox="0 0 160 160" role="img" aria-label="漫漫，橙色背包旅行伙伴">
    <ellipse cx="81" cy="146" rx="49" ry="9" fill="#e8edf1" />
    <path d="M61 26v-8c0-12 38-12 38 0v8" fill="none" stroke="#c96d16" strokeWidth="10" strokeLinecap="round" />
    <path d="m34 91-15 15c-8 8-17-2-10-10l16-21M128 84l15-18c7-8 16 1 11 8l-16 25" fill="#ffa32a" stroke="#ed8b18" strokeWidth="4" strokeLinecap="round" />
    <path d="m52 129-7 12c-4 8 9 12 19 9l7-9m24-13 6 15c3 8 19 8 22 2 2-4-6-13-10-18" fill="#1cb0f6" stroke="#1596d3" strokeWidth="4" />
    <rect x="29" y="23" width="102" height="114" rx="31" fill="#ffac32" stroke="#ed8b18" strokeWidth="4" />
    <path d="M36 48c9-23 29-24 44-24h20c13 0 25 10 27 25" fill="#ffc75a" />
    <path d="M45 32v85m70-85v85" stroke="#f38c20" strokeWidth="7" strokeLinecap="round" />
    <rect x="40" y="88" width="80" height="42" rx="17" fill="#ffd657" stroke="#efaa26" strokeWidth="3" />
    <path d="M51 101h58" stroke="#f3b42d" strokeWidth="4" strokeLinecap="round" />
    <circle cx="110" cy="102" r="3" fill="#c97b18" />
    <ellipse cx="61" cy="64" rx="15" ry="19" fill="white" /><ellipse cx="99" cy="64" rx="15" ry="19" fill="white" />
    {excited ? <><path d="m61 50 3 9 8 5-8 4-3 9-4-9-8-4 8-5Z" fill="#3c3c3c" /><path d="m99 50 3 9 8 5-8 4-3 9-4-9-8-4 8-5Z" fill="#3c3c3c" /></> : <><ellipse cx="64" cy="66" rx="7" ry="10" fill="#3c3c3c" /><ellipse cx="96" cy="66" rx="7" ry="10" fill="#3c3c3c" /><circle cx="67" cy="61" r="2.5" fill="white" /><circle cx="99" cy="61" r="2.5" fill="white" /></>}
    <path d="M71 82q9 11 18 0" fill="#b85d16" stroke="#b85d16" strokeWidth="3" strokeLinecap="round" />
    <ellipse cx="45" cy="81" rx="8" ry="4" fill="#ff8354" opacity=".65" /><ellipse cx="115" cy="81" rx="8" ry="4" fill="#ff8354" opacity=".65" />
    <circle cx="80" cy="117" r="11" fill="#fff8db" stroke="#d59723" strokeWidth="2" /><path d="m80 108 4 9-4 9-4-9Z" fill="#1cb0f6" /><path d="m80 108 4 9h-8Z" fill="#ff7162" />
    {excited && <><path d="m141 21 3 7 7 3-7 3-3 7-3-7-7-3 7-3Z" fill="#ffc800" /><circle cx="20" cy="43" r="5" fill="#ce82ff" /></>}
  </svg>;
}
function Radar({ profile }: { profile: Profile }) {
  const ref = useRef<SVGSVGElement>(null);
  const [width, setWidth] = useState(320);
  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    const observer = new ResizeObserver(() => setWidth(el.clientWidth || 320));
    observer.observe(el);
    return () => observer.disconnect();
  }, []);
  const radius = Math.max(35, Math.min((width - 150) / 2, 87));
  const point = (i: number, fraction: number) => [width / 2 + Math.sin(i * Math.PI / 3) * radius * fraction, 133 - Math.cos(i * Math.PI / 3) * radius * fraction];
  return <svg ref={ref} className="radar" viewBox={`0 0 ${width} 270`} role="img" aria-label="根据回答生成的只读六维旅行画像"><title>旅行角色画像，分数由问答自动生成，不能手动调整</title><desc>{axes.map((a, i) => `${a} ${profile.values[i]} 分`).join('，')}</desc>
    {[.25, .5, .75, 1].map(f => <polygon key={f} className="gridline" points={axes.map((_, i) => point(i, f).join(',')).join(' ')} />)}
    {axes.map((a, i) => { const p = point(i, 1), l = point(i, 1.28); return <g key={a}><line className="gridline" x1={width / 2} y1="133" x2={p[0]} y2={p[1]} /><text x={l[0]} y={l[1]} textAnchor={i === 0 || i === 3 ? 'middle' : i < 3 ? 'start' : 'end'} dominantBaseline="middle">{a}</text></g>; })}
    <polygon className="radar-shape" points={profile.values.map((v, i) => point(i, v / 100).join(',')).join(' ')} />
    {profile.values.map((v, i) => { const p = point(i, v / 100); return <circle key={i} className="radar-dot" cx={p[0]} cy={p[1]} r="3.5" />; })}
  </svg>;
}

function App() {
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
    try { const data = await api<Bootstrap>('/api/bootstrap'); setProfile(data.profile); setTrips(data.trips); setScreen(data.profile ? (data.trips.length ? 'trips' : 'profile') : 'profile'); }
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
    try { const p = await api<Profile>('/api/profiles', { answers }); setProfile(p); window.scrollTo({ top: 0 }); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  function newTask(request?: TripRequest) { createKey.current = crypto.randomUUID(); setDraft(request ? { ...request, title: request.title + ' · 调整' } : initialDraft()); navigate('new'); }
  async function createTrip(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!profile || inFlight.current) return;
    if (draft.endTime <= draft.startTime) { setError('最晚返回时间需要晚于出发时间；第一版仅支持当天出发与返回。'); return; }
    if (!draft.title.trim()) { setError('请填写任务名称。'); return; }
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await api<Trip>('/api/trips', { ...draft, profileId: profile.id, title: draft.title.trim(), idempotencyKey: createKey.current }); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); setScreen('itinerary'); createKey.current = crypto.randomUUID(); }
    catch (e) { setError(message(e)); window.scrollTo({ top: 0 }); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function openTrip(id: string) {
    if (inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await api<Trip>(`/api/trips/${id}`); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); setScreen('itinerary'); }
    catch (e) { setError(message(e)); }
    finally { inFlight.current = false; setBusy(false); }
  }
  async function replan() {
    if (!selectedTrip || inFlight.current) return;
    inFlight.current = true; setBusy(true); setError('');
    try { const trip = await api<Trip>(`/api/trips/${selectedTrip.id}/replan`, { easier: true, idempotencyKey: replanKey.current }); setSelectedTrip(trip); setSelectedVersion(trip.latestVersion); addSummary(trip); replanKey.current = crypto.randomUUID(); window.scrollTo({ top: 0 }); }
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
        {screen === 'profile' && !profile && <>
          <div className="intro onboarding-intro"><div className="eyebrow">认识你的旅行角色</div><h1>嗨，冒险家！</h1><p className="subtitle">我是漫漫，先用 6 个小问题认识你。</p></div>
          <section className="questionbox" aria-label="创建旅行画像">
            <div className="question-progress"><span>角色创建</span><strong className="num">{step + 1}<span> / 6</span></strong></div>
            <div className="progress-rail" role="progressbar" aria-label="答题进度" aria-valuemin={0} aria-valuemax={6} aria-valuenow={step + 1}><span style={{ width: ((step + 1) / 6 * 100) + '%' }} /></div>
            <div className="buddy-question"><Buddy size={106} /><div className="speech-bubble"><h2 className="question-title">{questions[step].title}</h2></div></div>
            <p className="subtitle question-hint">{questions[step].hint}</p>
            <div className="choices">{questions[step].options.map((o, i) => <button key={o} type="button" className="choice" aria-pressed={answers[step] === i} onClick={() => setAnswers(prev => prev.map((a, index) => index === step ? i : a))} disabled={busy}><span className="choice-letter">{['A', 'B', 'C'][i]}</span><span>{o}</span><span className="choice-check">{answers[step] === i && <Icon name="check" size={17} />}</span></button>)}</div>
            <div className="question-footer"><button className="button quiet" onClick={() => setStep(s => s - 1)} disabled={step === 0 || busy}><Icon name="back" size={17} />上一题</button><button className="button primary" disabled={answers[step] === null || busy} onClick={() => step === 5 ? void createProfile() : setStep(s => s + 1)}>{busy ? '正在保存…' : step === 5 ? '生成我的角色' : '继续'}<Icon name="arrow" size={19} /></button></div>
          </section><p className="privacy-note"><Icon name="lock" size={13} />问答生成固定画像，雷达不能手动调分</p>
        </>}
        {screen === 'profile' && profile && <>
          <div className="intro"><div className="eyebrow">角色图鉴</div><h1>你的旅行角色登场！</h1><p className="subtitle">没有高低分，只有属于你的旅行节奏。</p></div>
          <section className="profile-visual"><div className="traveler-card-top"><span><Icon name="star" size={13} /> 旅行角色卡</span><span>NO. {profile.id.slice(0, 6).toUpperCase()}</span></div><div className="profile-title"><span className="profile-emblem"><Buddy size={80} excited /></span><div><div className="profile-name">{profile.character}</div><p>{profile.values[2] < 40 ? '慢一点，遇见更多。' : '自在探索，也照顾自己的节奏。'}</p></div></div><Radar profile={profile} /><div className="readonly-badge"><Icon name="lock" size={12} /> 问答生成 · 画像已固定</div></section>
          <section className="profile-traits">{[['节奏', profile.values[2] < 40 ? '悠闲留白' : profile.values[2] > 70 ? '丰富紧凑' : '松紧适中'], ['步行', profile.values[0] < 40 ? '短途优先' : profile.values[0] > 70 ? '喜欢步行' : '边走边停'], ['路线', profile.values[1] < 40 ? '尽量平缓' : profile.values[1] > 70 ? '愿意登高' : '少量坡道'], ['探索', profile.values[4] > 70 ? '小众发现' : profile.values[4] < 40 ? '经典优先' : '顺路探索']].map(([label, text]) => <div key={label}><span className="small">{label}</span><strong>{text}</strong></div>)}</section>
          <div className="soft-callout"><Icon name="heart" size={19} /><p>画像是你的旅行性格。<br /><span>今天的精力、预算和时间，可以在任务里单独填写。</span></p></div><button className="button primary full-button" onClick={() => newTask()}>带着这个角色，出发<Icon name="arrow" size={18} /></button>
        </>}
        {screen === 'new' && profile && <>
          <div className="intro"><div className="eyebrow">新建冒险任务</div><h1>配置这次冒险！</h1><p className="subtitle">带上预算与精力，漫漫来安排路线。</p></div>
          <div className="profile-strip"><span className="mini-avatar"><Buddy size={47} /></span><div><strong>{profile.character}</strong><span>固定画像已载入</span></div><Icon name="lock" size={15} /></div>
          <form onSubmit={e => void createTrip(e)} className="task-form"><section className="panel form-section"><div className="section-heading"><span className="section-number">01</span><h2>这一程，去哪里</h2></div><div className="destination"><Icon name="pin" size={23} /><div><strong>悉尼 Sydney</strong><span>单人一日游 · 当前示例城市</span></div><span className="pill">示例</span></div><label>给旅程起个名字<input name="title" value={draft.title} onChange={e => setDraft({ ...draft, title: e.target.value })} required maxLength={80} disabled={busy} /></label></section>
            <section className="panel form-section"><div className="section-heading"><span className="section-number">02</span><h2>留多少时间与余力</h2></div><div className="form-grid"><label className="full">出行日期<input name="date" type="date" value={draft.date} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, date: value })); }} onChange={e => setDraft({ ...draft, date: e.target.value })} required disabled={busy} /></label><label>出发时间<input name="startTime" type="time" value={draft.startTime} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, startTime: value })); }} onChange={e => setDraft({ ...draft, startTime: e.target.value })} required disabled={busy} /></label><label>最晚返回<input name="endTime" type="time" value={draft.endTime} onInput={e => { const value = e.currentTarget.value; setDraft(prev => ({ ...prev, endTime: value })); }} onChange={e => setDraft({ ...draft, endTime: e.target.value })} required disabled={busy} /></label><label>单日预算 · A$<input name="budget" type="number" inputMode="decimal" min={0} max={10000} step="0.01" value={draft.budget} onChange={e => setDraft({ ...draft, budget: Number(e.target.value) })} required disabled={busy} /></label><label>今天的精力<select name="energy" value={draft.energy} onChange={e => setDraft({ ...draft, energy: Number(e.target.value) })} disabled={busy}><option value={100}>充足 · HP 100</option><option value={70}>一般 · HP 70</option><option value={45}>疲惫 · HP 45</option></select></label></div></section>
            <section className="panel form-section"><div className="section-heading"><span className="section-number">03</span><h2>给这一天一点偏爱</h2></div><div className="mode-buttons" aria-label="行程主题">{([['harbour', '海港与城市', 'sun'], ['culture', '文化与咖啡', 'wallet'], ['nature', '公园与自然', 'leaf']] as [string, string, IconName][]).map(([id, label, icon]) => <button type="button" key={id} className="mode" aria-pressed={draft.theme === id} onClick={() => setDraft({ ...draft, theme: id })} disabled={busy}><Icon name={icon} size={21} /><span>{label}</span></button>)}</div><label>还有想记下的吗？<textarea name="notes" value={draft.notes} onChange={e => setDraft({ ...draft, notes: e.target.value })} placeholder="和海风待一会儿，别太赶。" maxLength={500} disabled={busy} /><span className="field-hint">备注仅保存；本版根据上面的结构化选项规划，未接入 AI。</span></label></section>
            <p className="plan-promise"><Icon name="check" size={16} />校验体力与预算 · 安排休息 · 预留返程</p><button className="button primary full-button" type="submit" disabled={busy}>{busy ? '正在计算并保存行程…' : '生成我的行程'}{!busy && <Icon name="arrow" size={18} />}</button><p className="privacy-note">地点、费用、营业时间和交通均为演示估算</p></form>
        </>}
        {screen === 'trips' && <>
          <div className="intro"><div className="eyebrow">今天，也想出去玩</div><h1>准备好出发了吗？</h1><p className="subtitle">你的下一场小冒险，就从这里开始。</p></div>
          <section className="journey-hero"><div className="hero-top"><div className="hero-copy"><span className="pill"><Icon name="pin" size={12} /> 悉尼 · 一日游</span><h2>世界这么大，<br />一起去看看！</h2><p>按你的节奏，好玩也不累。</p></div><Buddy size={152} excited /></div><button className="hero-action" onClick={() => newTask()} disabled={busy}>规划新冒险<Icon name="plus" size={20} /></button></section>
          <div className="section-title"><h2><Icon name="flag" size={21} /> 我的冒险计划</h2><span className="small">{trips.length ? trips.length + ' 份行程' : '第一程，从这里开始'}</span></div>
          {!trips.length ? <div className="panel empty"><span className="empty-mark"><Icon name="route" size={31} /></span><h2>还没有冒险计划</h2><p className="subtitle">填下时间、预算和精力，<br />让漫漫帮你找到舒服的路线。</p><button className="button primary" onClick={() => newTask()}>创建第一个任务<Icon name="plus" size={18} /></button></div> : <div className="trip-list">{trips.map((t, index) => <button key={t.id} className="trip-card" disabled={busy} onClick={() => void openTrip(t.id)}><span className="quest-marker" aria-hidden="true">{String(index + 1).padStart(2, '0')}</span><div className="trip-card-top"><span className="trip-date"><Icon name="sun" size={17} />{dateLabel(t.request.date)}</span><span className={'status ' + t.status}>{statusLabel(t.status)}</span></div><h2>{t.title}</h2><p className="small">{themeLabel(t.request.theme)} · {shortTime(t.request.startTime)}–{shortTime(t.request.endTime)}</p><div className="trip-card-bottom"><span><Icon name="wallet" size={16} />{'A$' + currency(t.request.budget)}</span><span><Icon name="heart" size={16} /> HP {t.request.energy}</span><span className="trip-version">V{t.latestVersion}<Icon name="arrow" size={18} /></span></div></button>)}</div>}
        </>}
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
function Itinerary({ trip, version, selectedVersion, onVersion, onReplan, onAdjust, onMap, busy }: { trip: Trip; version: TripVersion; selectedVersion: number; onVersion: (v: number) => void; onReplan: () => void; onAdjust: () => void; onMap: () => void; busy: boolean }) {
  const plan = version.plan, request = trip.request;
  const failed = trip.status === 'failed' && version.version === trip.latestVersion;
  return <>
    <div className="intro itinerary-intro"><div className="eyebrow">冒险路线已就绪</div><h1>{plan ? '冒险路线，安排好了！' : failed ? '这次生成，还没完成。' : '给这一天，留点余地。'}</h1><p className="subtitle">{trip.title}</p><div className="itinerary-date"><Icon name="pin" size={14} />悉尼 · {dateLabel(request.date)}<span className="pill">V{version.version}{version.easier ? ' · 轻松版' : ' · 已保存'}</span></div></div>
    {trip.versions.length > 1 && <label className="version-picker"><span>行程版本</span><select aria-label="查看行程版本" value={selectedVersion} onChange={e => onVersion(Number(e.target.value))} disabled={busy}>{trip.versions.map(v => <option value={v.version} key={v.version}>V{v.version}{v.easier ? ' · 轻松版' : ' · 初始版'}{v.plan ? '' : ' · 无可行解'}</option>)}</select></label>}
    {plan ? <>
      <section className="day-summary"><div className="summary-title"><span><Icon name="leaf" size={15} />这一天，心里有数</span><span>预计</span></div><div className="stats"><Metric icon="heart" label="结束余力" value={`${plan.remainingHp}`} unit="HP" detail={`起始 ${request.energy}`} ratio={plan.remainingHp / request.energy} /><Metric icon="wallet" label="总花费" value={`${currency(plan.totalCost)}`} unit="A$" detail={`预算 ${currency(request.budget)}`} ratio={request.budget > 0 ? plan.totalCost / request.budget : 0} /><Metric icon="clock" label="返回时间" value={shortTime(plan.returnTime)} detail={`${shortTime(request.endTime)} 前`} ratio={(minuteOfDay(plan.returnTime) - minuteOfDay(request.startTime)) / Math.max(1, minuteOfDay(request.endTime) - minuteOfDay(request.startTime))} /></div><div className="summary-foot"><span>预计步行 {plan.walkKm.toFixed(1)} km</span><span>预算余 A${currency(request.budget - plan.totalCost)}</span></div></section>
      <button className="button map-view-button full-button" onClick={onMap} disabled={busy}><Icon name="map" size={21} />在地图中查看<Icon name="arrow" size={18} /></button><div className="section-title"><h2><Icon name="flag" size={20} /> 今天的路线</h2><span className="small">{shortTime(request.startTime)} 出发 · 已含返程</span></div><ol className="timeline">{plan.stops.map((stop, index) => { const previousHp = index === 0 ? request.energy : plan.stops[index - 1].remainingHp; const delta = stop.remainingHp - previousHp; const remainingBudget = request.budget - plan.stops.slice(0, index + 1).reduce((sum, item) => sum + item.cost, 0); return <li key={`${stop.id}-${index}`}><div className="time num">{shortTime(stop.startTime)}<span className={'timeline-dot ' + (stop.id === 'return' ? 'finish' : '')}>{stop.id === 'return' ? <Icon name="flag" size={12} /> : index + 1}</span></div><div className="stop"><div className="stop-category">{stop.category}<span>{stop.id === 'return' ? '抵达 ' : '停留至 '}{shortTime(stop.endTime)}</span></div><h3>{stop.name}</h3><p className="small">{stop.description}</p><div className="stop-chips"><span><Icon name="clock" size={12} />转场 {stop.transferMinutes} 分</span><span>A${currency(stop.cost)}</span><span className={delta > 0 ? 'recovery' : ''}>HP {delta > 0 ? '+' : ''}{delta}</span></div><div className="stop-balance" aria-label="本站结束后的预计余力和剩余预算"><span className="stop-remaining-hp">余力 <strong>HP {stop.remainingHp}</strong></span><span className="stop-remaining-budget">预算余 <strong>A${currency(remainingBudget)}</strong></span></div></div></li>; })}</ol>
      <section className="panel why"><div className="section-heading"><Icon name="leaf" size={18} /><h2>照顾你的小细节</h2></div><div className="reason-list">{plan.reasons.map(reason => <div key={reason}><h3>{reasonLabel(reason)}</h3><p>{reason}</p></div>)}</div></section><div className="notice">{plan.dataNotice || '地点、开放时段、路线和费用为示例估算，真实出发前需核验。'}</div>{request.notes && <section className="panel note-panel"><h3>留给这一天的备注</h3><p className="small note-value">{request.notes}</p><p className="field-hint">仅保存，未通过 AI 解析。</p></section>}
    </> : <section className="panel infeasible"><span className="empty-mark"><Icon name="leaf" size={30} /></span><span className="pill">{failed ? '生成失败' : '当前条件下无可行解'}</span><h2>{failed ? '任务已保留，可以重新尝试。' : '不勉强，换一种安排。'}</h2><p className="subtitle">{failed ? '服务没有返回完整行程，请稍后重试或调整要求。' : '试着多留一点时间，或调整预算与本次精力。'}</p><ul>{(version.conflicts.length ? version.conflicts : ['暂时无法在当前条件下找到合适行程，请调整预算、时间或精力后重试。']).map(c => <li key={c}>{c}</li>)}</ul></section>}
    <div className="wide-action"><button className="button primary full-button" onClick={onReplan} disabled={busy || version.easier || !plan}><Icon name="leaf" size={18} />{busy ? '正在重新计算…' : version.easier ? '当前已是轻松版' : '再轻松一点，重新安排'}</button><button className="button full-button" onClick={onAdjust} disabled={busy}>调整要求，创建新任务<Icon name="arrow" size={17} /></button><span className="small action-caption">重新安排会保存为新版本，原行程依然保留</span></div>
  </>;
}
function Metric({ icon, label, value, unit, detail, ratio }: { icon: IconName; label: string; value: string; unit?: string; detail: string; ratio: number }) {
  return <div className="stat"><div className="stat-label"><Icon name={icon} size={12} />{label}</div><strong>{value}<span>{unit}</span></strong><div className="stat-detail">{detail}</div><div className="meter"><span style={{ width: Math.max(0, Math.min(100, ratio * 100)) + '%' }} /></div></div>;
}
const appRoot = createRoot(document.getElementById('root')!);
appRoot.render(<App />);
if (import.meta.hot) {
  import.meta.hot.dispose(() => appRoot.unmount());
}
