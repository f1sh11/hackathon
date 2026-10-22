import { mapLink, money, shortDate } from '../domain';
import type { Trip, Weather } from '../types';
import { Icon } from './Icon';
import { RouteSketch } from './RouteSketch';

const modeLabels = { demo: '演示规划', assisted: 'AI 推荐 · 示例数据', live: '在线数据规划' };
interface Props {
  trip: Trip;
  dayIndex: number;
  onDayChange: (index: number) => void;
  weather?: Weather | null;
  busy: boolean;
  onWeather: () => void;
  onRain: () => void;
}
/** 负责行程、天气和证据展示；保存决策由 App 管理。 / Renders itinerary and evidence; App owns saving decisions. */
export function TripView({ trip, dayIndex, onDayChange, weather, busy, onWeather, onRain }: Props) {
  const day = trip.days[dayIndex] ?? trip.days[0];
  if (!day) return <section className="card empty-state">这份行程尚未包含地点。</section>;
  const currentWeather = weather ?? trip.weather;
  const forecast = currentWeather.days?.find((row) => row.date === day.date);
  // 未知天气使用中性云图标，不把缺失数据画成晴天。 / Unknown weather gets a neutral cloud, never a sunny assumption.
  const unknownWeather =
    forecast?.available === false ||
    ['unavailable', 'out_of_range'].includes(currentWeather.status ?? '');
  return (
    <>
      <div className="day-tabs" role="tablist" aria-label="选择行程日期">
        {trip.days.map((item, i) => (
          <button
            type="button"
            key={item.date}
            role="tab"
            aria-selected={i === dayIndex}
            className={i === dayIndex ? 'active' : ''}
            onClick={() => onDayChange(i)}
          >
            <span>DAY {String(i + 1).padStart(2, '0')}</span>
            {shortDate(item.date)}
          </button>
        ))}
      </div>
      <div className="trip-grid">
        <div className="itinerary-column">
          <section className="day-summary">
            <span>
              <Icon name="walk" />
              <strong>{day.estimatedSteps.toLocaleString()}</strong>
              <small>预计步数</small>
            </span>
            <span>
              <Icon name="bag" />
              <strong>{money(Math.round(day.estimatedCost * 100))}</strong>
              <small>预计活动费</small>
            </span>
            <span>
              <Icon name="clock" />
              <strong>{day.finishTime}</strong>
              <small>预计返回</small>
            </span>
          </section>
          <section className="timeline card">
            <div className="section-heading">
              <div>
                <span className="eyebrow">A DAY WELL SPENT</span>
                <h2>今天，走这条路</h2>
              </div>
              <span className="mini-pill">步行探索</span>
            </div>
            <div className="journey-endpoint">
              <span className="endpoint-dot" />
              <div>
                <strong>
                  {trip.profile.startMinute / 60 < 12 ? '早上' : '出发'}{' '}
                  {String(Math.floor(trip.profile.startMinute / 60)).padStart(2, '0')}:
                  {String(trip.profile.startMinute % 60).padStart(2, '0')} 出发
                </strong>
                <small>{day.start.name}</small>
              </div>
            </div>
            {day.stops.map((stop, i) => (
              <article className="stop" key={stop.id}>
                <div className="walk-leg">
                  <Icon name="walk" size={13} />
                  步行约 {day.legs[i]?.minutes ?? '—'} 分钟<span>含衔接与休息安排</span>
                </div>
                <div className="stop-main">
                  <span className="stop-number">{String(i + 1).padStart(2, '0')}</span>
                  <div className="stop-content">
                    <div className="stop-meta">
                      <span>
                        {stop.arrival} — {stop.departure}
                      </span>
                      <span className={stop.indoor ? 'indoor-tag' : 'outdoor-tag'}>
                        {stop.indoor ? '室内' : '户外'} · {stop.category}
                      </span>
                    </div>
                    <h3>{stop.name}</h3>
                    <p>{stop.note}</p>
                    <div className="stop-bottom">
                      <span>
                        {stop.cost ? `${money(Math.round(stop.cost * 100))} 估算` : '活动预算 A$0'}{' '}
                        · {stop.visitMinutes} 分钟
                      </span>
                      <a href={mapLink(stop)} target="_blank" rel="noreferrer">
                        Google Maps
                        <Icon name="external" size={12} />
                      </a>
                    </div>
                    {!stop.hoursVerified && <small className="unverified">营业时间待核实</small>}
                  </div>
                </div>
              </article>
            ))}
            <div className="walk-leg return-leg">
              <Icon name="walk" size={13} />
              返回步行约 {day.legs.at(-1)?.minutes ?? '—'} 分钟
            </div>
            <div className="journey-endpoint end">
              <span className="endpoint-dot" />
              <div>
                <strong>
                  {day.finishTime} 回到 {day.end.name}
                </strong>
                <small>留一点余力，给下一次出发。</small>
              </div>
            </div>
          </section>
        </div>
        <aside className="trip-aside">
          <RouteSketch day={day} />
          <section className="weather-card card">
            <div className="weather-heading">
              <span className="round-icon">
                <Icon
                  name={unknownWeather || forecast?.rainy || currentWeather.rainy ? 'cloud' : 'sun'}
                />
              </span>
              <div>
                <span className="eyebrow">LOOKING UP</span>
                <h3>出发前，看看天气</h3>
              </div>
              <button
                type="button"
                className="icon-button"
                title="刷新真实天气"
                aria-label="刷新真实天气"
                disabled={busy}
                onClick={onWeather}
              >
                <Icon name="refresh" size={18} />
              </button>
            </div>
            <p>{forecast?.summary ?? currentWeather.summary}</p>
            <small>
              {currentWeather.source}
              {currentWeather.status &&
                ` · ${currentWeather.status === 'ok' ? '已获取' : '部分或暂不可用'}`}
            </small>
            <button className="text-button" type="button" disabled={busy} onClick={onRain}>
              试试下雨天的安排
              <Icon name="arrow" size={14} />
            </button>
            <span className="weather-note">情景测试将生成草案，确认后才替换行程。</span>
          </section>
        </aside>
      </div>
      <section className="evidence-card card">
        <details>
          <summary>
            <span>
              <Icon name="leaf" />
              为什么这样安排？<small>{modeLabels[trip.mode]}</small>
            </span>
            <span>＋</span>
          </summary>
          <p className="explanation">{trip.explanation}</p>
          <div className="trace-grid">
            {trip.trace.map((item, i) => (
              <article key={`${item.title}-${i}`}>
                <span className="trace-index">0{i + 1}</span>
                <h4>{item.title}</h4>
                <p>{item.detail}</p>
                <small>{item.source}</small>
              </article>
            ))}
          </div>
          <div className="warning-list">
            <h4>出发前请了解</h4>
            <ul>
              {[...new Set([...trip.warnings, ...trip.days.flatMap((d) => d.warnings)])].map(
                (warning) => (
                  <li key={warning}>{warning}</li>
                ),
              )}
            </ul>
          </div>
        </details>
      </section>
    </>
  );
}
