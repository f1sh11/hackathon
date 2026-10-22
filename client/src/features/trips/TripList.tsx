import type { TripSummary } from '../../types/contracts';
import { Icon } from '../../components/Icon';
import { TravelBuddy as Buddy } from '../../components/TravelBuddy';
import { currency, dateLabel, shortTime, statusLabel, themeLabel } from '../../utils/format';

type TripListProps = { trips: TripSummary[]; busy: boolean; onCreateTask: () => void; onOpenTrip: (id: string) => void };

export function TripList({ trips, busy, onCreateTask, onOpenTrip }: TripListProps) {
  return <>
          <div className="intro"><div className="eyebrow">今天，也想出去玩</div><h1>准备好出发了吗？</h1><p className="subtitle">你的下一场小冒险，就从这里开始。</p></div>
          <section className="journey-hero"><div className="hero-top"><div className="hero-copy"><span className="pill"><Icon name="pin" size={12} /> 悉尼 · 一日游</span><h2>世界这么大，<br />一起去看看！</h2><p>按你的节奏，好玩也不累。</p></div><Buddy size={152} excited /></div><button className="hero-action" onClick={() => onCreateTask()} disabled={busy}>规划新冒险<Icon name="plus" size={20} /></button></section>
          <div className="section-title"><h2><Icon name="flag" size={21} /> 我的冒险计划</h2><span className="small">{trips.length ? trips.length + ' 份行程' : '第一程，从这里开始'}</span></div>
          {!trips.length ? <div className="panel empty"><span className="empty-mark"><Icon name="route" size={31} /></span><h2>还没有冒险计划</h2><p className="subtitle">填下时间、预算和精力，<br />让漫漫帮你找到舒服的路线。</p><button className="button primary" onClick={() => onCreateTask()}>创建第一个任务<Icon name="plus" size={18} /></button></div> : <div className="trip-list">{trips.map((t, index) => <button key={t.id} className="trip-card" disabled={busy} onClick={() => onOpenTrip(t.id)}><span className="quest-marker" aria-hidden="true">{String(index + 1).padStart(2, '0')}</span><div className="trip-card-top"><span className="trip-date"><Icon name="sun" size={17} />{dateLabel(t.request.date)}</span><span className={'status ' + t.status}>{statusLabel(t.status)}</span></div><h2>{t.title}</h2><p className="small">{themeLabel(t.request.theme)} · {shortTime(t.request.startTime)}–{shortTime(t.request.endTime)}</p><div className="trip-card-bottom"><span><Icon name="wallet" size={16} />{'A$' + currency(t.request.budget)}</span><span><Icon name="heart" size={16} /> HP {t.request.energy}</span><span className="trip-version">V{t.latestVersion}<Icon name="arrow" size={18} /></span></div></button>)}</div>}
        </>;
}
